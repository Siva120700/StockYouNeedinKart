using Microsoft.Extensions.Logging;
using StockYouNeed.Application.Abstractions;
using StockYouNeed.Application.IndexOptions;
using StockYouNeed.Domain;

namespace StockYouNeed.Application.IndexOptions;

/// <summary>
/// Alerts when a recommended Nifty index option strike meets high-probability thresholds.
/// </summary>
public sealed class IndexOptionNotificationService
{
    public const int MinConfidenceOrb = 80;
    public const int MinConfidenceCombo = 85;

    private static readonly TimeSpan Ist = TimeSpan.FromHours(5.5);

    private readonly IIndexOptionNotificationRepository _repo;
    private readonly ILogger<IndexOptionNotificationService> _logger;

    public IndexOptionNotificationService(
        IIndexOptionNotificationRepository repo,
        ILogger<IndexOptionNotificationService> logger)
    {
        _repo = repo;
        _logger = logger;
    }

    public static bool IsHighProbability(NiftyOrbRecommendationRow rec)
    {
        if (!string.Equals(rec.Status, "recommended", StringComparison.OrdinalIgnoreCase))
            return false;
        if (rec.ContractStrike is null or <= 0)
            return false;
        if (rec.PremiumLtp is null or <= 0)
            return false;

        return rec.SignalSource switch
        {
            NiftyOrbService.SourceOrbLiqV2
                => rec.ConfidenceScore >= MinConfidenceCombo,
            NiftyOrbService.SourceOrb or NiftyOrbService.SourceLiqBreakout
                or NiftyOrbService.SourceBreakoutVolume
                or NiftyOrbService.SourceBreakoutChain
                => rec.ConfidenceScore >= MinConfidenceOrb,
            NiftyOrbService.SourceHeroZero => false,
            _ => rec.ConfidenceScore >= MinConfidenceOrb,
        };
    }

    public async Task<bool> TryNotifyAsync(NiftyOrbRecommendationRow rec, CancellationToken ct = default)
    {
        if (!IsHighProbability(rec))
            return false;

        var asOf = DateOnly.FromDateTime(DateTimeOffset.UtcNow.ToOffset(Ist).DateTime);
        var strategy = SourceLabel(rec.SignalSource);
        var strikeLabel = $"{rec.ContractStrike:0.##} {rec.ContractOptionType}";
        var action = rec.Side == SignalSides.Buy ? "Buy CE" : "Buy PE";

        var title = $"NIFTY {action} · {strikeLabel} ({rec.ConfidenceScore}%)";
        var body =
            $"{strategy}: {action} @ ₹{rec.PremiumLtp:0.00} | Prem SL ₹{rec.PremiumStopLoss:0.00} | Prem T1 ₹{rec.PremiumTargetT1:0.00}";

        var row = new IndexOptionNotificationRow
        {
            Id = Guid.NewGuid(),
            UserId = rec.UserId,
            RecommendationId = rec.Id,
            SignalSource = rec.SignalSource,
            Side = rec.Side,
            AsOfDate = asOf,
            ContractStrike = rec.ContractStrike.Value,
            ContractOptionType = rec.ContractOptionType ?? "",
            PremiumLtp = rec.PremiumLtp.Value,
            PremiumStopLoss = rec.PremiumStopLoss,
            PremiumTargetT1 = rec.PremiumTargetT1,
            ConfidenceScore = rec.ConfidenceScore,
            Title = title,
            Body = body,
        };

        var inserted = await _repo.TryInsertAsync(row, ct);
        if (inserted)
        {
            _logger.LogInformation(
                "Index option notification: {Title} ({Source} confidence={Score})",
                title, rec.SignalSource, rec.ConfidenceScore);
        }

        return inserted;
    }

    public Task<IReadOnlyList<IndexOptionNotificationRow>> GetAsync(
        Guid userId, bool unreadOnly, int limit, CancellationToken ct = default)
        => _repo.GetAsync(userId, unreadOnly, limit, ct);

    public Task<int> MarkReadAsync(Guid userId, IReadOnlyList<Guid> ids, CancellationToken ct = default)
        => _repo.MarkReadAsync(userId, ids, ct);

    private static string SourceLabel(string source) => source switch
    {
        NiftyOrbService.SourceOrbLiqV2 => "ORB + Liquidity V2",
        NiftyOrbService.SourceLiqBreakout => "Liquidity + Breakout",
        NiftyOrbService.SourceBreakoutVolume => "Breakout + Volume",
        NiftyOrbService.SourceBreakoutChain => "Breakout + Chain",
        NiftyOrbService.SourceHeroZero => "Hero Zero",
        _ => "Nifty ORB",
    };
}
