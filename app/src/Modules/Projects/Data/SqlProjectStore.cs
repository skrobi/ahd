using Dapper;
using Microsoft.Data.SqlClient;
using PzlEv.Modules.Projects.Models;
using PzlEv.Shared.Models.Db;
using PzlEv.Shared.Models.Dictionaries;
using PzlEv.Shared.Utils.Data;
using PzlEv.Shared.Utils.Data.Sql;

namespace PzlEv.Modules.Projects.Data;

/// <summary>
/// Projekty w bazie: META_Project (wersje projektu) i META_PerformanceObjective (wersje węzłów nakładki; NodeId
/// i ProjectId z sekwencji META_LogicalId). Historia na osi technicznej – RecordedAt / SupersededAt.
/// </summary>
public sealed class SqlProjectStore(SqlDatabase db, IClock clock, ICurrentUser user) : IProjectStore
{
    private const string NodeColumns =
        "NodeId, Version, ParentNodeId, NodeLevel, SortOrder, IsVirtual, ProjectDefinition, WbsElement, Name, PersonResponsible, "
        + "ProfitCenter, LegacyWbs, PerformanceObligation, SacObjNumber, IsStatistical, IsAcctAsstElement";

    private string ProjectTable => db.Table("meta.Project");

    private string NodeTable => db.Table("meta.PerformanceObjective");

    public IReadOnlyList<ProjectInfo> Projects()
    {
        using var connection = db.Open();
        return connection.Query<ProjectRow>($"SELECT ProjectId, Version, Code, Name, ProjectType, RecordedAt, RecordedBy FROM {ProjectTable} WHERE SupersededAt IS NULL AND Active = 1 ORDER BY Code")
            .Select(r => r.ToInfo()).ToList();
    }

    public ProjectInfo? Find(string code)
    {
        using var connection = db.Open();
        return connection.QuerySingleOrDefault<ProjectRow>(
            $"SELECT ProjectId, Version, Code, Name, ProjectType, RecordedAt, RecordedBy FROM {ProjectTable} WHERE SupersededAt IS NULL AND Code = @code",
            new { code })?.ToInfo();
    }

    public PoTree Objectives(string code)
    {
        using var connection = db.Open();
        return new PoTree(ReadNodes(connection, null, code, lockRows: false).Select(r => r.ToNode()));
    }

    public IReadOnlyDictionary<string, string> WbsOwners(string exceptCode)
    {
        using var connection = db.Open();
        return Owners(connection, null, exceptCode);
    }

    public IReadOnlyDictionary<string, string> P1sOwners(string exceptCode)
    {
        using var connection = db.Open();
        return connection.Query<(string Element, string Project)>(
                $"SELECT P1sElement, Project FROM {db.Table("dict.WpCam")} WHERE SupersededAt IS NULL AND Project <> @exceptCode",
                new { exceptCode })
            .GroupBy(r => r.Element, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First().Project, StringComparer.OrdinalIgnoreCase);
    }

    public StoreResult Create(string code, string name, string type, PoTree objectives)
    {
        try
        {
            return db.InTransaction((connection, transaction) =>
            {
                var taken = connection.ExecuteScalar<int>(
                    $"SELECT COUNT(*) FROM {ProjectTable} WITH (UPDLOCK, HOLDLOCK) WHERE SupersededAt IS NULL AND Code = @code", new { code }, transaction);
                if (taken > 0)
                    return StoreResult.Rejected($"Projekt o kodzie {code} już istnieje.");
                if (OwnerConflict(connection, transaction, code, objectives) is { } conflict)
                    return StoreResult.Rejected(conflict);

                var now = clock.Now;
                connection.Execute(
                    $"INSERT INTO {ProjectTable} (ProjectId, Version, Code, Name, ProjectType, Active, RecordedAt, RecordedBy) VALUES (@id, 1, @code, @name, @type, 1, @now, @user)",
                    new { id = db.NextLogicalId(connection, transaction), code, name, type, now, user = user.Account }, transaction);
                var added = InsertNodes(connection, transaction, code, objectives, objectives.Nodes, new Dictionary<long, NodeRow>(), now);
                return new StoreResult(true, null, added, 0, 0);
            });
        }
        catch (SqlException ex) when (SqlDatabase.IsDuplicateKey(ex))
        {
            return StoreResult.Rejected($"Projekt o kodzie {code} albo element nakładki został w międzyczasie zapisany przez inną osobę – odśwież dane.");
        }
    }

