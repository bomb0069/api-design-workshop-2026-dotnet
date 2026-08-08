# Lab 11-02 - MQTT

IoT-style pub/sub messaging with an MQTT broker (Mosquitto) in .NET 8.

## Learning Objectives

- Understand IoT-style pub/sub messaging with MQTT
- Configure and run an MQTT broker (Mosquitto)
- Publish and subscribe to MQTT topics in .NET
- Work with MQTT QoS levels (0, 1, 2)
- Use MQTT wildcards (`+` single-level and `#` multi-level)

## Architecture

```
Publisher (IoT Sensors) ---> Mosquitto Broker (:1883) ---> Subscriber
```

The publisher simulates three IoT sensors that periodically send temperature and humidity readings. The subscriber listens using wildcard subscriptions and processes incoming data. The Mosquitto broker handles message routing between publishers and subscribers. Both apps are .NET console applications using the MQTTnet client library.

## Getting Started

Start all services:

```bash
docker-compose up --build
```

Watch subscriber output:

```bash
docker-compose logs -f subscriber
```

To run an app locally without Docker (requires a running Mosquitto broker):

```bash
cd publisher && dotnet run
cd subscriber && dotnet run
```

## Topics Used

| Topic                        | Description                    | QoS |
|------------------------------|--------------------------------|-----|
| `sensors/{sensor_id}/data`   | Sensor readings                | 1   |
| `sensors/alerts`             | High temperature alerts        | 2   |

## Test Manually with Mosquitto CLI

Subscribe to all sensor topics from inside the broker container:

```bash
docker-compose exec mosquitto mosquitto_sub -t "sensors/#" -v
```

Publish a test message:

```bash
docker-compose exec mosquitto mosquitto_pub -t "sensors/test/data" -m '{"sensor_id":"test","temperature":25.5}'
```

## MQTT vs RabbitMQ

| Feature            | MQTT (Mosquitto)                  | RabbitMQ (AMQP)                    |
|--------------------|-----------------------------------|------------------------------------|
| Protocol           | MQTT (lightweight)                | AMQP (feature-rich)               |
| Design Goal        | IoT, constrained devices          | Enterprise messaging               |
| Broker Complexity  | Simple                            | Complex (exchanges, bindings)      |
| Message Routing    | Topic-based only                  | Exchange types (direct, fanout, topic, headers) |
| QoS Levels         | 0, 1, 2                           | Acknowledgments + confirms         |
| Message Size       | Optimized for small payloads      | No specific optimization           |
| Wildcards          | `+` (single) and `#` (multi)      | `*` (single) and `#` (multi)       |
| Retained Messages  | Built-in                          | Not native (plugin required)       |
| Last Will (LWT)    | Built-in                          | Not native                         |
| Best For           | IoT, sensors, mobile              | Microservices, task queues          |

## QoS Levels

| Level | Name            | Description                                                                 |
|-------|-----------------|-----------------------------------------------------------------------------|
| 0     | At most once    | Fire and forget. No acknowledgment. Messages may be lost.                   |
| 1     | At least once   | Acknowledged delivery. Messages may be delivered more than once.            |
| 2     | Exactly once    | Assured delivery with a four-step handshake. Highest overhead.              |

## MQTT Wildcards

MQTT uses two wildcard characters for topic subscriptions:

### `+` Single-Level Wildcard

Matches exactly one topic level.

```
sensors/+/data
```

Matches:
- `sensors/sensor-01/data`
- `sensors/sensor-02/data`
- `sensors/any-id/data`

Does NOT match:
- `sensors/data`
- `sensors/floor1/sensor-01/data`

### `#` Multi-Level Wildcard

Matches zero or more topic levels. Must be the last character in the subscription.

```
sensors/#
```

Matches:
- `sensors`
- `sensors/sensor-01/data`
- `sensors/alerts`
- `sensors/floor1/sensor-01/data`

## Code Walkthrough

### MQTT Client (MQTTnet)

The lab uses the MQTTnet client library.

**Connect options:**

```csharp
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

client.DisconnectedAsync += async e =>
{
    Console.WriteLine($"Connection lost: {e.Reason}");
    // reconnect loop...
};
```

**Publishing messages:**

```csharp
var message = new MqttApplicationMessageBuilder()
    .WithTopic(topic)
    .WithPayload(payload)
    .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce) // QoS 1
    .Build();
await client.PublishAsync(message);
```

**Subscribing:**

```csharp
await client.SubscribeAsync(new MqttClientSubscribeOptionsBuilder()
    .WithTopicFilter("sensors/+/data", MqttQualityOfServiceLevel.AtLeastOnce)
    .WithTopicFilter("sensors/alerts", MqttQualityOfServiceLevel.ExactlyOnce)
    .WithTopicFilter("sensors/#", MqttQualityOfServiceLevel.AtMostOnce)
    .Build());
```

**Receiving messages:**

Unlike the Go paho client, MQTTnet uses a single received-message event for all subscriptions. The subscriber dispatches each message to the right handler by matching its topic against the subscription filters:

```csharp
client.ApplicationMessageReceivedAsync += e =>
{
    var topic = e.ApplicationMessage.Topic;
    if (MqttTopicFilterComparer.Compare(topic, "sensors/+/data") == MqttTopicFilterCompareResult.IsMatch)
    {
        // handle sensor data
    }
    return Task.CompletedTask;
};
```

## Exercises

1. **Retained Messages** - Modify the publisher to send retained messages (`.WithRetainFlag(true)`) for the last known state of each sensor. When a new subscriber connects, it should immediately receive the latest reading for each sensor.

2. **Last Will and Testament (LWT)** - Add a Last Will and Testament message to the publisher (`.WithWillTopic(...)`, `.WithWillPayload(...)`) so that when a sensor disconnects unexpectedly, the broker automatically publishes a status message to `sensors/{sensor_id}/status` with a payload of `offline`.

3. **Dashboard Subscriber** - Create a new subscriber that calculates running averages of temperature and humidity per sensor and periodically prints a summary table.

4. **Device Commands** - Implement bidirectional communication by having the publisher also subscribe to `sensors/{id}/commands`. Create a separate command publisher that sends control messages (e.g., change reporting interval, recalibrate sensor).

## Key Concepts

| Concept            | Description                                                                 |
|--------------------|-----------------------------------------------------------------------------|
| MQTT Protocol      | Lightweight publish/subscribe messaging protocol designed for IoT           |
| Pub/Sub Pattern    | Decoupled messaging where publishers and subscribers communicate via topics |
| QoS Levels         | Quality of Service guarantees (0: at most once, 1: at least once, 2: exactly once) |
| Topic Wildcards    | `+` for single-level and `#` for multi-level topic matching                |
| Retained Messages  | Broker stores the last message on a topic for new subscribers              |

## Cleanup

Stop and remove all containers and volumes:

```bash
docker-compose down -v
```
