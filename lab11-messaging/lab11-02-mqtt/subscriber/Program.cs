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
    .WithClientId("sensor-subscriber")
    .Build();

// Route incoming messages to handlers by topic filter.
// A message matching several filters triggers every matching handler,
// mirroring the per-subscription callbacks of the Go (paho) version.
client.ApplicationMessageReceivedAsync += e =>
{
    var topic = e.ApplicationMessage.Topic;
    var payload = e.ApplicationMessage.PayloadSegment.ToArray();

    // Single-level wildcard: sensors/+/data matches sensors/sensor-01/data, etc.
    if (MqttTopicFilterComparer.Compare(topic, "sensors/+/data") == MqttTopicFilterCompareResult.IsMatch)
    {
        HandleSensorData(topic, payload);
    }

    if (MqttTopicFilterComparer.Compare(topic, "sensors/alerts") == MqttTopicFilterCompareResult.IsMatch)
    {
        HandleAlert(payload);
    }

    // Multi-level wildcard: sensors/# matches everything under sensors/
    if (MqttTopicFilterComparer.Compare(topic, "sensors/#") == MqttTopicFilterCompareResult.IsMatch)
    {
        Console.WriteLine($"[ALL] Topic: {topic} | Payload size: {payload.Length} bytes");
    }

    return Task.CompletedTask;
};

client.ConnectedAsync += async _ =>
{
    Console.WriteLine("Connected to MQTT broker");
    await SubscribeAsync(client);
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

Console.WriteLine("Subscriber started. Listening for messages...");

// Block until SIGINT/SIGTERM
var shutdown = new ManualResetEventSlim();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; shutdown.Set(); };
AppDomain.CurrentDomain.ProcessExit += (_, _) => shutdown.Set();
shutdown.Wait();

Console.WriteLine("Shutting down subscriber...");
await client.DisconnectAsync();

static async Task SubscribeAsync(IMqttClient client)
{
    // Subscribe to all sensor data using the single-level wildcard (QoS 1),
    // alerts with QoS 2, and everything under sensors/ with QoS 0.
    await client.SubscribeAsync(new MqttClientSubscribeOptionsBuilder()
        .WithTopicFilter("sensors/+/data", MqttQualityOfServiceLevel.AtLeastOnce)
        .WithTopicFilter("sensors/alerts", MqttQualityOfServiceLevel.ExactlyOnce)
        .WithTopicFilter("sensors/#", MqttQualityOfServiceLevel.AtMostOnce)
        .Build());
}

static void HandleSensorData(string topic, byte[] payload)
{
    SensorData? data = null;
    try
    {
        data = JsonSerializer.Deserialize<SensorData>(payload);
    }
    catch (JsonException ex)
    {
        Console.WriteLine($"Error parsing message: {ex.Message}");
        return;
    }
    if (data is null)
    {
        return;
    }
    Console.WriteLine($"[DATA] {data.Timestamp} | Sensor: {data.SensorId} | Temp: {data.Temperature:F1}°C | Humidity: {data.Humidity:F1}% | Topic: {topic}");
}

static void HandleAlert(byte[] payload)
{
    Dictionary<string, JsonElement>? alert;
    try
    {
        alert = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(payload);
    }
    catch (JsonException)
    {
        return;
    }
    if (alert is null)
    {
        return;
    }
    Console.WriteLine($"[ALERT] {alert["timestamp"]} | Sensor: {alert["sensor_id"]} | Type: {alert["type"]} | " +
        $"Value: {alert["value"].GetDouble():F1} | Threshold: {alert["threshold"].GetDouble():F0}");
}

record SensorData(
    [property: JsonPropertyName("sensor_id")] string SensorId,
    [property: JsonPropertyName("temperature")] double Temperature,
    [property: JsonPropertyName("humidity")] double Humidity,
    [property: JsonPropertyName("timestamp")] string Timestamp);
