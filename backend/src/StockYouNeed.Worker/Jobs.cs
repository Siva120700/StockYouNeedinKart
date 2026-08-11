using Microsoft.Extensions.Options;
using StockYouNeed.Application.Abstractions;
using StockYouNeed.Application.Options;
using StockYouNeed.Application.Services;

namespace StockYouNeed.Worker;

public sealed class DailySyncHostedService : BackgroundService
{
    private readonly IServiceProvider _sp;
    private readonly WorkerScheduleOptions _schedule;
    private readonly ILogger<DailySyncHostedService> _logger;
    private DateOnly? _lastRunDateIst;

    public DailySyncHostedService(
        IServiceProvider sp,
        IOptions<WorkerScheduleOptions> schedule,
        ILogger<DailySyncHostedService> logger)
    {
        _sp = sp;
        _schedule = schedule.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Run once shortly after start, then daily at configured IST hour
        await Task.Delay(TimeSpan.FromSeconds(3), stoppingToken);
        await RunDailyPipelineAsync(stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var istNow = DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(5.5));
                var today = DateOnly.FromDateTime(istNow.DateTime);
                if (istNow.Hour == _schedule.DailySyncHourIst && _lastRunDateIst != today)
                {
                    await RunDailyPipelineAsync(stoppingToken);
                    _lastRunDateIst = today;
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Daily sync loop error");
            }

            await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
        }
    }

    private async Task RunDailyPipelineAsync(CancellationToken ct)
    {
        using var scope = _sp.CreateScope();
        var seed = scope.ServiceProvider.GetRequiredService<UniverseSeedService>();
        var tokens = scope.ServiceProvider.GetRequiredService<TokenSyncService>();
        var bars = scope.ServiceProvider.GetRequiredService<MarketBarsSyncService>();
        var instruments = scope.ServiceProvider.GetRequiredService<IInstrumentRepository>();
        var auth = scope.ServiceProvider.GetRequiredService<IOptions<DevAuthOptions>>().Value;

        _logger.LogInformation("Starting daily seed + token + 10-day bars sync…");
        await instruments.EnsureDemoUserAsync(auth.DemoUserId, auth.DemoEmail, auth.DemoDisplayName, ct);
        await seed.SeedAsync(ct);
        await tokens.SyncUniverseTokensAsync(ct);
        await bars.SyncLastNTradingDaysAsync(ct);

        try
        {
            var outcomes = scope.ServiceProvider.GetRequiredService<StockYouNeed.Application.Outcomes.SignalOutcomeService>();
            var resolved = await outcomes.ResolveOpenAsync(auth.DemoUserId, ct);
            _logger.LogInformation("Forward outcome resolve finished: {Resolved} closed", resolved);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Forward outcome resolve failed");
        }

        _lastRunDateIst = DateOnly.FromDateTime(DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(5.5)).DateTime);
        _logger.LogInformation("Daily sync finished.");
    }
}

public sealed class LtpPollHostedService : BackgroundService
{
    private readonly IServiceProvider _sp;
    private readonly WorkerScheduleOptions _schedule;
    private readonly ILogger<LtpPollHostedService> _logger;

    public LtpPollHostedService(
        IServiceProvider sp,
        IOptions<WorkerScheduleOptions> schedule,
        ILogger<LtpPollHostedService> logger)
    {
        _sp = sp;
        _schedule = schedule.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Always refresh once on startup (Angel returns last LTP even off-hours).
        await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
        await PollSafeAsync(stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await PollSafeAsync(stoppingToken);

                // Market hours: configured interval. Off-hours: every 15 min so weekend/holiday
                // still get a periodic last-traded refresh without hammering Angel.
                var delaySec = IsLikelyMarketHoursIst()
                    ? Math.Max(2, _schedule.LtpPollIntervalSeconds)
                    : 15 * 60;
                await Task.Delay(TimeSpan.FromSeconds(delaySec), stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "LTP poll loop error");
                await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
            }
        }
    }

    private async Task PollSafeAsync(CancellationToken ct)
    {
        try
        {
            using var scope = _sp.CreateScope();
            var poller = scope.ServiceProvider.GetRequiredService<LtpPollService>();
            var n = await poller.PollOnceAsync(ct);
            if (n > 0)
                _logger.LogInformation("LTP updated {Count} instruments", n);
            else
                _logger.LogDebug("LTP poll completed with 0 updates");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "LTP poll error");
        }
    }

    private static bool IsLikelyMarketHoursIst()
    {
        var ist = DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(5.5));
        if (ist.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
            return false;
        var t = ist.TimeOfDay;
        return t >= TimeSpan.FromHours(9) && t <= TimeSpan.FromHours(15).Add(TimeSpan.FromMinutes(35));
    }
}

