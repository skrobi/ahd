using PzlEv.Shared.Models.Db;
using PzlEv.Shared.Models.Sources;

namespace PzlEv.Shared.Utils.Data.Sql;

/// <summary>Wiersze tabel META_SourceDefinition, META_SourceLocation i META_Parser (wspólne dla Administracji i Importu).</summary>
public sealed class SqlDefinitionRow
{
    public const string Columns =
        "Id, DefinitionId, Version, Code, Prefix, ReportType, Columns AS ColumnsJson, Signature, Parser, ParserVersion, Mapping AS MappingJson, Active, " +
        "RecordedAt, RecordedBy, SupersededAt, SupersededBy";

    public long Id { get; set; }
    public long DefinitionId { get; set; }
    public int Version { get; set; }
    public string Code { get; set; } = "";
    public string Prefix { get; set; } = "";
    public string ReportType { get; set; } = "";
    public string ColumnsJson { get; set; } = "[]";
    public string Signature { get; set; } = "";
    public string Parser { get; set; } = "";
    public int ParserVersion { get; set; }
    public string? MappingJson { get; set; }
    public bool Active { get; set; }
    public DateTimeOffset RecordedAt { get; set; }
    public string RecordedBy { get; set; } = "";
    public DateTimeOffset? SupersededAt { get; set; }
    public string? SupersededBy { get; set; }

    public SourceDefinitionRow ToRow() =>
        new(Id, DefinitionId, Version, Code, Prefix, ReportType, SqlJson.Strings(ColumnsJson), Signature, Parser, ParserVersion,
            SqlJson.List<ColumnMapping>(MappingJson), Active, RecordedAt, RecordedBy, SupersededAt, SupersededBy);
}

public sealed class SqlLocationRow
{
    public const string Columns = "Id, LocationId, Version, Name, Path, Active, RecordedAt, RecordedBy, SupersededAt, SupersededBy";

    public long Id { get; set; }
    public long LocationId { get; set; }
    public int Version { get; set; }
    public string Name { get; set; } = "";
    public string Path { get; set; } = "";
    public bool Active { get; set; }
    public DateTimeOffset RecordedAt { get; set; }
    public string RecordedBy { get; set; } = "";
    public DateTimeOffset? SupersededAt { get; set; }
    public string? SupersededBy { get; set; }

    public SourceLocationRow ToRow() => new(Id, LocationId, Version, Name, Path, Active, RecordedAt, RecordedBy, SupersededAt, SupersededBy);
}

public sealed class SqlParserRow
{
    public const string Columns = "Id, ParserId, Version, Code, Name, TableName, Fields AS FieldsJson, Active, RecordedAt, RecordedBy, SupersededAt, SupersededBy";

    public long Id { get; set; }
    public long ParserId { get; set; }
    public int Version { get; set; }
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string TableName { get; set; } = "";
    public string FieldsJson { get; set; } = "[]";
    public bool Active { get; set; }
    public DateTimeOffset RecordedAt { get; set; }
    public string RecordedBy { get; set; } = "";
    public DateTimeOffset? SupersededAt { get; set; }
    public string? SupersededBy { get; set; }

    public ParserRow ToRow() =>
        new(Id, ParserId, Version, Code, Name, TableName, SqlJson.List<ParserField>(FieldsJson), Active, RecordedAt, RecordedBy, SupersededAt, SupersededBy);
}
