using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;

var server = new ChatServer(5000);
await server.StartAsync();

internal sealed class ChatServer
{
    private readonly TcpListener _listener;
    private readonly ConcurrentDictionary<TcpClient, ClientState> _clients = new();
    private readonly List<string> _messageHistory = new();
    private readonly object _historyLock = new();

    public ChatServer(int port)
    {
        _listener = new TcpListener(IPAddress.Any, port);
    }

    public async Task StartAsync()
    {
        _listener.Start();
        Console.WriteLine("Chat server running on 0.0.0.0:5000");

        while (true)
        {
            var client = await _listener.AcceptTcpClientAsync();
            _ = HandleClientAsync(client);
        }
    }

    private async Task HandleClientAsync(TcpClient client)
    {
        var state = new ClientState(client);

        try
        {
            var stream = client.GetStream();
            state.Reader = new StreamReader(stream);
            state.Writer = new StreamWriter(stream) { AutoFlush = true };

            var userName = await state.Reader.ReadLineAsync();
            if (string.IsNullOrWhiteSpace(userName))
            {
                client.Close();
                return;
            }

            state.UserName = userName.Trim();
            _clients[client] = state;

            await SendHistoryAsync(state);
            await BroadcastAsync($"{state.UserName} joined the chat.");
            Console.WriteLine($"{state.UserName} connected.");

            while (true)
            {
                var line = await state.Reader.ReadLineAsync();
                if (line == null)
                {
                    break;
                }

                var message = line.Trim();
                if (message.Length == 0)
                {
                    continue;
                }

                var formattedMessage = $"{state.UserName}: {message}";
                await BroadcastAsync(formattedMessage);
            }
        }
        catch (IOException)
        {
        }
        catch (ObjectDisposedException)
        {
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Client error: {ex.Message}");
        }
        finally
        {
            if (_clients.TryRemove(client, out var removed))
            {
                if (!string.IsNullOrWhiteSpace(removed.UserName))
                {
                    await BroadcastAsync($"{removed.UserName} left the chat.");
                    Console.WriteLine($"{removed.UserName} disconnected.");
                }
            }

            client.Close();
        }
    }

    private async Task BroadcastAsync(string message)
    {
        AddHistory(message);
        foreach (var pair in _clients)
        {
            try
            {
                if (pair.Value.Writer != null)
                {
                    await pair.Value.Writer.WriteLineAsync(message);
                }
            }
            catch (IOException)
            {
            }
            catch (ObjectDisposedException)
            {
            }
        }
    }

    private void AddHistory(string message)
    {
        lock (_historyLock)
        {
            _messageHistory.Add(message);
            if (_messageHistory.Count > 100)
            {
                _messageHistory.RemoveRange(0, _messageHistory.Count - 100);
            }
        }
    }

    private async Task SendHistoryAsync(ClientState state)
    {
        if (state.Writer == null)
        {
            return;
        }

        List<string> snapshot;
        lock (_historyLock)
        {
            snapshot = new List<string>(_messageHistory);
        }

        foreach (var message in snapshot)
        {
            await state.Writer.WriteLineAsync(message);
        }
    }
}

internal sealed class ClientState
{
    public ClientState(TcpClient client)
    {
        Client = client;
    }

    public TcpClient Client { get; }
    public string UserName { get; set; } = string.Empty;
    public StreamReader Reader { get; set; }
    public StreamWriter Writer { get; set; }
}