/// <summary>
/// Scans Nifty Index Options (ORB + optional Liq V2) during the session so a
/// recommendation appears as soon as a break/setup is found — not only on manual Run.
/// </summary>
public sealed class NiftyOrbPollHostedService : BackgroundService
{
    private static readonly TimeSpan Ist = TimeSpan.FromHours(5.5);
    private readonly IServiceProvider _sp;
    private readonly WorkerScheduleOptions _schedule;
    private readonly ILogger<NiftyOrbPollHostedService> _logger;

    public NiftyOrbPollHostedService(
        IServiceProvider sp,
        IOptions<WorkerScheduleOptions> schedule,
        ILogger<NiftyOrbPollHostedService> logger)
    {
        _sp = sp;
        _schedule = schedule.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_schedule.NiftyOrbPollEnabled)
        {
            _logger.LogInformation("Nifty ORB poll disabled in WorkerSchedule");
            return;
        }

        await Task.Delay(TimeSpan.FromSeconds(12), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (InScanWindowIst())
                    await ScanSafeAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Nifty ORB poll loop error");
            }

            var delaySec = Math.Max(60, _schedule.NiftyOrbPollIntervalSeconds);
            // Off-window: wake hourly to catch next session without busy-looping.
            if (!InScanWindowIst())
                delaySec = Math.Max(delaySec, 60 * 15);
            await Task.Delay(TimeSpan.FromSeconds(delaySec), stoppingToken);
        }
    }

    private async Task ScanSafeAsync(CancellationToken ct)
    {
        try
        {
            using var scope = _sp.CreateScope();
            var auth = scope.ServiceProvider.GetRequiredService<IOptions<DevAuthOptions>>().Value;
            var niftyOrb = scope.ServiceProvider
                .GetRequiredService<StockYouNeed.Application.IndexOptions.NiftyOrbService>();
            var instruments = scope.ServiceProvider.GetRequiredService<IInstrumentRepository>();
            await instruments.EnsureDemoUserAsync(auth.DemoUserId, auth.DemoEmail, auth.DemoDisplayName, ct);

            var run = await niftyOrb.RunAsync(auth.DemoUserId, ct);
            var recs = await niftyOrb.GetRecommendationsAsync(auth.DemoUserId, run.Id, ct);
            var orb = recs.FirstOrDefault(r => r.SignalSource == "nifty_orb"
                || string.IsNullOrEmpty(r.SignalSource));
            var combo = recs.FirstOrDefault(r => r.SignalSource == "nifty_orb_liq_v2");
            var liqBo = recs.FirstOrDefault(r => r.SignalSource == "nifty_liq_breakout");
            _logger.LogInformation(
                "Nifty ORB auto-scan: run={Status} orb={OrbStatus} combo={ComboStatus} liqBo={LiqBoStatus}",
                run.Status,
                orb?.Status ?? "(none)",
                combo?.Status ?? "(none)",
                liqBo?.Status ?? "(none)");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Nifty ORB auto-scan failed");
        }
    }

    /// <summary>9:15–14:30 IST weekdays — OR forming + break watch until flat.</summary>
    private static bool InScanWindowIst()
    {
        var ist = DateTimeOffset.UtcNow.ToOffset(Ist);
        if (ist.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
            return false;
        var t = TimeOnly.FromDateTime(ist.DateTime);
        return t >= new TimeOnly(9, 15) && t < new TimeOnly(14, 30);
    }
}
