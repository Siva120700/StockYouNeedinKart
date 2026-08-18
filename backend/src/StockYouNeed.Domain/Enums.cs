namespace StockYouNeed.Domain;

public static class UniverseCodes
{
    public const string Nifty50 = "nifty_50";
    public const string Nifty100 = "nifty_100";
    public const string NiftyFno = "nifty_fno";

    /// <summary>All equity scan universes (Nifty 50 + 100 + F&amp;O underlyings).</summary>
    public static readonly string[] AllEquityScan = [Nifty50, Nifty100, NiftyFno];

    /// <summary>SQL IN-list for universe_memberships filters.</summary>
    public const string SqlEquityScanIn = "'nifty_50', 'nifty_100', 'nifty_fno'";
}

public static class QuoteModes
{
    public const string Ltp = "LTP";
    public const string Ohlc = "OHLC";
    public const string Full = "FULL";
}

public static class SignalSides
{
    public const string Buy = "buy";
    public const string Sell = "sell";
}

public static class AnalysisTriggers
{
    public const string FirstOpenOfDay = "first_open_of_day";
    public const string ManualRun = "manual_run";
}
