using System.Data;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Npgsql;
using StockYouNeed.Application.Abstractions;
using StockYouNeed.Application.Options;
using StockYouNeed.Infrastructure.Angel;
using StockYouNeed.Infrastructure.Persistence;

namespace StockYouNeed.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<DatabaseOptions>(configuration.GetSection(DatabaseOptions.SectionName));
        services.Configure<AngelOptions>(configuration.GetSection(AngelOptions.SectionName));
        services.Configure<WorkerScheduleOptions>(configuration.GetSection(WorkerScheduleOptions.SectionName));
        services.Configure<DevAuthOptions>(configuration.GetSection(DevAuthOptions.SectionName));

        services.AddSingleton<IDbConnectionFactory, NpgsqlConnectionFactory>();
        services.AddHttpClient<IAngelMarketDataClient, AngelMarketDataClient>();
        services.AddScoped<IInstrumentRepository, InstrumentRepository>();
        services.AddScoped<IMarketDataRepository, MarketDataRepository>();
        services.AddScoped<IPortfolioRepository, PortfolioRepository>();
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
