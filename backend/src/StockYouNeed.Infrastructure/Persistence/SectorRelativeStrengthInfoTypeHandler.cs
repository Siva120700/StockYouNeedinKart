using System.Data;
using Dapper;
using StockYouNeed.Domain;

namespace StockYouNeed.Infrastructure.Persistence;

/// <summary>
/// SectorRs is GraphQL-only overlay — never persist. Without a handler Dapper
/// fails Insert* with "cannot be used as a parameter value".
/// </summary>
public sealed class SectorRelativeStrengthInfoTypeHandler : SqlMapper.TypeHandler<SectorRelativeStrengthInfo?>
{
    public override void SetValue(IDbDataParameter parameter, SectorRelativeStrengthInfo? value)
    {
        parameter.Value = DBNull.Value;
    }

    public override SectorRelativeStrengthInfo? Parse(object value)
        => null;
}
