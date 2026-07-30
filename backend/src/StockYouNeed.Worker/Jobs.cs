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
