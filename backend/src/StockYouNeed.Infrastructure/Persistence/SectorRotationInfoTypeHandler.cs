using System.Data;
using Dapper;
using StockYouNeed.Domain;

namespace StockYouNeed.Infrastructure.Persistence;

/// <summary>GraphQL-only overlay — never persist.</summary>
public sealed class SectorRotationInfoTypeHandler : SqlMapper.TypeHandler<SectorRotationInfo?>
{
    public override void SetValue(IDbDataParameter parameter, SectorRotationInfo? value)
        => parameter.Value = DBNull.Value;

    public override SectorRotationInfo? Parse(object value) => null;
}
