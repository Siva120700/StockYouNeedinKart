using StockYouNeed.Api.Auth;
using StockYouNeed.Api.GraphQL;
using StockYouNeed.Application;
using StockYouNeed.Application.Abstractions;
using StockYouNeed.Application.Options;
using StockYouNeed.Application.Services;
using StockYouNeed.Infrastructure;
using StockYouNeed.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);

builder.Configuration.AddJsonFile(
    $"appsettings.{builder.Environment.EnvironmentName}.local.json",
    optional: true,
    reloadOnChange: true);

builder.Services.AddHttpContextAccessor();
builder.Services.AddCors(o => o.AddDefaultPolicy(p =>
    p.AllowAnyHeader().AllowAnyMethod().AllowAnyOrigin()));

builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddScoped<ICurrentUserAccessor, HttpCurrentUserAccessor>();

builder.Services
    .AddGraphQLServer()
    .AddQueryType<Query>()
    .AddMutationType<Mutation>()
    .ModifyRequestOptions(o =>
    {
        o.IncludeExceptionDetails = builder.Environment.IsDevelopment();
        // Liquidity run syncs 1H bars for the full universe (Angel-paced) — far beyond 30s default.
        o.ExecutionTimeout = TimeSpan.FromMinutes(15);
    });

var app = builder.Build();

app.UseCors();
app.MapGet("/health", () => Results.Ok(new { status = "ok" }));
app.MapGraphQL();

// Ensure demo user exists for thin frontend without auth yet
using (var scope = app.Services.CreateScope())
{
    var dbRoot = FindDatabaseRoot();
    var migrator = scope.ServiceProvider.GetRequiredService<DatabaseMigrator>();
    try
    {
        await migrator.MigrateAsync(dbRoot);
    }
    catch (Exception ex)
    {
        app.Logger.LogWarning(ex, "DB migrate skipped/failed — ensure Postgres is running and connection string is set.");
    }

    var auth = scope.ServiceProvider.GetRequiredService<Microsoft.Extensions.Options.IOptions<DevAuthOptions>>().Value;
    var instruments = scope.ServiceProvider.GetRequiredService<IInstrumentRepository>();
    try
    {
        await instruments.EnsureDemoUserAsync(auth.DemoUserId, auth.DemoEmail, auth.DemoDisplayName);
        var seeder = scope.ServiceProvider.GetRequiredService<UniverseSeedService>();
        await seeder.SeedAsync();
        // Map new Nifty 100 symbols to Angel tokens (Worker also does this daily).
        var tokenSync = scope.ServiceProvider.GetRequiredService<TokenSyncService>();
        await tokenSync.SyncUniverseTokensAsync();
    }
    catch (Exception ex)
    {
        app.Logger.LogWarning(ex, "Seed skipped — database may be offline.");
    }
}

app.Run();

static string FindDatabaseRoot()
{
    var dir = new DirectoryInfo(AppContext.BaseDirectory);
    while (dir is not null)
    {
        var candidate = System.IO.Path.Combine(dir.FullName, "database");
        if (Directory.Exists(candidate) && File.Exists(System.IO.Path.Combine(candidate, "001_init.sql")))
            return candidate;
        dir = dir.Parent;
    }

    throw new DirectoryNotFoundException("Could not locate database/ folder with 001_init.sql");
}
