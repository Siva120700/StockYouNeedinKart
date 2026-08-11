using System.Data;
using Dapper;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Npgsql;
using StockYouNeed.Application.Abstractions;
using StockYouNeed.Application.News;
using StockYouNeed.Application.Options;
using StockYouNeed.Infrastructure.Angel;
using StockYouNeed.Infrastructure.Persistence;

namespace StockYouNeed.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        SqlMapper.AddTypeHandler(new DateOnlyTypeHandler());

        services.Configure<DatabaseOptions>(configuration.GetSection(DatabaseOptions.SectionName));
        services.Configure<AngelOptions>(configuration.GetSection(AngelOptions.SectionName));
        services.Configure<WorkerScheduleOptions>(configuration.GetSection(WorkerScheduleOptions.SectionName));
        services.Configure<DevAuthOptions>(configuration.GetSection(DevAuthOptions.SectionName));
        services.Configure<NewsOptions>(configuration.GetSection(NewsOptions.SectionName));

        services.AddMemoryCache();
        services.AddSingleton<IDbConnectionFactory, NpgsqlConnectionFactory>();
        // Singleton so JWT session is reused (transient typed client was re-logging in every scope → 403 rate limit).
        services.AddHttpClient<AngelMarketDataClient>();
        services.AddSingleton<AngelMarketDataClient>();
        services.AddSingleton<IAngelMarketDataClient>(sp => sp.GetRequiredService<AngelMarketDataClient>());
        services.AddHttpClient(MarketNewsService.HttpClientName, client =>
        {
            client.Timeout = TimeSpan.FromSeconds(20);
            client.DefaultRequestHeaders.TryAddWithoutValidation(
                "User-Agent",
                "StockYouNeed/1.0 (+https://localhost)");
            client.DefaultRequestHeaders.TryAddWithoutValidation(
                "Accept",
                "application/rss+xml, application/xml, text/xml, */*");
        });
        services.AddScoped<IInstrumentRepository, InstrumentRepository>();
        services.AddScoped<IMarketDataRepository, MarketDataRepository>();
        services.AddScoped<IPortfolioRepository, PortfolioRepository>();
        services.AddScoped<IBacktestRepository, BacktestRepository>();
        services.AddScoped<ITradeScoreRepository, TradeScoreRepository>();
        services.AddScoped<IBreakoutRepository, BreakoutRepository>();
        services.AddScoped<ISignalOutcomeRepository, SignalOutcomeRepository>();
        services.AddScoped<IOptionsIntradayRepository, OptionsIntradayRepository>();
        services.AddScoped<INiftyOrbRepository, NiftyOrbRepository>();
        services.AddScoped<IIndexOptionNotificationRepository, IndexOptionNotificationRepository>();
        services.AddSingleton<DatabaseMigrator>();

        return services;
    }
}

public sealed class NpgsqlConnectionFactory : IDbConnectionFactory
{
    private readonly string _connectionString;

    public NpgsqlConnectionFactory(IOptions<DatabaseOptions> options)
    {
        _connectionString = options.Value.ConnectionString
            ?? throw new InvalidOperationException("Database:ConnectionString is required.");
    }

    public IDbConnection CreateConnection()
    {
        var conn = new NpgsqlConnection(_connectionString);
        conn.Open();
        return conn;
    }
}
