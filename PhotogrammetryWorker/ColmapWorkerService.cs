using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using System.Text;
using System.Text.Json;
using System.Diagnostics;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Configuration;

namespace PhotogrammetryWorker;

public class ColmapWorkerService : BackgroundService
{
    private readonly IConfiguration _configuration;
    private IConnection? _connection;
    private IModel? _channel;
    private readonly string _queueName = "photogrammetry-queue";
    private readonly string _statusQueueName = "photogrammetry-status";
    
    public ColmapWorkerService(IConfiguration configuration)
    {
        _configuration = configuration;
    }
    
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(2000, stoppingToken);
        
        try
        {
            var factory = new ConnectionFactory
            {
                HostName = _configuration["RabbitMQ:Host"] ?? "localhost",
                Port = int.Parse(_configuration["RabbitMQ:Port"] ?? "5672"),
                UserName = _configuration["RabbitMQ:Username"] ?? "guest",
                Password = _configuration["RabbitMQ:Password"] ?? "guest",
                RequestedHeartbeat = TimeSpan.FromSeconds(60),
                NetworkRecoveryInterval = TimeSpan.FromSeconds(10),
                AutomaticRecoveryEnabled = true
            };
            
            _connection = factory.CreateConnection();
            _channel = _connection.CreateModel();
            
            // Setup Dead Letter Exchange for failed messages
            var deadLetterExchange = "photogrammetry-dlx";
            var deadLetterQueue = "photogrammetry-failed";
            
            _channel.ExchangeDeclare(deadLetterExchange, "direct", durable: true);
            _channel.QueueDeclare(
                queue: deadLetterQueue,
                durable: true,
                exclusive: false,
                autoDelete: false,
                arguments: null
            );
            _channel.QueueBind(deadLetterQueue, deadLetterExchange, "failed");
            
            // Main queue with consumer timeout and dead letter configuration
            var args = new Dictionary<string, object>
            {
                { "x-consumer-timeout", 86400000 }, // 24 hours
                { "x-dead-letter-exchange", deadLetterExchange },
                { "x-dead-letter-routing-key", "failed" }
            };
            
            _channel.QueueDeclare(
                queue: _queueName,
                durable: true,
                exclusive: false,
                autoDelete: false,
                arguments: args
            );
            
            // Prefetch 1 message at a time (fair dispatch)
            _channel.BasicQos(prefetchSize: 0, prefetchCount: 1, global: false);
            
            // Declare status update queue for API to consume
            _channel.QueueDeclare(
                queue: _statusQueueName,
                durable: true,
                exclusive: false,
                autoDelete: false,
                arguments: null
            );
            
            Console.WriteLine("[INFO] Worker connected to RabbitMQ");
            Console.WriteLine($"       Queue: {_queueName}");
            Console.WriteLine($"       Status Queue: {_statusQueueName}");
            Console.WriteLine($"       Dead Letter Queue: {deadLetterQueue}");
            Console.WriteLine("       Waiting for projects...");
            Console.WriteLine();
            
            var consumer = new EventingBasicConsumer(_channel);
            consumer.Received += async (model, ea) =>
            {
                var deliveryTag = ea.DeliveryTag;
                var projectId = -1;
                
                try
                {
                    var body = ea.Body.ToArray();
                    var message = Encoding.UTF8.GetString(body);
                    var data = JsonSerializer.Deserialize<Dictionary<string, int>>(message);
                    
                    if (data != null && data.ContainsKey("ProjectId"))
                    {
                        projectId = data["ProjectId"];
                        await ProcessProjectAsync(projectId);
                        
                        // Success - acknowledge message
                        if (_channel != null && _channel.IsOpen)
                        {
                            _channel.BasicAck(deliveryTag: deliveryTag, multiple: false);
                            Console.WriteLine($"[INFO] Project {projectId} acknowledged");
                        }
                    }
                    else
                    {
                        Console.WriteLine("[WARN] Invalid message format - rejecting");
                        if (_channel != null && _channel.IsOpen)
                        {
                            _channel.BasicReject(deliveryTag: deliveryTag, requeue: false);
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[ERROR] Error processing project {projectId}: {ex.Message}");
                    Console.WriteLine($"        Stack trace: {ex.StackTrace}");
                    
                    try
                    {
                        if (_channel != null && _channel.IsOpen)
                        {
                            // Check if this is a retriable error or fatal error
                            var shouldRequeue = ShouldRequeueMessage(ex, projectId);
                            
                            if (shouldRequeue)
                            {
                                Console.WriteLine($"        [INFO] Requeuing project {projectId} for retry");
                                _channel.BasicNack(deliveryTag: deliveryTag, multiple: false, requeue: true);
                            }
                            else
                            {
                                Console.WriteLine($"        [ERROR] Rejecting project {projectId} (fatal error)");
                                _channel.BasicReject(deliveryTag: deliveryTag, requeue: false);
                            }
                        }
                    }
                    catch (Exception nackEx)
                    {
                        Console.WriteLine($"[ERROR] Failed to nack/reject message: {nackEx.Message}");
                    }
                }
            };
            
            _channel.BasicConsume(queue: _queueName, autoAck: false, consumer: consumer);
            
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Worker error: {ex.Message}");
            throw;
        }
    }
    
    private bool ShouldRequeueMessage(Exception ex, int projectId)
    {
        // Retriable errors - requeue for another worker to try
        var retriableErrors = new[]
        {
            "Project directory not found",
            "Connection refused",
            "Network",
            "Timeout",
            "temporarily unavailable",
            "No space left on device"
        };
        
        foreach (var error in retriableErrors)
        {
            if (ex.Message.Contains(error, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        
        // Fatal errors - don't requeue (send to dead letter queue)
        var fatalErrors = new[]
        {
            "COLMAP command failed",
            "Invalid",
            "Corrupt",
            "Permission denied"
        };
        
        foreach (var error in fatalErrors)
        {
            if (ex.Message.Contains(error, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }
        
        // Default: requeue once (RabbitMQ will track delivery count)
        return true;
    }
    
    private void PublishStatusUpdate(int projectId, string status, string? errorMessage = null, string? outputPath = null)
    {
        try
        {
            if (_channel == null || !_channel.IsOpen)
            {
                Console.WriteLine("[WARN] Cannot publish status update - channel closed");
                return;
            }
            
            var statusUpdate = new
            {
                ProjectId = projectId,
                Status = status,
                ErrorMessage = errorMessage,
                OutputModelPath = outputPath,
                Timestamp = DateTime.UtcNow
            };
            
            var message = JsonSerializer.Serialize(statusUpdate);
            var body = Encoding.UTF8.GetBytes(message);
            
            var properties = _channel.CreateBasicProperties();
            properties.Persistent = true;
            properties.ContentType = "application/json";
            
            _channel.BasicPublish(
                exchange: "",
                routingKey: _statusQueueName,
                basicProperties: properties,
                body: body
            );
            
            Console.WriteLine($"[INFO] Status update published: Project {projectId} -> {status}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[WARN] Failed to publish status update: {ex.Message}");
        }
    }
    
    private async Task ProcessProjectAsync(int projectId)
    {
        Console.WriteLine($"[INFO] Processing Project {projectId}");
        Console.WriteLine($"       Started at: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        
        // Send "Processing" status update
        PublishStatusUpdate(projectId, "Processing");
        
        var projectsPath = _configuration["Storage:ProjectsPath"] ?? "Projects";
        var projectPath = Path.Combine(projectsPath, $"project_{projectId}");
        
        if (!Directory.Exists(projectPath))
        {
            Console.WriteLine($"[ERROR] Project directory not found: {projectPath}");
            PublishStatusUpdate(projectId, "Failed", $"Project directory not found: {projectPath}");
            return;
        }
        
        try
        {
            var imagesDir = Path.Combine(projectPath, "images");
            var outputDir = Path.Combine(projectPath, "output");
            var databasePath = Path.Combine(projectPath, "database.db");
            var flatImagesDir = Path.Combine(projectPath, "flat_images");
            
            if (Directory.Exists(flatImagesDir))
                Directory.Delete(flatImagesDir, true);
            Directory.CreateDirectory(flatImagesDir);
            
            FlattenImageDirectory(imagesDir, flatImagesDir);
            
            var sparseDir = Path.Combine(outputDir, "sparse");
            var denseDir = Path.Combine(outputDir, "dense");
            Directory.CreateDirectory(sparseDir);
            Directory.CreateDirectory(denseDir);
            
            var colmapPath = _configuration["Colmap:ExecutablePath"] ?? "colmap";
            
            Console.WriteLine("       Step 1/7: Feature extraction...");
            await RunColmapCommand(colmapPath,
                $"feature_extractor --database_path \"{databasePath}\" --image_path \"{flatImagesDir}\"");
            
            Console.WriteLine("       Step 2/7: Feature matching...");
            await RunColmapCommand(colmapPath,
                $"exhaustive_matcher --database_path \"{databasePath}\"");
            
            Console.WriteLine("       Step 3/7: Sparse reconstruction...");
            await RunColmapCommand(colmapPath,
                $"mapper --database_path \"{databasePath}\" --image_path \"{flatImagesDir}\" --output_path \"{sparseDir}\"");
            
            Console.WriteLine("       Step 4/7: Image undistortion...");
            await RunColmapCommand(colmapPath,
                $"image_undistorter --image_path \"{flatImagesDir}\" --input_path \"{Path.Combine(sparseDir, "0")}\" --output_path \"{denseDir}\"");
            
            Console.WriteLine("       Step 5/7: Dense stereo matching (GPU)...");
            await RunColmapCommand(colmapPath,
                $"patch_match_stereo --workspace_path \"{denseDir}\"");
            
            Console.WriteLine("       Step 6/7: Stereo fusion...");
            await RunColmapCommand(colmapPath,
                $"stereo_fusion --workspace_path \"{denseDir}\" --output_path \"{Path.Combine(denseDir, "fused.ply")}\"");
            
            Console.WriteLine("       Step 7/7: Poisson meshing...");
            var meshPath = Path.Combine(denseDir, "meshed-poisson.ply");
            await RunColmapCommand(colmapPath,
                $"poisson_mesher --input_path \"{Path.Combine(denseDir, "fused.ply")}\" --output_path \"{meshPath}\"");
            
            // Send "Completed" status update with output path
            var relativeMeshPath = Path.GetRelativePath(projectsPath, meshPath);
            PublishStatusUpdate(projectId, "Completed", null, relativeMeshPath);
            
            Console.WriteLine($"[INFO] Project {projectId} completed successfully");
            Console.WriteLine($"       Output: {meshPath}");
            Console.WriteLine($"       Finished at: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            Console.WriteLine();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ERROR] Project {projectId} failed: {ex.Message}");
            
            // Send "Failed" status update
            PublishStatusUpdate(projectId, "Failed", ex.Message);
            
            throw;
        }
    }
    
    private void FlattenImageDirectory(string sourceDir, string targetDir)
    {
        foreach (var file in Directory.GetFiles(sourceDir, "*.*", SearchOption.AllDirectories))
        {
            var extension = Path.GetExtension(file).ToLower();
            if (extension == ".jpg" || extension == ".jpeg" || extension == ".png")
            {
                var fileName = Path.GetFileName(file);
                var targetPath = Path.Combine(targetDir, fileName);
                File.Copy(file, targetPath, true);
            }
        }
    }
    
    private async Task RunColmapCommand(string colmapPath, string arguments)
    {
        Console.WriteLine($"      COLMAP: {arguments.Split(' ')[0]}");
        
        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = colmapPath,
                Arguments = arguments,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };
        
        process.OutputDataReceived += (sender, args) => 
        {
            if (!string.IsNullOrEmpty(args.Data))
                Console.WriteLine($"      {args.Data}");
        };
        
        process.ErrorDataReceived += (sender, args) => 
        {
            if (!string.IsNullOrEmpty(args.Data))
            {
                if (args.Data.Contains(" E") || args.Data.Contains(" W") || args.Data.Contains(" F") || 
                    args.Data.Contains("ERROR") || args.Data.Contains("WARNING") || args.Data.Contains("error:") ||
                    args.Data.Contains("Error:"))
                {
                    Console.WriteLine($"      [WARN] {args.Data}");
                }
                else if (args.Data.Contains("Elapsed time:") || args.Data.Contains("Writing output:") || 
                         args.Data.Contains("Number of") || args.Data.Contains("Processing"))
                {
                    var startIndex = args.Data.IndexOf(']');
                    if (startIndex >= 0 && startIndex < args.Data.Length - 1)
                        Console.WriteLine($"      {args.Data.Substring(startIndex + 1).Trim()}");
                }
            }
        };
        
        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        
        await process.WaitForExitAsync();
        
        if (process.ExitCode != 0)
        {
            throw new Exception($"COLMAP command failed with exit code {process.ExitCode}");
        }
    }
    
    public override void Dispose()
    {
        _channel?.Dispose();
        _connection?.Dispose();
        base.Dispose();
    }
}
