namespace StockYouNeed.Application.Options;

public sealed class DatabaseOptions
{
    public const string SectionName = "Database";
    public string ConnectionString { get; set; } = "";
}

public sealed class AngelOptions
{
    public const string SectionName = "Angel";
    public string ApiKey { get; set; } = "";
    public string ClientCode { get; set; } = "";
    public string Password { get; set; } = "";
    public string TotpSecret { get; set; } = "";
    public string BaseUrl { get; set; } = "https://apiconnect.angelone.in";
    public string ScripMasterUrl { get; set; } =
        "https://margincalculator.angelone.in/OpenAPI_File/files/OpenAPIScripMaster.json";
    /// <summary>When false, workers skip live Angel calls (useful for local UI wiring).</summary>
    public bool Enabled { get; set; } = true;
    /// <summary>Minimum gap between any Angel HTTP call (login, quote, candles).</summary>
    public int MinRequestIntervalMs { get; set; } = 1000;
    /// <summary>After a login rate-limit (403), block further login attempts for this many minutes.</summary>
    public int LoginCooldownMinutes { get; set; } = 3;
}

public sealed class WorkerScheduleOptions
{
    public const string SectionName = "WorkerSchedule";
    /// <summary>IST hour (0-23) to run daily token + bars sync.</summary>
    public int DailySyncHourIst { get; set; } = 8;
    public int LtpPollIntervalSeconds { get; set; } = 5;
    public int MarketBarsLookbackDays { get; set; } = 60;
}

public sealed class DevAuthOptions
{
    public const string SectionName = "DevAuth";
    public Guid DemoUserId { get; set; } = Guid.Parse("11111111-1111-1111-1111-111111111111");
    public string DemoEmail { get; set; } = "demo@stockyouneed.local";
    public string DemoDisplayName { get; set; } = "Demo User";
}
