using System.Globalization;
using System.Net.WebSockets;
using System.Text.Json.Serialization;
using System.Threading.Channels;

public record Message(
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("username")] string Username,
    [property: JsonPropertyName("content")] string Content,
    [property: JsonPropertyName("timestamp")] string Timestamp);

public class Client
{
    public Client(WebSocket socket, string username)
    {
        Socket = socket;
        Username = username;
        Send = Channel.CreateBounded<Message>(256);
    }

    public WebSocket Socket { get; }
    public string Username { get; }

    // Outgoing message queue — the equivalent of the Go client's `send` channel.
    public Channel<Message> Send { get; }
}

/// <summary>
/// Central coordinator that manages all active connections, mirroring the Go
/// Hub pattern: register/unregister/broadcast events are processed
/// sequentially by a single background loop to avoid race conditions.
/// </summary>
public class Hub
{
    private abstract record HubEvent;
    private sealed record RegisterEvent(Client Client) : HubEvent;
    private sealed record UnregisterEvent(Client Client) : HubEvent;
    private sealed record BroadcastEvent(Message Message) : HubEvent;

    private readonly object _lock = new();
    private readonly HashSet<Client> _clients = new();
    private readonly List<Message> _history = new();
    private readonly Channel<HubEvent> _events = Channel.CreateUnbounded<HubEvent>();

    public void Register(Client client) => _events.Writer.TryWrite(new RegisterEvent(client));

    public void Unregister(Client client) => _events.Writer.TryWrite(new UnregisterEvent(client));

    public void Broadcast(Message message) => _events.Writer.TryWrite(new BroadcastEvent(message));

    public (int OnlineUsers, List<string> Usernames, int MessageCount) GetStats()
    {
        lock (_lock)
        {
            return (_clients.Count, _clients.Select(c => c.Username).ToList(), _history.Count);
        }
    }

    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        await foreach (var evt in _events.Reader.ReadAllAsync(cancellationToken))
        {
            switch (evt)
            {
                case RegisterEvent(var client):
                    lock (_lock)
                    {
                        _clients.Add(client);
                    }

                    // Send chat history to new client
                    List<Message> history;
                    lock (_lock)
                    {
                        history = _history.ToList();
                    }
                    foreach (var msg in history)
                        client.Send.Writer.TryWrite(msg);

                    // Broadcast join message
                    Broadcast(new Message("system", "", client.Username + " joined the chat", Now()));
                    break;

                case UnregisterEvent(var client):
                    lock (_lock)
                    {
                        if (_clients.Remove(client))
                            client.Send.Writer.TryComplete();
                    }

                    Broadcast(new Message("system", "", client.Username + " left the chat", Now()));
                    break;

                case BroadcastEvent(var message):
                    lock (_lock)
                    {
                        if (message.Type == "message")
                        {
                            _history.Add(message);
                            if (_history.Count > 100)
                                _history.RemoveAt(0);
                        }

                        foreach (var client in _clients.ToList())
                        {
                            // If the client's send queue is full, drop the client
                            if (!client.Send.Writer.TryWrite(message))
                            {
                                client.Send.Writer.TryComplete();
                                _clients.Remove(client);
                            }
                        }
                    }
                    break;
            }
        }
    }

    // RFC 3339 timestamp, like Go's time.Now().UTC().Format(time.RFC3339).
    // InvariantCulture keeps the Gregorian calendar regardless of system locale.
    public static string Now() =>
        DateTime.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
}
