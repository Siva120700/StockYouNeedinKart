using StockYouNeed.Domain;

namespace StockYouNeed.Application.Signals;

/// <summary>
/// Blocks same-stock side flips when a prior opposite setup is still open/unresolved
/// within a short calendar window (default 2 days). Prevents noise like buy yesterday
/// then sell today on the same equity.
/// </summary>
public static class OppositeSignalFlipGuard
{
    public const int MaxCalendarDaysApart = 2;

    public static bool IsFlipAgainstOpen(
        Guid instrumentId,
        string newSide,
        DateOnly asOf,
        IEnumerable<SignalOutcomeRow> openOutcomes,
        out string? reason)
    {
        reason = null;
        if (string.IsNullOrWhiteSpace(newSide))
            return false;

        foreach (var prior in openOutcomes)
        {
            if (prior.InstrumentId != instrumentId)
                continue;
            if (string.Equals(prior.Side, newSide, StringComparison.OrdinalIgnoreCase))
                continue;
            if (!string.Equals(prior.Result, "open", StringComparison.OrdinalIgnoreCase))
                continue;

            var days = Math.Abs(asOf.DayNumber - prior.SignalDate.DayNumber);
            if (days > MaxCalendarDaysApart)
                continue;

            reason =
                $"Opposite open {prior.Strategy} {prior.Side} from {prior.SignalDate:yyyy-MM-dd} " +
                $"(within {MaxCalendarDaysApart}d) — skip new {newSide}";
            return true;
        }

        return false;
    }

    /// <summary>
    /// Historical replay: a prior note still blocks flips at <paramref name="asOf"/> if it has
    /// not exited yet (exit date null / after asOf, or result still open).
    /// </summary>
    public static bool IsFlipAgainstOpenNotes(
        string newSide,
        DateOnly asOf,
        IEnumerable<BacktestNoteRow> priorNotes,
        out string? reason)
    {
        reason = null;
        if (string.IsNullOrWhiteSpace(newSide))
            return false;

        foreach (var prior in priorNotes)
        {
            if (string.Equals(prior.Side, newSide, StringComparison.OrdinalIgnoreCase))
                continue;

            var stillOpenAtAsOf =
                string.Equals(prior.Result, "open", StringComparison.OrdinalIgnoreCase)
                || prior.ExitDate is null
                || prior.ExitDate > asOf;

            if (!stillOpenAtAsOf)
                continue;

            var days = Math.Abs(asOf.DayNumber - prior.SignalDate.DayNumber);
            if (days > MaxCalendarDaysApart)
                continue;

            reason =
                $"Opposite open {prior.Strategy} {prior.Side} from {prior.SignalDate:yyyy-MM-dd} " +
                $"(within {MaxCalendarDaysApart}d) — skip new {newSide}";
            return true;
        }

        return false;
    }
}
