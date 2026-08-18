using Microsoft.Extensions.DependencyInjection;
using StockYouNeed.Application.Analyze;
using StockYouNeed.Application.News;
using StockYouNeed.Application.OptionsIntraday;
using StockYouNeed.Application.Outcomes;
using StockYouNeed.Application.Services;

namespace StockYouNeed.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddScoped<TokenSyncService>();
        services.AddScoped<MarketBarsSyncService>();
        services.AddScoped<IntradayBarsSyncService>();
        services.AddScoped<LtpPollService>();
        services.AddScoped<SignalOutcomeService>();
        services.AddScoped<NfoSyncService>();
        services.AddScoped<OptionsIntradayService>();
        services.AddScoped<Application.IndexOptions.NiftyOrbService>();
        services.AddScoped<Application.IndexOptions.NiftyOptionChainService>();
        services.AddScoped<Application.IndexOptions.IndexOptionNotificationService>();
        services.AddScoped<AnalysisRunService>();
        services.AddScoped<MomentumAnalysisService>();
        services.AddScoped<LiquidityAnalysisService>();
        services.AddScoped<Application.Confluence.ConfluenceService>();
        services.AddScoped<Application.Breakout.BreakoutAnalysisService>();
        services.AddScoped<TradeConfidenceService>();
        services.AddScoped<AnalyzeStockService>();
        services.AddScoped<Application.SectorScope.SectorScopeService>();
        services.AddScoped<BacktestService>();
        services.AddScoped<UniverseSeedService>();
        services.AddScoped<FnoUniverseSeedService>();
        services.AddScoped<MarketNewsService>();
        return services;
    }
}
