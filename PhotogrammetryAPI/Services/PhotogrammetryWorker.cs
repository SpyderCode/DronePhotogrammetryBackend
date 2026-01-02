using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using System.Text;
using System.Text.Json;
using PhotogrammetryAPI.Data;
using PhotogrammetryAPI.Models;
using Microsoft.EntityFrameworkCore;
using System.Diagnostics;

namespace PhotogrammetryAPI.Services;

public class PhotogrammetryWorker : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IConfiguration _configuration;
    private IConnection? _connection;
    private IModel? _channel;
    private readonly string _queueName = "photogrammetry-queue";
    
    public PhotogrammetryWorker(IServiceProvider serviceProvider, IConfiguration configuration)
    {
        _serviceProvider = serviceProvider;
        _configuration = configuration;
    }
    
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(5000, stoppingToken); // Wait for app to start
        
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
            
            // Set consumer timeout to 24 hours (in milliseconds)
            var args = new Dictionary<string, object>
            {
                { "x-consumer-timeout", 86400000 } // 24 hours
            };
            
            _channel.QueueDeclare(
                queue: _queueName,
                durable: true,
                exclusive: false,
                autoDelete: false,
                arguments: args
            );
            
            _channel.BasicQos(prefetchSize: 0, prefetchCount: 1, global: false);
            
            var consumer = new EventingBasicConsumer(_channel);
            consumer.Received += async (model, ea) =>
            {
                var deliveryTag = ea.DeliveryTag;
                try
                {
                    var body = ea.Body.ToArray();
                    var message = Encoding.UTF8.GetString(body);
                    var data = JsonSerializer.Deserialize<Dictionary<string, int>>(message);
                    
                    if (data != null && data.ContainsKey("ProjectId"))
                    {
                        await ProcessProjectAsync(data["ProjectId"]);
                    }
                    
                    // Acknowledge after successful processing
                    try
                    {
                        if (_channel != null && _channel.IsOpen)
                        {
                            _channel.BasicAck(deliveryTag: deliveryTag, multiple: false);
                        }
                        else
                        {
                            Console.WriteLine("Channel closed, cannot acknowledge message. Project completed successfully anyway.");
                        }
                    }
                    catch (Exception ackEx)
                    {
                        Console.WriteLine($"Failed to acknowledge message (but processing succeeded): {ackEx.Message}");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error processing message: {ex.Message}");
                    try
                    {
                        if (_channel != null && _channel.IsOpen)
                        {
                            _channel.BasicNack(deliveryTag: deliveryTag, multiple: false, requeue: true);
                        }
                    }
                    catch (Exception nackEx)
                    {
                        Console.WriteLine($"Failed to nack message: {nackEx.Message}");
                    }
                }
            };
            
            _channel.BasicConsume(queue: _queueName, autoAck: false, consumer: consumer);
            
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Worker error: {ex.Message}");
        }
    }
    
    private async Task ProcessProjectAsync(int projectId)
    {
        using var scope = _serviceProvider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        
        var project = await context.Projects.FindAsync(projectId);
        if (project == null) return;
        
        try
        {
            project.Status = ProcessingStatus.Processing;
            project.ProcessingStartedAt = DateTime.UtcNow;
            await context.SaveChangesAsync();
            
            var outputPath = await RunPhotogrammetryAsync(project.ZipFilePath, projectId);
            
            project.OutputModelPath = outputPath;
            project.Status = ProcessingStatus.Finished;
            project.CompletedAt = DateTime.UtcNow;
        }
        catch (Exception ex)
        {
            project.Status = ProcessingStatus.Failed;
            project.ErrorMessage = ex.Message;
            project.CompletedAt = DateTime.UtcNow;
        }
        
        await context.SaveChangesAsync();
    }
    
    private async Task<string> RunPhotogrammetryAsync(string zipFilePath, int projectId)
    {
        var colmapPath = _configuration["Colmap:ExecutablePath"] ?? "colmap";
        var modelsPath = _configuration["Storage:ModelsPath"] ?? "models";
        var workingPath = Path.Combine(modelsPath, $"project_{projectId}");
        var imagesPath = Path.Combine(workingPath, "images");
        var outputPath = Path.Combine(workingPath, "output");
        var databasePath = Path.Combine(workingPath, "database.db");
        
        Directory.CreateDirectory(workingPath);
        Directory.CreateDirectory(imagesPath);
        Directory.CreateDirectory(outputPath);
        
        Console.WriteLine($"Extracting images from {zipFilePath} to {imagesPath}");
        System.IO.Compression.ZipFile.ExtractToDirectory(zipFilePath, imagesPath, true);
        
        var imageFiles = Directory.GetFiles(imagesPath, "*.*", SearchOption.AllDirectories)
            .Where(f => f.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) ||
                       f.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase) ||
                       f.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
            .ToList();
        
        Console.WriteLine($"Found {imageFiles.Count} images in nested structure");
        
        if (imageFiles.Count < 3)
        {
            throw new Exception("At least 3 images are required for photogrammetry processing");
        }
        
        // Flatten images to root of imagesPath for COLMAP
        var flatImagesPath = Path.Combine(workingPath, "flat_images");
        Directory.CreateDirectory(flatImagesPath);
        
        foreach (var imageFile in imageFiles)
        {
            var fileName = Path.GetFileName(imageFile);
            var destPath = Path.Combine(flatImagesPath, fileName);
            File.Copy(imageFile, destPath, true);
        }
        
        Console.WriteLine($"Flattened {imageFiles.Count} images to {flatImagesPath}");
        
        // Check if colmap is available
        if (File.Exists(colmapPath) || await IsCommandAvailable("colmap"))
        {
            Console.WriteLine("Running COLMAP photogrammetry processing...");
            
            // COLMAP automatic reconstruction pipeline
            // 1. Feature extraction
            await RunColmapCommand(colmapPath, $"feature_extractor --database_path \"{databasePath}\" --image_path \"{flatImagesPath}\"");
            
            // 2. Feature matching
            await RunColmapCommand(colmapPath, $"exhaustive_matcher --database_path \"{databasePath}\"");
            
            // 3. Mapper (SfM)
            var sparseDir = Path.Combine(outputPath, "sparse");
            Directory.CreateDirectory(sparseDir);
            await RunColmapCommand(colmapPath, $"mapper --database_path \"{databasePath}\" --image_path \"{flatImagesPath}\" --output_path \"{sparseDir}\"");
            
            // 4. Image undistortion
            var denseDir = Path.Combine(outputPath, "dense");
            Directory.CreateDirectory(denseDir);
            await RunColmapCommand(colmapPath, $"image_undistorter --image_path \"{flatImagesPath}\" --input_path \"{sparseDir}/0\" --output_path \"{denseDir}\"");
            
            // 5. Stereo matching
            await RunColmapCommand(colmapPath, $"patch_match_stereo --workspace_path \"{denseDir}\"");
            
            // 6. Fusion
            await RunColmapCommand(colmapPath, $"stereo_fusion --workspace_path \"{denseDir}\" --output_path \"{denseDir}/fused.ply\"");
            
            // 7. Poisson meshing
            var meshPath = Path.Combine(denseDir, "meshed-poisson.ply");
            await RunColmapCommand(colmapPath, $"poisson_mesher --input_path \"{denseDir}/fused.ply\" --output_path \"{meshPath}\"");
            
            // Find the generated mesh file
            var meshFile = FindMeshFile(outputPath);
            if (meshFile != null)
            {
                Console.WriteLine($"Mesh generated successfully: {meshFile}");
                return meshFile;
            }
        }
        else
        {
            Console.WriteLine("COLMAP not installed - creating dummy model for testing");
            await Task.Delay(3000);
            
            var dummyModelPath = Path.Combine(outputPath, "model.obj");
            await File.WriteAllTextAsync(dummyModelPath, 
                $"# Dummy 3D Model for Project {projectId}\n" +
                $"# Processed {imageFiles.Count} images\n" +
                $"# COLMAP not installed - install COLMAP for actual processing\n" +
                "v 0 0 0\nv 1 0 0\nv 0 1 0\nf 1 2 3\n");
            
            return dummyModelPath;
        }
        
        throw new Exception("No mesh file was generated");
    }
    
    private async Task RunColmapCommand(string colmapPath, string arguments)
    {
        Console.WriteLine($"COLMAP: {arguments}");
        
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
                Console.WriteLine($"COLMAP: {args.Data}");
        };
        
        process.ErrorDataReceived += (sender, args) => 
        {
            if (!string.IsNullOrEmpty(args.Data))
            {
                // COLMAP writes info logs to stderr, only show warnings/errors or log level E/W/F
                if (args.Data.Contains(" E") || args.Data.Contains(" W") || args.Data.Contains(" F") || 
                    args.Data.Contains("ERROR") || args.Data.Contains("WARNING") || args.Data.Contains("error:") ||
                    args.Data.Contains("Error:"))
                {
                    Console.WriteLine($"COLMAP Warning/Error: {args.Data}");
                }
                else if (args.Data.Contains("Elapsed time:") || args.Data.Contains("Writing output:") || 
                         args.Data.Contains("Number of") || args.Data.Contains("Processing"))
                {
                    // Log important progress messages
                    Console.WriteLine($"COLMAP: {args.Data.Substring(args.Data.IndexOf(']') + 1).Trim()}");
                }
                // Suppress verbose info logs (lines starting with I followed by timestamp)
            }
        };
        
        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        
        await process.WaitForExitAsync();
        
        if (process.ExitCode != 0)
        {
            throw new Exception($"COLMAP command failed with exit code {process.ExitCode}: {arguments}");
        } else{
            Console.WriteLine("COLMAP command completed successfully");
        }
    }
    
    private async Task<bool> IsCommandAvailable(string command)
    {
        try
        {
            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "which",
                    Arguments = command,
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };
            
            process.Start();
            await process.WaitForExitAsync();
            return process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }
    
    private string? FindMeshFile(string outputPath)
    {
        var extensions = new[] { ".obj", ".ply", ".fbx", ".gltf", ".glb" };
        
        foreach (var ext in extensions)
        {
            var files = Directory.GetFiles(outputPath, $"*{ext}", SearchOption.AllDirectories);
            if (files.Length > 0)
            {
                return files.OrderByDescending(f => new FileInfo(f).Length).First();
            }
        }
        
        return null;
    }
    
    public override void Dispose()
    {
        _channel?.Close();
        _connection?.Close();
        base.Dispose();
    }
}
