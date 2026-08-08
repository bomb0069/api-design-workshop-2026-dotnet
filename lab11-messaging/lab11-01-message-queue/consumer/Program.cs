using System.Text.Json;
using System.Text.Json.Serialization;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

var rabbitUrl = Environment.GetEnvironmentVariable("RABBITMQ_URL")
    ?? "amqp://guest:guest@localhost:5672/";

var factory = new ConnectionFactory
{
    Uri = new Uri(rabbitUrl),
    DispatchConsumersAsync = true
};

IConnection? connection = null;
Exception? lastError = null;
for (var i = 0; i < 30; i++)
{
    try
    {
        connection = factory.CreateConnection();
        break;
    }
    catch (Exception ex)
    {
        lastError = ex;
        Console.WriteLine($"Waiting for RabbitMQ... ({i + 1}/30)");
        Thread.Sleep(TimeSpan.FromSeconds(2));
    }
}

if (connection is null)
{
    Console.Error.WriteLine($"Failed to connect to RabbitMQ: {lastError?.Message}");
    Environment.Exit(1);
    return;
}

using var conn = connection;
using var channel = conn.CreateModel();

// Declare exchange
channel.ExchangeDeclare("orders", ExchangeType.Topic, durable: true, autoDelete: false, arguments: null);

// Declare queue
var queue = channel.QueueDeclare(
    queue: "order_processing", // name
    durable: true,             // survives broker restart
    exclusive: false,
    autoDelete: false,
    arguments: null);

// Bind queue to exchange with a routing key pattern
channel.QueueBind(
    queue: queue.QueueName,
    exchange: "orders",
    routingKey: "order.*");

// Set prefetch: deliver one message at a time per consumer
channel.BasicQos(prefetchSize: 0, prefetchCount: 1, global: false);

var consumer = new AsyncEventingBasicConsumer(channel);
consumer.Received += async (_, ea) =>
{
    Order? order = null;
    try
    {
        order = JsonSerializer.Deserialize<Order>(ea.Body.Span);
    }
    catch (JsonException)
    {
        // fall through to the nack below
    }

    if (order is null)
    {
        Console.WriteLine("Error parsing message");
        channel.BasicNack(ea.DeliveryTag, multiple: false, requeue: false);
        return;
    }

    Console.WriteLine($"Processing order: {order.Id} - {order.Item} (${order.Amount:F2})");

    // Simulate processing time
    await Task.Delay(TimeSpan.FromSeconds(2));

    Console.WriteLine($"Order processed: {order.Id}");
    channel.BasicAck(ea.DeliveryTag, multiple: false);
};

channel.BasicConsume(
    queue: queue.QueueName,
    autoAck: false,            // manual ack
    consumerTag: "order-consumer",
    consumer: consumer);

Console.WriteLine("Consumer started. Waiting for messages...");

// Block until SIGINT/SIGTERM
var shutdown = new ManualResetEventSlim();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; shutdown.Set(); };
AppDomain.CurrentDomain.ProcessExit += (_, _) => shutdown.Set();
shutdown.Wait();

record Order(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("item")] string Item,
    [property: JsonPropertyName("amount")] double Amount,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("created_at")] string CreatedAt);
