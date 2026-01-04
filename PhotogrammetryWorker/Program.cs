using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using PhotogrammetryWorker;
using Serilog;
using Serilog.Events;

// Configure Serilog
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
    .MinimumLevel.Override("System", LogEventLevel.Warning)
    .Enrich.FromLogContext()
    .WriteTo.Console(
        outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}")
    .WriteTo.File(
        path: "logs/worker-.log",
        rollingInterval: RollingInterval.Day,
        retainedFileCountLimit: 7,
        outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
    .CreateLogger();

try
{
    var builder = Host.CreateApplicationBuilder(args);

    // Use Serilog
    builder.Services.AddSerilog();

    // Load configuration
    builder.Configuration
        .SetBasePath(Directory.GetCurrentDirectory())
        .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
        .AddEnvironmentVariables();

    // Register services
    builder.Services.AddHostedService<ColmapWorkerService>();

    // Build and run
    var host = builder.Build();

    Log.Information("========================================");
    Log.Information("  Photogrammetry Worker Starting");
    Log.Information("========================================");
    Log.Information("RabbitMQ: {Host}:{Port}", 
        builder.Configuration["RabbitMQ:Host"], 
        builder.Configuration["RabbitMQ:Port"]);
    Log.Information("COLMAP: {Path}", builder.Configuration["Colmap:ExecutablePath"]);
    Log.Information("Projects Path: {Path}", builder.Configuration["Storage:ProjectsPath"]);
    Log.Information("Press Ctrl+C to stop");
    Log.Information("");

    await host.RunAsync();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Application terminated unexpectedly");
}
finally
{
    Log.CloseAndFlush();
}
