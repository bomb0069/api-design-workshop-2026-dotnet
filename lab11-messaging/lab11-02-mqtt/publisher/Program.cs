using System.Text.Json;
using System.Text.Json.Serialization;
using MQTTnet;
using MQTTnet.Client;
using MQTTnet.Protocol;

var broker = Environment.GetEnvironmentVariable("MQTT_BROKER") ?? "tcp://localhost:1883";
var brokerUri = new Uri(broker);

var factory = new MqttFactory();
using var client = factory.CreateMqttClient();

var options = new MqttClientOptionsBuilder()
    .WithTcpServer(brokerUri.Host, brokerUri.Port)
    .WithClientId("sensor-publisher")
    .Build();

client.ConnectedAsync += _ =>
{
    Console.WriteLine("Connected to MQTT broker");
    return Task.CompletedTask;
};

// Auto-reconnect: when an established connection drops, keep retrying until it is back.
client.DisconnectedAsync += async e =>
{
    if (!e.ClientWasConnected)
    {
        return;
    }
    Console.WriteLine($"Connection lost: {e.Reason}");
    while (!client.IsConnected)
    {
        try
        {
            await client.ConnectAsync(options);
        }
        catch (Exception)
        {
            await Task.Delay(TimeSpan.FromSeconds(2));
        }
    }
};

for (var i = 0; i < 30; i++)
{
    try
    {
        await client.ConnectAsync(options);
        break;
    }
    catch (Exception)
    {
        Console.WriteLine($"Waiting for MQTT broker... ({i + 1}/30)");
        await Task.Delay(TimeSpan.FromSeconds(2));
    }
}

if (!client.IsConnected)
{
    Console.Error.WriteLine("Failed to connect to MQTT broker");
    Environment.Exit(1);
    return;
}

var sensors = new[] { "sensor-01", "sensor-02", "sensor-03" };
var random = Random.Shared;

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };
AppDomain.CurrentDomain.ProcessExit += (_, _) => cts.Cancel();

Console.WriteLine("Publisher started. Sending sensor data every 3 seconds...");

// Publish sensor data periodically
using var timer = new PeriodicTimer(TimeSpan.FromSeconds(3));
try
{
    while (await timer.WaitForNextTickAsync(cts.Token))
    {
        foreach (var sensorId in sensors)
        {
            var data = new SensorData(
                SensorId: sensorId,
                Temperature: 20 + random.NextDouble() * 15,
                Humidity: 40 + random.NextDouble() * 40,
                Timestamp: DateTime.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'"));

            var payload = JsonSerializer.SerializeToUtf8Bytes(data);
            var topic = $"sensors/{sensorId}/data";

            // QoS 1 = At least once delivery
            var message = new MqttApplicationMessageBuilder()
                .WithTopic(topic)
                .WithPayload(payload)
                .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce)
                .Build();
            await client.PublishAsync(message, cts.Token);

            Console.WriteLine($"Published to {topic}: temp={data.Temperature:F1}°C humidity={data.Humidity:F1}%");
        }

        // Also publish an alert if temperature is high
        foreach (var sensorId in sensors)
        {
            var temp = 20 + random.NextDouble() * 20;
            if (temp > 32)
            {
                var alert = new Dictionary<string, object>
                {
                    ["sensor_id"] = sensorId,
                    ["type"] = "high_temperature",
                    ["value"] = temp,
                    ["threshold"] = 32.0,
                    ["timestamp"] = DateTime.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'")
                };
                var payload = JsonSerializer.SerializeToUtf8Bytes(alert);

                // QoS 2 = Exactly once
                var message = new MqttApplicationMessageBuilder()
                    .WithTopic("sensors/alerts")
                    .WithPayload(payload)
                    .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.ExactlyOnce)
                    .Build();
                await client.PublishAsync(message, cts.Token);

                Console.WriteLine($"ALERT published for {sensorId}: temp={temp:F1}°C");
            }
        }
    }
}
catch (OperationCanceledException)
{
    // shutdown requested
}

Console.WriteLine("Shutting down publisher...");
await client.DisconnectAsync();

record SensorData(
    [property: JsonPropertyName("sensor_id")] string SensorId,
    [property: JsonPropertyName("temperature")] double Temperature,
    [property: JsonPropertyName("humidity")] double Humidity,
    [property: JsonPropertyName("timestamp")] string Timestamp);