    public StoreResult SaveObjectives(string code, PoTree objectives)
    {
        try
        {
            return db.InTransaction((connection, transaction) =>
            {
                var current = ReadNodes(connection, transaction, code, lockRows: true).ToDictionary(r => r.NodeId);
                foreach (var node in objectives.Nodes.Where(n => n.NodeId is not null))
                {
                    if (!current.TryGetValue(node.NodeId!.Value, out var existing))
                        return StoreResult.Rejected($"Węzeł „{node.Name}” został w międzyczasie usunięty – odśwież nakładkę.");
                    if (existing.Version != node.Version)
                        return StoreResult.Rejected($"Węzeł „{node.Name}” zmienił {existing.RecordedBy} ({existing.RecordedAt:yyyy-MM-dd HH:mm}) – odśwież nakładkę.");
                }
                if (OwnerConflict(connection, transaction, code, objectives) is { } conflict)
                    return StoreResult.Rejected(conflict);

                var keep = objectives.Nodes.Where(n => n.NodeId is not null).Select(n => n.NodeId!.Value).ToHashSet();
                var removed = current.Keys.Where(id => !keep.Contains(id)).ToList();

                // Identyfikatory nowych węzłów – rodzic może być nowym węzłem.
                var ids = objectives.Nodes.Where(n => n.NodeId is not null).ToDictionary(n => n.Key, n => n.NodeId!.Value);
                var changed = objectives.Nodes
                    .Where(n => n.NodeId is { } id && !current[id].ToNode().SameContent(WithParentIds(n, ids)))
                    .ToList();
                var added = objectives.Nodes.Where(n => n.NodeId is null).ToList();
                if (removed.Count + changed.Count + added.Count == 0)
                    return new StoreResult(true, null, 0, 0, 0);

                var now = clock.Now;
                foreach (var id in removed.Concat(changed.Select(n => n.NodeId!.Value)))
                {
                    connection.Execute($"UPDATE {NodeTable} SET SupersededAt = @now, SupersededBy = @user WHERE NodeId = @id AND SupersededAt IS NULL",
                        new { now, user = user.Account, id }, transaction);
                }
                InsertNodes(connection, transaction, code, objectives, added.Concat(changed).ToList(), current, now);
                return new StoreResult(true, null, added.Count, changed.Count, removed.Count);
            });
        }
        catch (SqlException ex) when (SqlDatabase.IsDuplicateKey(ex))
        {
            return StoreResult.Rejected("Element CES nakładki został w międzyczasie zapisany przez inną osobę – odśwież nakładkę.");
        }
    }

