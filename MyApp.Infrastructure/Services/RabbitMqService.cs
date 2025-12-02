// Required usings
using Microsoft.Extensions.Configuration;
using RabbitMQ.Client;
using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace MyApp.Infrastructure.Services
{
    public class RabbitMqService : IDisposable
    {
        private readonly IConnection _connection;
        private readonly ConnectionFactory _factory;
        private readonly string _exchange;
        private readonly string _queueName;

        public RabbitMqService(IConfiguration config)
        {
            _factory = new ConnectionFactory
            {
                HostName = config.GetValue<string>("RabbitMQ:Host") ?? "localhost",
                UserName = config.GetValue<string>("RabbitMQ:User") ?? "guest",
                Password = config.GetValue<string>("RabbitMQ:Pass") ?? "guest",
                AutomaticRecoveryEnabled = true,
            };

            _exchange = config.GetValue<string>("RabbitMQ:Exchange") ?? "telemetry_exchange";
            _queueName = config.GetValue<string>("RabbitMQ:Queue") ?? "telemetry_queue";

            // synchronous connect (works fine during startup)
            _connection = _factory.CreateConnection();

            // ensure exchange + queue + binding exist (idempotent)
            using var ch = _connection.CreateModel();

            // durable fanout exchange (messages routed to all bound queues)
            ch.ExchangeDeclare(exchange: _exchange, type: ExchangeType.Fanout, durable: true, autoDelete: false);
            // also we have direct also which use the key 
            // declare the queue (durable so it survives broker restart)
            ch.QueueDeclare(
                queue: _queueName,
                durable: true,
                exclusive: false,
                autoDelete: false,
                arguments: null
            );

            // bind queue to exchange. For fanout routingKey is ignored.
            ch.QueueBind(queue: _queueName, exchange: _exchange, routingKey: "");
        }

        /// <summary>
        /// Publish a single message. Publishing to the exchange will deliver to all bound queues (fanout).
        /// </summary>
        public Task PublishAsync<T>(T payload, CancellationToken ct = default)
        {
            if (ct.IsCancellationRequested) return Task.FromCanceled(ct);

            var body = JsonSerializer.SerializeToUtf8Bytes(payload);

            using var channel = _connection.CreateModel();
            var props = channel.CreateBasicProperties();
            props.Persistent = true; // mark message as persistent; queue must be durable too
            props.ContentType = "application/json";
            props.MessageId = Guid.NewGuid().ToString();
            props.Timestamp = new AmqpTimestamp(DateTimeOffset.UtcNow.ToUnixTimeSeconds());

            // publish to exchange; routingKey ignored for fanout
            channel.BasicPublish(exchange: _exchange, routingKey: "", basicProperties: props, body: body);

            return Task.CompletedTask;
        }

        public void Dispose()
        {
            try { _connection?.Close(); _connection?.Dispose(); } catch { /* ignore */ }
        }
    }
}
