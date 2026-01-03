using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using System.Text;
using System.Text.Json;

namespace PhotogrammetryAPI.Services;

public interface IQueueService
{
    Task PublishProjectAsync(int projectId);
}

public class RabbitMQService : IQueueService, IDisposable
{
    private readonly IConnection _connection;
    private readonly IModel _channel;
    private readonly string _queueName = "photogrammetry-queue";
    
    public RabbitMQService(IConfiguration configuration)
    {
        var factory = new ConnectionFactory
        {
            HostName = configuration["RabbitMQ:Host"] ?? "localhost",
            Port = int.Parse(configuration["RabbitMQ:Port"] ?? "5672"),
            UserName = configuration["RabbitMQ:Username"] ?? "guest",
            Password = configuration["RabbitMQ:Password"] ?? "guest"
        };
        
        _connection = factory.CreateConnection();
        _channel = _connection.CreateModel();
        
        // Setup Dead Letter Exchange
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
        
        // Main queue with dead letter configuration
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
    }
    
    public Task PublishProjectAsync(int projectId)
    {
        var message = JsonSerializer.Serialize(new { ProjectId = projectId });
        var body = Encoding.UTF8.GetBytes(message);
        
        var properties = _channel.CreateBasicProperties();
        properties.Persistent = true;
        
        _channel.BasicPublish(
            exchange: "",
            routingKey: _queueName,
            basicProperties: properties,
            body: body
        );
        
        return Task.CompletedTask;
    }
    
    public void Dispose()
    {
        _channel?.Close();
        _connection?.Close();
    }
}
