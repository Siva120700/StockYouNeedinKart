using StockYouNeed.Application;
using StockYouNeed.Application.Options;
using StockYouNeed.Infrastructure;
using StockYouNeed.Infrastructure.Persistence;

var builder = Host.CreateApplicationBuilder(args);

builder.Configuration.AddJsonFile(
    $"appsettings.{builder.Environment.EnvironmentName}.local.json",
    optional: true,
    reloadOnChange: true);

builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddHostedService<StockYouNeed.Worker.DailySyncHostedService>();
builder.Services.AddHostedService<StockYouNeed.Worker.LtpPollHostedService>();
builder.Services.AddHostedService<StockYouNeed.Worker.NiftyOrbPollHostedService>();

var host = builder.Build();

var dbRoot = FindDatabaseRoot();
var migrator = host.Services.GetRequiredService<DatabaseMigrator>();
await migrator.MigrateAsync(dbRoot);

await host.RunAsync();

static string FindDatabaseRoot()
{
    var dir = new DirectoryInfo(AppContext.BaseDirectory);
    while (dir is not null)
    {
        var candidate = Path.Combine(dir.FullName, "database");
        if (Directory.Exists(candidate) && File.Exists(Path.Combine(candidate, "001_init.sql")))
            return candidate;
        dir = dir.Parent;
    }

    throw new DirectoryNotFoundException("Could not locate database/ folder with 001_init.sql");
}