    /// <summary>Wstawia wersje węzłów: nowe (NodeId z sekwencji, wersja 1) i zmienione (wersja + 1). Zwraca liczbę wierszy.</summary>
    private int InsertNodes(SqlConnection connection, SqlTransaction transaction, string code, PoTree tree, IReadOnlyList<PoNode> nodes,
        IReadOnlyDictionary<long, NodeRow> current, DateTimeOffset now)
    {
        var ids = tree.Nodes.Where(n => n.NodeId is not null).ToDictionary(n => n.Key, n => n.NodeId!.Value);
        foreach (var node in nodes.Where(n => n.NodeId is null))
            ids[node.Key] = db.NextLogicalId(connection, transaction);
        foreach (var node in nodes)
        {
            var version = node.NodeId is { } id && current.TryGetValue(id, out var existing) ? existing.Version + 1 : 1;
            connection.Execute(
                $"""
                INSERT INTO {NodeTable} (NodeId, Version, Project, ParentNodeId, NodeLevel, SortOrder, IsVirtual, ProjectDefinition, WbsElement, Name,
                    PersonResponsible, ProfitCenter, LegacyWbs, PerformanceObligation, SacObjNumber, IsStatistical, IsAcctAsstElement, RecordedAt, RecordedBy)
                VALUES (@nodeId, @version, @code, @parent, @level, @sortOrder, @isVirtual, @projectDefinition, @wbs, @name,
                    @person, @profitCenter, @legacy, @obligation, @sacObj, @statistical, @acctAsst, @now, @user)
                """,
                new
                {
                    nodeId = ids[node.Key], version, code, parent = node.ParentKey is { } p ? ids[p] : (long?)null, level = node.Level,
                    sortOrder = node.SortOrder, isVirtual = node.IsVirtual, projectDefinition = node.ProjectDefinition,
                    wbs = node.IsVirtual ? null : node.WbsElement, name = node.Name, person = node.PersonResponsible, profitCenter = node.ProfitCenter,
                    legacy = node.LegacyWbs, obligation = node.PerformanceObligation, sacObj = node.SacObjNumber,
                    statistical = node.IsStatistical, acctAsst = node.IsAcctAsstElement, now, user = user.Account,
                },
                transaction);
        }
        return nodes.Count;
    }

    /// <summary>Węzeł z rodzicem jako NodeId (porównanie z wersją w bazie).</summary>
    private static PoNode WithParentIds(PoNode node, IReadOnlyDictionary<long, long> ids)
    {
        var copy = node.Copy();
        copy.ParentKey = node.ParentKey is { } p ? ids.GetValueOrDefault(p, p) : null;
        return copy;
    }

    private string? OwnerConflict(SqlConnection connection, SqlTransaction transaction, string code, PoTree tree)
    {
        var owners = Owners(connection, transaction, code);
        var taken = tree.Nodes
            .Where(n => !n.IsVirtual && n.WbsElement is not null && owners.ContainsKey(n.WbsElement))
            .Select(n => $"{n.WbsElement} ({owners[n.WbsElement!]})")
            .ToList();
        return taken.Count == 0 ? null : $"Elementy CES należą już do nakładki innego projektu: {string.Join(", ", taken.Take(10))}{(taken.Count > 10 ? "…" : "")}";
    }

    private Dictionary<string, string> Owners(SqlConnection connection, SqlTransaction? transaction, string exceptCode) =>
        connection.Query<(string Wbs, string Project)>(
                $"SELECT WbsElement, Project FROM {NodeTable} {(transaction is null ? "" : "WITH (UPDLOCK, HOLDLOCK)")} WHERE SupersededAt IS NULL AND WbsElement IS NOT NULL AND Project <> @exceptCode",
                new { exceptCode }, transaction)
            .GroupBy(r => r.Wbs, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First().Project, StringComparer.OrdinalIgnoreCase);

    private List<NodeRow> ReadNodes(SqlConnection connection, SqlTransaction? transaction, string code, bool lockRows) =>
        connection.Query<NodeRow>(
            $"SELECT {NodeColumns}, RecordedAt, RecordedBy FROM {NodeTable} {(lockRows ? "WITH (UPDLOCK, HOLDLOCK)" : "")} WHERE Project = @code AND SupersededAt IS NULL ORDER BY ParentNodeId, SortOrder",
            new { code }, transaction).ToList();

    private sealed class ProjectRow
    {
        public long ProjectId { get; set; }
        public int Version { get; set; }
        public string Code { get; set; } = "";
        public string Name { get; set; } = "";
        public string ProjectType { get; set; } = "";
        public DateTimeOffset RecordedAt { get; set; }
        public string RecordedBy { get; set; } = "";

