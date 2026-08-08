# Lab 11-01 - Message Queue

Asynchronous communication between services using RabbitMQ in .NET 8.

## Learning Objectives

- Async communication with RabbitMQ
- Publisher/consumer pattern
- Message exchanges, queues, and bindings
- Manual acknowledgment
- Message persistence

## Architecture

```
Publisher (HTTP API :8080)  -->  RabbitMQ (:5672)  -->  Consumer (Worker)
    POST /orders                 Exchange: orders       Queue: order_processing
                                 Type: topic            Binding: order.*
```

The publisher is an ASP.NET Core minimal API; the consumer is a .NET console worker. Both use the `RabbitMQ.Client` library.

## Getting Started

```bash
docker-compose up --build
```

Access the RabbitMQ Management UI at [http://localhost:15672](http://localhost:15672) with credentials `guest` / `guest`.

To run a project locally without Docker (requires a running RabbitMQ):

```bash
cd publisher && dotnet run
cd consumer && dotnet run
```

## Test Workflow

### 1. Send an Order

```bash
curl -s -X POST http://localhost:8080/orders \
  -H "Content-Type: application/json" \
  -d '{"item":"Laptop","amount":999.99}' | jq
```

### 2. Watch Consumer Logs

```bash
docker-compose logs -f consumer
```

### 3. Send Multiple Orders Rapidly

```bash
for i in $(seq 1 5); do
  curl -s -X POST http://localhost:8080/orders \
    -H "Content-Type: application/json" \
    -d "{\"item\":\"Item-$i\",\"amount\":$((i * 100))}" | jq
done
```

Watch them queue up and get processed one by one (2-second simulated processing time each).

### 4. Scale Consumers

```bash
docker-compose up -d --scale consumer=3
```

Now send multiple orders again and observe how they are distributed across consumers:

```bash
docker-compose logs -f consumer
```

### 5. Health Check

```bash
curl -s http://localhost:8080/health | jq
```

## Code Walkthrough

### Publisher (Order API)

| Concept | Code |
|---------|------|
| Exchange declaration | `channel.ExchangeDeclare("orders", ExchangeType.Topic, durable: true, ...)` - declares a durable topic exchange |
| Publishing with persistence | `props.DeliveryMode = 2` - messages survive broker restart |
| Routing key | `"order.created"` - used by exchange to route messages to bound queues |

### Consumer (Worker)

| Concept | Code |
|---------|------|
| Queue declaration | `channel.QueueDeclare("order_processing", durable: true, ...)` - durable queue survives restart |
| Binding with routing key pattern | `channel.QueueBind(queue, "orders", "order.*")` - matches any `order.X` routing key |
| Manual acknowledgment | `channel.BasicAck(ea.DeliveryTag, false)` - message removed from queue only after successful processing |
| Negative acknowledgment | `channel.BasicNack(ea.DeliveryTag, false, false)` - reject message without requeue on parse errors |
| Prefetch / QoS | `channel.BasicQos(0, 1, false)` - deliver one message at a time per consumer |
| Async consumer | `AsyncEventingBasicConsumer` with `DispatchConsumersAsync = true` - handle deliveries with async/await |

## RabbitMQ Concepts

```
                         Binding
Publisher --> Exchange ──────────────> Queue --> Consumer
              (orders)  routing key   (order_processing)
                        "order.*"
```

A **publisher** sends messages to an **exchange**. The exchange routes messages to **queues** based on **bindings** and **routing keys**. A **consumer** reads from a queue and acknowledges each message after processing.

## Exchange Types

| Type | Routing Behavior |
|------|-----------------|
| **direct** | Routes to queues where the routing key exactly matches the binding key |
| **topic** | Routes to queues where the routing key matches a pattern (`*` = one word, `#` = zero or more words) |
| **fanout** | Routes to all bound queues, ignoring routing keys |
| **headers** | Routes based on message header attributes instead of routing key |

This lab uses a **topic** exchange so the consumer binding `order.*` matches routing keys like `order.created`, `order.updated`, `order.cancelled`, etc.

## Exercises

1. **Dead Letter Queue** - Add a dead letter queue for failed messages. Configure the `order_processing` queue with `x-dead-letter-exchange` and `x-dead-letter-routing-key` arguments. Intentionally fail some messages and verify they land in the DLQ.

2. **Notification Queue (Fanout)** - Create a second exchange of type `fanout` for notifications. When an order is processed, publish a notification message. Add a separate consumer that simulates sending email/SMS notifications.

3. **Message Priority** - Add priority support to the queue using `x-max-priority`. Modify the publisher to accept a priority field and set `props.Priority` on the message. Verify that higher-priority orders are processed first.

4. **Analytics Consumer** - Add a separate consumer bound to `order.#` on the same exchange that logs order data for analytics/reporting without interfering with the main processing consumer.

## Key Concepts

| Concept | Description |
|---------|-------------|
| **Message Queue** | A buffer that stores messages between producers and consumers, enabling async communication |
| **Exchange Types** | Direct (exact match), Topic (pattern match), Fanout (broadcast), Headers (attribute match) |
| **Routing Keys** | Labels attached to messages that exchanges use to determine which queues receive the message |
| **Acknowledgment** | Manual ack ensures messages are only removed from the queue after successful processing |
| **Prefetch** | QoS setting that limits how many unacknowledged messages a consumer receives at once |

## Cleanup

```bash
docker-compose down -v
```
