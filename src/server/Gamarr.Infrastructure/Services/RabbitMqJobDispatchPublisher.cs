using System.Text;
using System.Text.Json;
using Gamarr.Application.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;

namespace Gamarr.Infrastructure.Services;

public sealed class RabbitMqJobDispatchPublisher : IJobDispatchPublisher, IDisposable
{
    private readonly ILogger<RabbitMqJobDispatchPublisher> _logger;
    private readonly IConnection? _connection;
    private readonly IModel? _channel;

    public RabbitMqJobDispatchPublisher(IConfiguration configuration, ILogger<RabbitMqJobDispatchPublisher> logger)
    {
        _logger = logger;
        try
        {
            var factory = new ConnectionFactory
            {
                HostName = configuration["RabbitMq:Host"] ?? configuration["GAMARR_RABBITMQ_HOST"] ?? "localhost",
                Port = int.TryParse(configuration["RabbitMq:Port"] ?? configuration["GAMARR_RABBITMQ_PORT"], out var port) ? port : 5672,
                UserName = configuration["RabbitMq:Username"] ?? configuration["GAMARR_RABBITMQ_USERNAME"] ?? "gamarr",
                Password = configuration["RabbitMq:Password"] ?? configuration["GAMARR_RABBITMQ_PASSWORD"] ?? "gamarr"
            };

            _connection = factory.CreateConnection();
            _channel = _connection.CreateModel();
            _channel.ExchangeDeclare("gamarr.jobs", ExchangeType.Topic, durable: true);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "RabbitMQ unavailable. Dispatch notifications will be skipped.");
        }
    }

    public Task PublishJobCreatedAsync(Guid jobId, Guid machineId, CancellationToken cancellationToken)
    {
        Publish("job.created", new { jobId, machineId, createdAtUtc = DateTimeOffset.UtcNow });
        return Task.CompletedTask;
    }

    public Task PublishJobUpdatedAsync(Guid jobId, CancellationToken cancellationToken)
    {
        Publish("job.updated", new { jobId, updatedAtUtc = DateTimeOffset.UtcNow });
        return Task.CompletedTask;
    }

    private void Publish(string routingKey, object payload)
    {
        if (_channel is null)
        {
            return;
        }

        var body = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(payload));
        var properties = _channel.CreateBasicProperties();
        properties.Persistent = true;
        _channel.BasicPublish("gamarr.jobs", routingKey, properties, body);
    }

    public void Dispose()
    {
        _channel?.Dispose();
        _connection?.Dispose();
    }
}
