# Lab 09-02: WebSocket

Real-time bidirectional communication with WebSocket in ASP.NET Core - building a simple chat system.

## Learning Objectives

- Implement a WebSocket server with ASP.NET Core's built-in WebSocket support
- Understand real-time bidirectional communication between client and server
- Apply the Hub/Client pattern for managing multiple concurrent connections
- Build a simple chat application with live message broadcasting

## Getting Started

```bash
docker-compose up --build
# or locally:
dotnet run
```

Open [http://localhost:8080](http://localhost:8080) in **multiple browser tabs** to simulate different chat users. Enter a username in each tab and start chatting.

## Using from CLI

You can also test with `wscat` (install via `npm install -g wscat`):

```bash
wscat -c "ws://localhost:8080/ws?username=CLI"
```

Then send messages as JSON:

```json
{"content": "Hello from the terminal!"}
```

## Stats Endpoint

Check the stats endpoint to see online users and message count:

```bash
curl http://localhost:8080/stats
```

Example response:

```json
{
  "online_users": 2,
  "usernames": ["Alice", "Bob"],
  "message_count": 5
}
```

## Code Walkthrough

### WebSocket Upgrade

ASP.NET Core handles the HTTP-to-WebSocket protocol upgrade with the WebSocket middleware plus `AcceptWebSocketAsync`:

```csharp
app.UseWebSockets();

app.Map("/ws", async (HttpContext context) =>
{
    using var socket = await context.WebSockets.AcceptWebSocketAsync();
    // ...
});
```

(No origin check is performed, matching the Go version's `CheckOrigin` that allows all origins for development.)

### Hub Pattern

The `Hub` is the central coordinator that manages all active connections. Register, unregister, and broadcast events all flow through a single `Channel<HubEvent>`:

- **Register** - New clients connect and are added to the client set
- **Unregister** - Disconnected clients are removed and their send channel is completed
- **Broadcast** - Messages are fanned out to every connected client

The Hub runs in its own background task (`hub.RunAsync()`) and processes events sequentially from the channel, avoiding race conditions — the same role Go's `select` loop plays.

### Client Read/Write Pumps

Each connected client runs two async loops:

- **ReadPumpAsync** - Reads incoming messages from the WebSocket connection and forwards them to the Hub's broadcast channel
- **WritePumpAsync** - Reads from the client's `Send` channel and writes messages to the WebSocket connection

This separation ensures that reading and writing never block each other.

### Chat History

The Hub maintains the last 100 messages in memory. When a new client connects, it receives the full history so it can see recent conversation context.

## WebSocket vs REST Comparison

| Feature | REST | WebSocket |
|---------|------|-----------|
| Connection | New connection per request | Persistent connection |
| Direction | Client-initiated only | Bidirectional |
| Overhead | HTTP headers on every request | Minimal framing after handshake |
| Use Case | CRUD operations | Real-time data (chat, games, live feeds) |
| Scaling | Stateless, easy to scale | Stateful, requires sticky sessions or pub/sub |

## Exercises

1. **Add typing indicators** - Broadcast a "user is typing..." message when a user starts typing, and clear it when they stop or send a message.

2. **Add private messaging** - Implement a `/dm username message` command that sends a message only to the specified user.

3. **Add chat rooms/channels** - Allow users to create and join different chat rooms, with messages scoped to the room.

4. **Add message persistence with PostgreSQL** - Store messages in a database so chat history survives server restarts.

## Key Concepts

- **WebSocket Protocol** - A full-duplex communication protocol over a single TCP connection, initiated via an HTTP upgrade handshake.
- **Hub/Client Pattern** - A central hub manages all connections and coordinates message routing; each client has its own read and write loops.
- **Async Tasks for Concurrent Connections** - Each client connection is handled by dedicated async tasks, enabling the server to manage thousands of simultaneous connections efficiently.
- **Real-time Communication** - Messages are delivered instantly to all connected clients without polling, providing a responsive user experience.

## Cleanup

```bash
docker-compose down
```
