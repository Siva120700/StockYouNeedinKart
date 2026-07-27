namespace StockYouNeed.Application.Abstractions;

/// <summary>
/// Angel SmartAPI getCandleData max calendar days per request (per interval).
/// See: https://smartapi.angelone.in/docs — Historical API.
/// </summary>
public static class AngelHistoricalLimits
{
    public const int OneMinute = 30;
    public const int ThreeMinute = 60;
    public const int FiveMinute = 100;
    public const int TenMinute = 100;
    public const int FifteenMinute = 200;
    public const int ThirtyMinute = 200;
    public const int OneHour = 400;
    public const int OneDay = 2000;
}
