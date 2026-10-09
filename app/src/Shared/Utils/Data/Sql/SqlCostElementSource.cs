using System.Data;
using Dapper;

namespace PzlEv.Shared.Utils.Data.Sql;

/// <summary>Cost elementy ostatniego importu ACTUALS (procedura CAN_ActualsCostElements, migracja 018).</summary>
public sealed class SqlCostElementSource(SqlDatabase db) : ICostElementSource
{
    public IReadOnlyList<ActualsCostElement> CostElements()
    {
        using var connection = db.Open();
        return connection.Query<(string CostElement, string? Name, int Rows, decimal? Amount)>(db.Table("can.ActualsCostElements"),
                commandType: CommandType.StoredProcedure, commandTimeout: 600)
            .Select(r => new ActualsCostElement(r.CostElement.Trim(), string.IsNullOrWhiteSpace(r.Name) ? null : r.Name.Trim(), r.Rows, r.Amount ?? 0))
            .ToList();
    }
}
