using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using System.Text;
using System.Text.Json;
using System.Diagnostics;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace PhotogrammetryWorker;

public class ColmapWorkerService : BackgroundService
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<ColmapWorkerService> _logger;
    private IConnection? _connection;
    private IModel? _channel;
    private readonly string _queueName = "photogrammetry-queue";
    private readonly string _statusQueueName = "photogrammetry-status";
    private readonly string _verboseStatusQueueName = "photogrammetry-status-verbose";
    private readonly string _workerId;
    
    public ColmapWorkerService(IConfiguration configuration, ILogger<ColmapWorkerService> logger)
    {
        _configuration = configuration;
        _logger = logger;
        _workerId = $"worker-{Environment.MachineName}-{Guid.NewGuid().ToString()[..8]}";
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
            
            _logger.LogInformation("Worker connected to RabbitMQ");
            _logger.LogInformation("Queue: {QueueName}", _queueName);
            _logger.LogInformation("Status Queue: {StatusQueueName}", _statusQueueName);
            _logger.LogInformation("Dead Letter Queue: {DeadLetterQueue}", deadLetterQueue);
            _logger.LogInformation("Waiting for projects...");

            
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
                            _logger.LogInformation("Project {ProjectId} acknowledged", projectId);
                        }
                    }
                    else
                    {
                        _logger.LogWarning("Invalid message format - rejecting");
                        if (_channel != null && _channel.IsOpen)
                        {
                            _channel.BasicReject(deliveryTag: deliveryTag, requeue: false);
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error processing project {ProjectId}", projectId);
                    
                    try
                    {
                        if (_channel != null && _channel.IsOpen)
                        {
                            // Check if this is a retriable error or fatal error
                            var shouldRequeue = ShouldRequeueMessage(ex, projectId);
                            
                            if (shouldRequeue)
                            {
                                _logger.LogInformation("Requeuing project {ProjectId} for retry", projectId);
                                _channel.BasicNack(deliveryTag: deliveryTag, multiple: false, requeue: true);
                            }
                            else
                            {
                                _logger.LogError("Rejecting project {ProjectId} (fatal error)", projectId);
                                _channel.BasicReject(deliveryTag: deliveryTag, requeue: false);
                            }
                        }
                    }
                    catch (Exception nackEx)
                    {
                        _logger.LogError(nackEx, "Failed to nack/reject message");
                    }
                }
            };
            
            _channel.BasicConsume(queue: _queueName, autoAck: false, consumer: consumer);
            
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Worker error");
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
                _logger.LogWarning("Cannot publish status update - channel closed");
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
            
            _logger.LogInformation("Status update published: Project {ProjectId} -> {Status}", projectId, status);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to publish status update");
        }
    }
    
    private void PublishVerboseStatus(int projectId, string status, string? currentStep = null, string? message = null, int? imageCount = null)
    {
        try
        {
            if (_channel == null || !_channel.IsOpen) return;
            
            var verboseUpdate = new
            {
                ProjectId = projectId,
                Status = status,
                WorkerId = _workerId,
                CurrentStep = currentStep,
                Message = message,
                ImageCount = imageCount,
                Timestamp = DateTime.UtcNow
            };
            
            var json = JsonSerializer.Serialize(verboseUpdate);
            var body = Encoding.UTF8.GetBytes(json);
            
            var properties = _channel.CreateBasicProperties();
            properties.Persistent = false;
            properties.ContentType = "application/json";
            
            _channel.BasicPublish(
                exchange: "",
                routingKey: _verboseStatusQueueName,
                basicProperties: properties,
                body: body
            );
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to publish verbose status (non-critical)");
        }
    }
    
    private async Task ProcessProjectAsync(int projectId)
    {
        _logger.LogInformation("Processing Project {ProjectId} started at {StartTime}", projectId, DateTime.Now);
        
        // Send "Processing" status update
        PublishStatusUpdate(projectId, "Processing");
        PublishVerboseStatus(projectId, "Processing", "Starting", $"Worker {_workerId} started processing");
        
        var projectsPath = _configuration["Storage:ProjectsPath"] ?? "Projects";
        var projectPath = Path.Combine(projectsPath, $"project_{projectId}");
        
        if (!Directory.Exists(projectPath))
        {
            _logger.LogError("Project directory not found: {ProjectPath}", projectPath);
            PublishStatusUpdate(projectId, "Failed", $"Project directory not found: {projectPath}");
            PublishVerboseStatus(projectId, "Failed", message: $"Project directory not found: {projectPath}");
            return;
        }
        
        try
        {
            var imagesDir = Path.Combine(projectPath, "images");
            var outputDir = Path.Combine(projectPath, "output");
            
            // Use local cache for database to avoid network share locking issues
            var localCachePath = _configuration["Storage:LocalCachePath"] ?? "/tmp/colmap_cache";
            var localProjectCache = Path.Combine(localCachePath, $"project_{projectId}");
            Directory.CreateDirectory(localProjectCache);
            var databasePath = Path.Combine(localProjectCache, "database.db");
            
            var flatImagesDir = Path.Combine(projectPath, "flat_images");
            
            if (Directory.Exists(flatImagesDir))
                Directory.Delete(flatImagesDir, true);
            Directory.CreateDirectory(flatImagesDir);
            
            PublishVerboseStatus(projectId, "Processing", "Preparing images", "Flattening image directory structure");
            FlattenImageDirectory(imagesDir, flatImagesDir);
            
            // Count images
            var imageFiles = Directory.GetFiles(flatImagesDir, "*.*", SearchOption.AllDirectories)
                .Where(f => f.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) ||
                           f.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase) ||
                           f.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
                .ToArray();
            _logger.LogInformation("Project {ProjectId}: Total images = {ImageCount}", projectId, imageFiles.Length);
            PublishVerboseStatus(projectId, "Processing", "Images counted", $"Found {imageFiles.Length} images", imageFiles.Length);
            
            var sparseDir = Path.Combine(outputDir, "sparse");
            var denseDir = Path.Combine(outputDir, "dense");
            Directory.CreateDirectory(sparseDir);
            Directory.CreateDirectory(denseDir);
            
            var colmapPath = _configuration["Colmap:ExecutablePath"] ?? "colmap";
            
            // Get quality settings from configuration
            var maxFeatures = _configuration["Colmap:FeatureExtraction:SiftMaxNumFeatures"] ?? "16384";
            var firstOctave = _configuration["Colmap:FeatureExtraction:SiftFirstOctave"] ?? "-1";
            var matchDistance = _configuration["Colmap:FeatureMatching:SiftMatchingMaxDistance"] ?? "0.7";
            var matchRatio = _configuration["Colmap:FeatureMatching:SiftMatchingMaxRatio"] ?? "0.8";
            var maxImageSize = _configuration["Colmap:DenseReconstruction:StereoMaxImageSize"] ?? "3200";
            var windowRadius = _configuration["Colmap:DenseReconstruction:StereoWindowRadius"] ?? "5";
            var windowStep = _configuration["Colmap:DenseReconstruction:StereoWindowStep"] ?? "1";
            var minNumPixels = _configuration["Colmap:DenseReconstruction:FusionMinNumPixels"] ?? "3";
            var maxReprojError = _configuration["Colmap:DenseReconstruction:FusionMaxReprojError"] ?? "2.0";
            var maxDepthError = _configuration["Colmap:DenseReconstruction:FusionMaxDepthError"] ?? "0.005";
            
            _logger.LogInformation("Step 1/7: Feature extraction (high quality)...");
            PublishVerboseStatus(projectId, "Processing", "Step 1/7: Feature extraction", "Extracting SIFT features from images", imageFiles.Length);
            await RunColmapCommand(colmapPath,
                $"feature_extractor --database_path \"{databasePath}\" --image_path \"{flatImagesDir}\" " +
                $"--ImageReader.single_camera 0 --SiftExtraction.max_num_features {maxFeatures} " +
                $"--SiftExtraction.first_octave {firstOctave}");
            
            _logger.LogInformation("Step 2/7: Feature matching (high quality)...");
            PublishVerboseStatus(projectId, "Processing", "Step 2/7: Feature matching", "Matching features between images", imageFiles.Length);
            await RunColmapCommand(colmapPath,
                $"exhaustive_matcher --database_path \"{databasePath}\" " +
                $"--SiftMatching.max_distance {matchDistance} --SiftMatching.max_ratio {matchRatio}");
            
            _logger.LogInformation("Step 3/7: Sparse reconstruction...");
            PublishVerboseStatus(projectId, "Processing", "Step 3/7: Sparse reconstruction", "Building sparse 3D point cloud", imageFiles.Length);
            await RunColmapCommand(colmapPath,
                $"mapper --database_path \"{databasePath}\" --image_path \"{flatImagesDir}\" --output_path \"{sparseDir}\"");
            
            _logger.LogInformation("Step 4/7: Image undistortion...");
            PublishVerboseStatus(projectId, "Processing", "Step 4/7: Image undistortion", "Undistorting images for dense reconstruction", imageFiles.Length);
            await RunColmapCommand(colmapPath,
                $"image_undistorter --image_path \"{flatImagesDir}\" --input_path \"{Path.Combine(sparseDir, "0")}\" " +
                $"--output_path \"{denseDir}\" --max_image_size {maxImageSize}");
            
            _logger.LogInformation("Step 5/7: Dense stereo matching (GPU - high quality)...");
            PublishVerboseStatus(projectId, "Processing", "Step 5/7: Dense stereo matching", "Computing depth maps (GPU accelerated)", imageFiles.Length);
            await RunColmapCommand(colmapPath,
                $"patch_match_stereo --workspace_path \"{denseDir}\" " +
                $"--PatchMatchStereo.max_image_size {maxImageSize} " +
                $"--PatchMatchStereo.window_radius {windowRadius} " +
                $"--PatchMatchStereo.window_step {windowStep} " +
                $"--PatchMatchStereo.geom_consistency true");
            
            _logger.LogInformation("Step 6/7: Stereo fusion (high quality)...");
            PublishVerboseStatus(projectId, "Processing", "Step 6/7: Stereo fusion", "Fusing depth maps into dense point cloud", imageFiles.Length);
            await RunColmapCommand(colmapPath,
                $"stereo_fusion --workspace_path \"{denseDir}\" --output_path \"{Path.Combine(denseDir, "fused.ply")}\" " +
                $"--StereoFusion.min_num_pixels {minNumPixels} " +
                $"--StereoFusion.max_reproj_error {maxReprojError} " +
                $"--StereoFusion.max_depth_error {maxDepthError}");
            
            _logger.LogInformation("Step 7/7: Delaunay meshing...");
            PublishVerboseStatus(projectId, "Processing", "Step 7/7: Delaunay meshing", "Creating triangulated mesh from point cloud", imageFiles.Length);
            var meshPath = Path.Combine(denseDir, "meshed-delaunay.ply");
            await RunColmapCommand(colmapPath,
                $"delaunay_mesher --input_path \"{denseDir}\" --input_type dense --output_path \"{meshPath}\"");
            
            // Send "Completed" status update with output path
            var relativeMeshPath = Path.GetRelativePath(projectsPath, meshPath);
            PublishStatusUpdate(projectId, "Completed", null, relativeMeshPath);
            PublishVerboseStatus(projectId, "Completed", "Finished", $"Mesh generated successfully: {meshPath}", imageFiles.Length);
            
            _logger.LogInformation("Project {ProjectId} completed successfully. Output: {MeshPath}. Finished at: {FinishedTime}", 
                projectId, meshPath, DateTime.Now);
            
            // Cleanup local cache
            if (Directory.Exists(localProjectCache))
            {
                Directory.Delete(localProjectCache, true);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Project {ProjectId} failed", projectId);
            PublishVerboseStatus(projectId, "Failed", "Error", ex.Message);
            
            // Cleanup local cache on failure too
            var localCachePath = _configuration["Storage:LocalCachePath"] ?? "/tmp/colmap_cache";
            var localProjectCache = Path.Combine(localCachePath, $"project_{projectId}");
            if (Directory.Exists(localProjectCache))
            {
                Directory.Delete(localProjectCache, true);
            }
            
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
                _logger.LogDebug("COLMAP: {Output}", args.Data);
        };
        
        process.ErrorDataReceived += (sender, args) => 
        {
            if (!string.IsNullOrEmpty(args.Data))
            {
                if (args.Data.Contains(" E") || args.Data.Contains(" W") || args.Data.Contains(" F") || 
                    args.Data.Contains("ERROR") || args.Data.Contains("WARNING") || args.Data.Contains("error:") ||
                    args.Data.Contains("Error:"))
                {
                    _logger.LogWarning("COLMAP: {Output}", args.Data);
                }
                else if (args.Data.Contains("Elapsed time:") || args.Data.Contains("Writing output:") || 
                         args.Data.Contains("Number of") || args.Data.Contains("Processing"))
                {
                    var startIndex = args.Data.IndexOf(']');
                    if (startIndex >= 0 && startIndex < args.Data.Length - 1)
                        _logger.LogInformation("COLMAP: {Output}", args.Data.Substring(startIndex + 1).Trim());
                }
                else
                {
                    _logger.LogDebug("COLMAP: {Output}", args.Data);
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
