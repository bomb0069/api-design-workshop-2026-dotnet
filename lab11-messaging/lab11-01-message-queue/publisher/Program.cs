using System.Text.Json;
using System.Text.Json.Serialization;
using RabbitMQ.Client;

var rabbitUrl = Environment.GetEnvironmentVariable("RABBITMQ_URL")
    ?? "amqp://guest:guest@localhost:5672/";

var factory = new ConnectionFactory { Uri = new Uri(rabbitUrl) };

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

var channel = connection.CreateModel();

// Declare exchange
channel.ExchangeDeclare(
    exchange: "orders",   // name
    type: ExchangeType.Topic,
    durable: true,
    autoDelete: false,
    arguments: null);

// IModel is not thread-safe, so serialize publishes across concurrent requests.
var publishLock = new object();

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls("http://0.0.0.0:8080");
var app = builder.Build();

app.MapPost("/orders", async (HttpContext context) =>
{
    OrderInput? input = null;
    try
    {
        input = await JsonSerializer.DeserializeAsync<OrderInput>(context.Request.Body);
    }
    catch (JsonException)
    {
        // fall through to the bad-request response below
    }

    if (input is null)
    {
        return Results.Json(new { error = "Invalid request body" }, statusCode: StatusCodes.Status400BadRequest);
    }

    var order = new Order(
        Id: $"ORD-{(DateTime.UtcNow - DateTime.UnixEpoch).Ticks * 100}",
        Item: input.Item ?? "",
        Amount: input.Amount,
        Status: "created",
        CreatedAt: DateTime.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'"));

    var body = JsonSerializer.SerializeToUtf8Bytes(order);

    try
    {
        lock (publishLock)
        {
            var props = channel.CreateBasicProperties();
            props.ContentType = "application/json";
            props.DeliveryMode = 2; // persistent
            props.Timestamp = new AmqpTimestamp(DateTimeOffset.UtcNow.ToUnixTimeSeconds());

            channel.BasicPublish(
                exchange: "orders",         // exchange
                routingKey: "order.created", // routing key
                mandatory: false,
                basicProperties: props,
                body: body);
        }
    }
    catch (Exception)
    {
        return Results.Json(new { error = "Failed to publish message" }, statusCode: StatusCodes.Status500InternalServerError);
    }

    Console.WriteLine($"Published order: {order.Id}");

    return Results.Json(new
    {
        message = "Order accepted for processing",
        order
    }, statusCode: StatusCodes.Status202Accepted);
});

app.MapGet("/health", () => Results.Json(new { status = "ok" }));

Console.WriteLine("Publisher (Order API) starting on :8080");

app.Lifetime.ApplicationStopped.Register(() =>
{
    channel.Close();
    connection.Close();
});

app.Run();

record Order(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("item")] string Item,
    [property: JsonPropertyName("amount")] double Amount,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("created_at")] string CreatedAt);

record OrderInput(
    [property: JsonPropertyName("item")] string? Item,
    [property: JsonPropertyName("amount")] double Amount);
