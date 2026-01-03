using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using PhotogrammetryWorker;

var builder = Host.CreateApplicationBuilder(args);

// Load configuration
builder.Configuration
    .SetBasePath(Directory.GetCurrentDirectory())
    .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
    .AddEnvironmentVariables();

// Register services
builder.Services.AddHostedService<ColmapWorkerService>();

// Build and run
var host = builder.Build();

Console.WriteLine("========================================");
Console.WriteLine("  Photogrammetry Worker Starting");
Console.WriteLine("========================================");
Console.WriteLine($"RabbitMQ: {builder.Configuration["RabbitMQ:Host"]}:{builder.Configuration["RabbitMQ:Port"]}");
Console.WriteLine($"COLMAP: {builder.Configuration["Colmap:ExecutablePath"]}");
Console.WriteLine($"Projects Path: {builder.Configuration["Storage:ProjectsPath"]}");
Console.WriteLine("Press Ctrl+C to stop");
Console.WriteLine();

await host.RunAsync();