        public ProjectInfo ToInfo() => new(ProjectId, Version, Code, Name, ProjectType, RecordedAt, RecordedBy);
    }

    private sealed class NodeRow
    {
        public long NodeId { get; set; }
        public int Version { get; set; }
        public long? ParentNodeId { get; set; }
        public int NodeLevel { get; set; }
        public int SortOrder { get; set; }
        public bool IsVirtual { get; set; }
        public string? ProjectDefinition { get; set; }
        public string? WbsElement { get; set; }
        public string Name { get; set; } = "";
        public string? PersonResponsible { get; set; }
        public string? ProfitCenter { get; set; }
        public string? LegacyWbs { get; set; }
        public string? PerformanceObligation { get; set; }
        public string? SacObjNumber { get; set; }
        public bool IsStatistical { get; set; }
        public bool IsAcctAsstElement { get; set; }
        public DateTimeOffset RecordedAt { get; set; }
        public string RecordedBy { get; set; } = "";

        public PoNode ToNode() => new()
        {
            Key = NodeId, NodeId = NodeId, Version = Version, ParentKey = ParentNodeId, Level = NodeLevel, SortOrder = SortOrder,
            IsVirtual = IsVirtual, ProjectDefinition = ProjectDefinition, WbsElement = WbsElement, Name = Name,
            PersonResponsible = PersonResponsible, ProfitCenter = ProfitCenter, LegacyWbs = LegacyWbs,
            PerformanceObligation = PerformanceObligation, SacObjNumber = SacObjNumber, IsStatistical = IsStatistical,
            IsAcctAsstElement = IsAcctAsstElement,
        };
    }

    public (IReadOnlyList<string> Columns, IReadOnlyList<IReadOnlyList<object?>> Rows) CostReport(string code, string value)
    {
        using var connection = db.Open();
        using var reader = connection.ExecuteReader(db.Table("rep.ProjectCosts"), new { Project = code, Value = value },
            commandType: System.Data.CommandType.StoredProcedure, commandTimeout: 600);
        var columns = Enumerable.Range(0, reader.FieldCount).Select(reader.GetName).ToList();
        var rows = new List<IReadOnlyList<object?>>();
        while (reader.Read())
            rows.Add(Enumerable.Range(0, reader.FieldCount).Select(i => reader.IsDBNull(i) ? null : reader.GetValue(i)).ToList());
        return (columns, rows);
    }

    public IReadOnlyDictionary<string, decimal> CostsByElement(string code, string value)
    {
        using var connection = db.Open();
        return connection.Query<(string WbsElement, decimal? Amount)>(db.Table("rep.ProjectCostsByElement"), new { Project = code, Value = value },
                commandType: System.Data.CommandType.StoredProcedure, commandTimeout: 600)
            .ToDictionary(r => r.WbsElement, r => r.Amount ?? 0, StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyList<ActualsFile> ActualsFiles()
    {
        using var connection = db.Open();
        return connection.Query<ActualsFile>(
            $"SELECT f.FileName, f.ModifiedAt AS ReportAt, f.ImportedAt, s.ModifiedAt AS SeenReportAt, s.CheckedAt " +
            $"FROM {db.Table("can.LatestFiles")}('ACTUALS', NULL, NULL) f " +
            $"OUTER APPLY (SELECT TOP (1) x.ModifiedAt, b.StartedAt AS CheckedAt FROM {db.Table("meta.SourceFileSeen")} x " +
            $"  JOIN {db.Table("meta.ImportBatch")} b ON b.BatchId = x.BatchId " +
            $"  WHERE x.Location = f.Location AND x.FileName = f.FileName AND x.Decision IN @decisions ORDER BY x.Id DESC) s",
            new { decisions = new[] { FileDecisions.Imported, FileDecisions.Skipped, FileDecisions.Duplicate } }).ToList();
    }
}
