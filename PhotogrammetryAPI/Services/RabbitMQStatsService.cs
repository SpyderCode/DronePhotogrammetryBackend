using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace PhotogrammetryAPI.Services;

public interface IRabbitMQStatsService
{
    Task<object> GetQueueStatsAsync();
}

public class RabbitMQStatsService : IRabbitMQStatsService
{
    private readonly IConfiguration _configuration;
    private readonly HttpClient _httpClient;
    private readonly string _managementUrl;
    private readonly string _queueName = "photogrammetry-queue";

    public RabbitMQStatsService(IConfiguration configuration, HttpClient httpClient)
    {
        _configuration = configuration;
        _httpClient = httpClient;
        
        var host = configuration["RabbitMQ:Host"] ?? "localhost";
        var managementPort = configuration["RabbitMQ:ManagementPort"] ?? "15672";
        var username = configuration["RabbitMQ:Username"] ?? "guest";
        var password = configuration["RabbitMQ:Password"] ?? "guest";
        
        _managementUrl = $"http://{host}:{managementPort}/api";
        
        var authString = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{username}:{password}"));
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", authString);
    }

    public async Task<object> GetQueueStatsAsync()
    {
        try
        {
            var queueUrl = $"{_managementUrl}/queues/%2F/{_queueName}";
            var response = await _httpClient.GetAsync(queueUrl);
            
            if (!response.IsSuccessStatusCode)
            {
                return new
                {
                    error = "Failed to fetch RabbitMQ stats",
                    statusCode = (int)response.StatusCode
                };
            }

            var content = await response.Content.ReadAsStringAsync();
            var queueData = JsonSerializer.Deserialize<JsonElement>(content);

            var consumers = queueData.GetProperty("consumers").GetInt32();
            var messages = queueData.GetProperty("messages").GetInt32();
            var messagesReady = queueData.GetProperty("messages_ready").GetInt32();
            var messagesUnacknowledged = queueData.GetProperty("messages_unacknowledged").GetInt32();

            return new
            {
                workers = consumers,
                queue = new
                {
                    name = _queueName,
                    totalMessages = messages,
                    messagesReady = messagesReady,
                    messagesProcessing = messagesUnacknowledged
                },
                status = consumers > 0 ? "active" : "no_workers"
            };
        }
        catch (Exception ex)
        {
            return new
            {
                error = $"Error fetching stats: {ex.Message}",
                workers = 0,
                queue = new
                {
                    name = _queueName,
                    totalMessages = 0,
                    messagesReady = 0,
                    messagesProcessing = 0
                },
                status = "error"
            };
        }
    }
}
