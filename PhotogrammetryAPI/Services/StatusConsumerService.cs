using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PhotogrammetryAPI.Data;
using PhotogrammetryAPI.Models;

namespace PhotogrammetryAPI.Services;

public class StatusConsumerService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<StatusConsumerService> _logger;
    private readonly IConfiguration _configuration;
    private IConnection? _connection;
    private IModel? _channel;
    private readonly string _statusQueueName = "photogrammetry-status";
    
    public StatusConsumerService(IServiceProvider serviceProvider, IConfiguration configuration, ILogger<StatusConsumerService> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
        _configuration = configuration;
    }
    
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            var factory = new ConnectionFactory
            {
                HostName = _configuration["RabbitMQ:Host"] ?? "localhost",
                Port = int.Parse(_configuration["RabbitMQ:Port"] ?? "5672"),
                UserName = _configuration["RabbitMQ:Username"] ?? "guest",
                Password = _configuration["RabbitMQ:Password"] ?? "guest",
                DispatchConsumersAsync = true
            };
            
            _connection = factory.CreateConnection();
            _channel = _connection.CreateModel();
            
            // Declare status queue
            _channel.QueueDeclare(
                queue: _statusQueueName,
                durable: true,
                exclusive: false,
                autoDelete: false,
                arguments: null
            );
            
            _channel.BasicQos(prefetchSize: 0, prefetchCount: 1, global: false);
            
            _logger.LogInformation("Status Consumer connected to RabbitMQ");
            _logger.LogInformation($"Listening on: {_statusQueueName}");
            _logger.LogInformation("");
            
            var consumer = new AsyncEventingBasicConsumer(_channel);
            consumer.Received += async (model, ea) =>
            {
                var deliveryTag = ea.DeliveryTag;
                try
                {
                    var body = ea.Body.ToArray();
                    var message = Encoding.UTF8.GetString(body);
                    var statusUpdate = JsonSerializer.Deserialize<StatusUpdateMessage>(message);
                    
                    if (statusUpdate != null)
                    {
                        await UpdateProjectStatusAsync(statusUpdate);
                        _channel.BasicAck(deliveryTag: deliveryTag, multiple: false);
                    }
                    else
                    {
                        _logger.LogWarning("Invalid status update message");
                        _channel.BasicReject(deliveryTag: deliveryTag, requeue: false);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError($"Error processing status update: {ex.Message}");
                    _channel.BasicNack(deliveryTag: deliveryTag, multiple: false, requeue: true);
                }
            };
            
            _channel.BasicConsume(queue: _statusQueueName, autoAck: false, consumer: consumer);
            
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (Exception ex)
        {
            _logger.LogError($"Status consumer error: {ex.Message}");
            throw;
        }
    }
    
    private async Task UpdateProjectStatusAsync(StatusUpdateMessage statusUpdate)
    {
        using var scope = _serviceProvider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        
        var project = await dbContext.Projects.FindAsync(statusUpdate.ProjectId);
        
        if (project == null)
        {
            _logger.LogWarning($"Project {statusUpdate.ProjectId} not found in database");
            return;
        }
        
        var oldStatus = project.Status;
        
        switch (statusUpdate.Status)
        {
            case "Processing":
                project.Status = ProcessingStatus.Processing;
                project.ProcessingStartedAt = DateTime.UtcNow;
                _logger.LogInformation($"Project {project.Id}: {oldStatus} → Processing");
                break;
                
            case "Completed":
                project.Status = ProcessingStatus.Finished;
                project.CompletedAt = DateTime.UtcNow;
                if (!string.IsNullOrEmpty(statusUpdate.OutputModelPath))
                {
                    project.OutputModelPath = statusUpdate.OutputModelPath;
                }
                _logger.LogInformation($"Project {project.Id}: {oldStatus} → Finished");
                _logger.LogInformation($"Output: {project.OutputModelPath}");
                break;
                
            case "Failed":
                project.Status = ProcessingStatus.Failed;
                project.ErrorMessage = statusUpdate.ErrorMessage;
                _logger.LogInformation($"Project {project.Id}: {oldStatus} → Failed");
                _logger.LogInformation($"Error: {statusUpdate.ErrorMessage}");
                break;
                
            default:
                _logger.LogWarning($"Unknown status: {statusUpdate.Status}");
                return;
        }
        
        await dbContext.SaveChangesAsync();
    }
    
    public override void Dispose()
    {
        _channel?.Close();
        _connection?.Close();
        base.Dispose();
    }
}

public class StatusUpdateMessage
{
    public int ProjectId { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? ErrorMessage { get; set; }
    public string? OutputModelPath { get; set; }
    public DateTime Timestamp { get; set; }
}
