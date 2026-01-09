using Spectre.Console;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using Newtonsoft.Json;
using System.Text;
using Microsoft.Extensions.Configuration;

namespace PhotogrammetryStatus;

class Program
{
    private static Dictionary<string, WorkerStatus> _workers = new();
    private static Dictionary<int, ProjectInfo> _projects = new();
    private static readonly object _lock = new object();
    private static bool _running = true;

    static async Task Main(string[] args)
    {
        var config = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: false)
            .Build();

        var rabbitHost = config["RabbitMQ:Host"] ?? "localhost";
        var rabbitPort = int.Parse(config["RabbitMQ:Port"] ?? "5672");
        var statusQueue = config["RabbitMQ:StatusQueue"] ?? "photogrammetry-status-verbose";

        Console.CancelKeyPress += (s, e) =>
        {
            e.Cancel = true;
            _running = false;
        };

        try
        {
            AnsiConsole.Clear();
            ShowHeader();

            var factory = new ConnectionFactory
            {
                HostName = rabbitHost,
                Port = rabbitPort,
                UserName = config["RabbitMQ:Username"] ?? "guest",
                Password = config["RabbitMQ:Password"] ?? "guest"
            };

            using var connection = await factory.CreateConnectionAsync();
            using var channel = await connection.CreateChannelAsync();

            await channel.QueueDeclareAsync(
                queue: statusQueue,
                durable: true,
                exclusive: false,
                autoDelete: false,
                arguments: null
            );

            var consumer = new AsyncEventingBasicConsumer(channel);
            consumer.ReceivedAsync += async (model, ea) =>
            {
                try
                {
                    var body = ea.Body.ToArray();
                    var message = Encoding.UTF8.GetString(body);
                    var statusMsg = JsonConvert.DeserializeObject<StatusMessage>(message);

                    if (statusMsg != null)
                    {
                        UpdateStatus(statusMsg);
                    }

                    await channel.BasicAckAsync(ea.DeliveryTag, false);
                }
                catch (Exception ex)
                {
                    AnsiConsole.MarkupLine($"[red]Error processing message: {ex.Message}[/]");
                }
            };

            await channel.BasicConsumeAsync(queue: statusQueue, autoAck: false, consumer: consumer);

            AnsiConsole.MarkupLine($"[green]Connected to RabbitMQ ({rabbitHost}:{rabbitPort})[/]");
            AnsiConsole.MarkupLine($"[green]Listening on queue: {statusQueue}[/]");
            AnsiConsole.MarkupLine("[dim]Press Ctrl+C to exit[/]\n");

            // Start dashboard update loop
            _ = Task.Run(() => UpdateDashboard());

            while (_running)
            {
                await Task.Delay(100);
            }

            AnsiConsole.MarkupLine("\n[yellow]Shutting down...[/]");
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]Fatal error: {ex.Message}[/]");
        }
    }

    static void ShowHeader()
    {
        var rule = new Rule("[bold blue]Photogrammetry Status Dashboard[/]")
        {
            Justification = Justify.Center
        };
        AnsiConsole.Write(rule);
        AnsiConsole.WriteLine();
    }

    static void UpdateStatus(StatusMessage msg)
    {
        lock (_lock)
        {
            // Update worker status
            if (!string.IsNullOrEmpty(msg.WorkerId))
            {
                if (!_workers.ContainsKey(msg.WorkerId))
                {
                    _workers[msg.WorkerId] = new WorkerStatus
                    {
                        WorkerId = msg.WorkerId
                    };
                }

                var worker = _workers[msg.WorkerId];
                worker.LastUpdate = msg.Timestamp;
                worker.Status = msg.Status;
                worker.CurrentStep = msg.CurrentStep ?? "";
                worker.DetailedMessage = msg.Message;

                if (msg.Status == "Processing")
                {
                    // Clear this project from any other workers that might be stuck on it
                    foreach (var otherWorker in _workers.Values.Where(w => w.WorkerId != msg.WorkerId && w.CurrentProjectId == msg.ProjectId))
                    {
                        otherWorker.CurrentProjectId = null;
                        otherWorker.ProcessingStarted = null;
                        otherWorker.Status = "Idle";
                        otherWorker.CurrentStep = "";
                        otherWorker.ImageCount = 0;
                    }
                    
                    worker.CurrentProjectId = msg.ProjectId;
                    worker.ProcessingStarted ??= msg.Timestamp;
                    worker.ImageCount = msg.ImageCount ?? 0;
                }
                else if (msg.Status == "Completed" || msg.Status == "Failed" || msg.Status == "Idle")
                {
                    worker.CurrentProjectId = null;
                    worker.ProcessingStarted = null;
                    worker.Status = "Idle";
                    worker.CurrentStep = "";
                    worker.ImageCount = 0;
                }
            }

            // Update project status (skip project ID 0 which is used for idle heartbeats)
            if (msg.ProjectId == 0)
            {
                return;
            }
            
            if (!_projects.ContainsKey(msg.ProjectId))
            {
                _projects[msg.ProjectId] = new ProjectInfo
                {
                    ProjectId = msg.ProjectId,
                    ProjectName = $"Project {msg.ProjectId}",
                    QueuedAt = msg.Timestamp
                };
            }

            var project = _projects[msg.ProjectId];
            var oldStatus = project.Status;
            project.Status = msg.Status;
            
            // When project changes worker, update the assignment
            if (!string.IsNullOrEmpty(msg.WorkerId))
            {
                project.WorkerId = msg.WorkerId;
            }
            
            project.CurrentStep = msg.CurrentStep;
            project.ImageCount = msg.ImageCount ?? project.ImageCount;

            if (msg.Status == "Processing")
            {
                // Reset processing start if this is a new attempt (e.g., after failure)
                if (oldStatus == "Failed" || oldStatus == "InQueue")
                {
                    project.ProcessingStarted = msg.Timestamp;
                }
                else if (!project.ProcessingStarted.HasValue)
                {
                    project.ProcessingStarted = msg.Timestamp;
                }
            }
            else if (msg.Status == "Completed" || msg.Status == "Failed")
            {
                project.CompletedAt = msg.Timestamp;
                if (msg.Status == "Failed")
                {
                    project.Error = msg.Message;
                }
            }
        }
    }

    static async Task UpdateDashboard()
    {
        while (_running)
        {
            try
            {
                lock (_lock)
                {
                    RenderDashboard();
                }
            }
            catch { }

            await Task.Delay(1000);
        }
    }

    static void RenderDashboard()
    {
        // Clean up stale workers (no update in 5 minutes)
        var staleWorkers = _workers.Where(w => (DateTime.UtcNow - w.Value.LastUpdate.ToUniversalTime()).TotalMinutes > 5).ToList();
        foreach (var staleWorker in staleWorkers)
        {
            _workers.Remove(staleWorker.Key);
        }

        //Clean up workers that are "Idle" if there are more projects than workers
        var idleWorkers = _workers.Where(w => w.Value.Status == "Idle").ToList();
        var activeProjectCount = _projects.Count(p => p.Value.Status == "InQueue");
        if (idleWorkers.Count > 0 && activeProjectCount < _workers.Count)
        {
            foreach (var idleWorker in idleWorkers)
            {
                _workers.Remove(idleWorker.Key);
            }
        }
        
        // Also clean up workers that are "Processing" but haven't updated in 2 minutes (likely crashed)
        var stuckWorkers = _workers.Where(w => 
            w.Value.Status == "Processing" && 
            (DateTime.UtcNow - w.Value.LastUpdate.ToUniversalTime()).TotalMinutes > 2).ToList();
        foreach (var stuckWorker in stuckWorkers)
        {
            _workers.Remove(stuckWorker.Key);
        }

        AnsiConsole.Clear();
        ShowHeader();

        // Workers Table
        var workersTable = new Table()
            .Border(TableBorder.Rounded)
            .BorderColor(Color.Blue)
            .AddColumn(new TableColumn("[bold]Worker ID[/]").Centered())
            .AddColumn(new TableColumn("[bold]Status[/]").Centered())
            .AddColumn(new TableColumn("[bold]Current Project[/]").Centered())
            .AddColumn(new TableColumn("[bold]Current Step[/]").LeftAligned())
            .AddColumn(new TableColumn("[bold]Elapsed Time[/]").Centered())
            .AddColumn(new TableColumn("[bold]Images[/]").Centered())
            .AddColumn(new TableColumn("[bold]Last Update[/]").Centered());

        if (_workers.Count == 0)
        {
            workersTable.AddRow("[dim]No workers connected[/]", "", "", "", "", "", "");
        }
        else
        {
            foreach (var worker in _workers.Values.OrderBy(w => w.WorkerId))
            {
                var statusColor = worker.Status switch
                {
                    "Processing" => "green",
                    "Idle" => "yellow",
                    _ => "gray"
                };

                var elapsed = worker.ProcessingStarted.HasValue
                    ? FormatElapsedTime(DateTime.UtcNow - worker.ProcessingStarted.Value.ToUniversalTime())
                    : "-";

                workersTable.AddRow(
                    $"[cyan]{worker.WorkerId}[/]",
                    $"[{statusColor}]{worker.Status}[/]",
                    worker.CurrentProjectId?.ToString() ?? "-",
                    TruncateStep(worker.CurrentStep),
                    elapsed,
                    worker.ImageCount > 0 ? worker.ImageCount.ToString() : "-",
                    worker.LastUpdate.ToString("HH:mm:ss")
                );
            }
        }

        AnsiConsole.Write(workersTable);
        AnsiConsole.WriteLine();

        // Projects Table (Recent)
        var projectsTable = new Table()
            .Border(TableBorder.Rounded)
            .BorderColor(Color.Green)
            .AddColumn(new TableColumn("[bold]Project ID[/]").Centered())
            .AddColumn(new TableColumn("[bold]Status[/]").Centered())
            .AddColumn(new TableColumn("[bold]Worker[/]").Centered())
            .AddColumn(new TableColumn("[bold]Current Step[/]").LeftAligned())
            .AddColumn(new TableColumn("[bold]Images[/]").Centered())
            .AddColumn(new TableColumn("[bold]Processing Time[/]").Centered())
            .AddColumn(new TableColumn("[bold]Queued At[/]").Centered());

        var recentProjects = _projects.Values
            .Where(p => p.ProjectId > 0) // Skip idle heartbeat entries
            .OrderByDescending(p => p.QueuedAt ?? DateTime.MinValue)
            .Take(15);

        if (!recentProjects.Any())
        {
            projectsTable.AddRow("[dim]No projects in queue[/]", "", "", "", "", "", "");
        }
        else
        {
            foreach (var project in recentProjects)
            {
                var statusColor = project.Status switch
                {
                    "InQueue" => "yellow",
                    "Processing" => "green",
                    "Completed" => "blue",
                    "Failed" => "red",
                    _ => "gray"
                };

                var processingTime = "-";
                if (project.ProcessingStarted.HasValue)
                {
                    var endTime = project.CompletedAt?.ToUniversalTime() ?? DateTime.UtcNow;
                    processingTime = FormatElapsedTime(endTime - project.ProcessingStarted.Value.ToUniversalTime());
                }

                projectsTable.AddRow(
                    $"[cyan]{project.ProjectId}[/]",
                    $"[{statusColor}]{project.Status}[/]",
                    project.WorkerId ?? "-",
                    TruncateStep(project.CurrentStep ?? ""),
                    project.ImageCount > 0 ? project.ImageCount.ToString() : "-",
                    processingTime,
                    project.QueuedAt?.ToString("HH:mm:ss") ?? "-"
                );
            }
        }

        AnsiConsole.Write(projectsTable);

        // Statistics
        var stats = new Table()
            .Border(TableBorder.None)
            .HideHeaders()
            .AddColumn("")
            .AddColumn("");

        var activeWorkers = _workers.Count(w => w.Value.Status == "Processing");
        var totalWorkers = _workers.Count;
        var queuedProjects = _projects.Count(p => p.Value.Status == "InQueue");
        var processingProjects = _projects.Count(p => p.Value.Status == "Processing");
        var completedProjects = _projects.Count(p => p.Value.Status == "Completed");
        var failedProjects = _projects.Count(p => p.Value.Status == "Failed");

        stats.AddRow("[bold]Active Workers:[/]", $"[green]{activeWorkers}/{totalWorkers}[/]");
        stats.AddRow("[bold]Queued Projects:[/]", $"[yellow]{queuedProjects}[/]");
        stats.AddRow("[bold]Processing:[/]", $"[green]{processingProjects}[/]");
        stats.AddRow("[bold]Completed:[/]", $"[blue]{completedProjects}[/]");
        stats.AddRow("[bold]Failed:[/]", $"[red]{failedProjects}[/]");

        AnsiConsole.WriteLine();
        AnsiConsole.Write(stats);
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine($"[dim]Last updated: {DateTime.Now:yyyy-MM-dd HH:mm:ss} | Press Ctrl+C to exit[/]");
    }

    static string FormatElapsedTime(TimeSpan elapsed)
    {
        if (elapsed.TotalHours >= 1)
            return $"{(int)elapsed.TotalHours}h {elapsed.Minutes}m";
        if (elapsed.TotalMinutes >= 1)
            return $"{(int)elapsed.TotalMinutes}m {elapsed.Seconds}s";
        return $"{(int)elapsed.TotalSeconds}s";
    }

    static string TruncateStep(string step)
    {
        if (string.IsNullOrEmpty(step)) return "-";
        return step.Length > 40 ? step.Substring(0, 37) + "..." : step;
    }
}
