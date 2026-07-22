using Microsoft.Extensions.DependencyInjection;
using StockYouNeed.Application.Services;

namespace StockYouNeed.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddScoped<TokenSyncService>();
        services.AddScoped<MarketBarsSyncService>();
        services.AddScoped<LtpPollService>();
        services.AddScoped<AnalysisRunService>();
        services.AddScoped<UniverseSeedService>();
        return services;
    }
}
