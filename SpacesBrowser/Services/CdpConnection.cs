using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text.Json;

namespace SpacesBrowser.Services;

internal sealed class CdpConnection : IAsyncDisposable
{
    private readonly ClientWebSocket _socket = new();
    private readonly ConcurrentDictionary<int, TaskCompletionSource<JsonElement>> _pending = new();
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private readonly CancellationTokenSource _lifetime = new();
    private Task? _receiveTask;
    private int _nextId;

    public Func<string, JsonElement, string?, Task>? EventReceived { get; set; }
    public Task Completion => _receiveTask ?? Task.CompletedTask;

    public async Task ConnectAsync(Uri endpoint, CancellationToken cancellationToken)
    {
        await _socket.ConnectAsync(endpoint, cancellationToken);
        _receiveTask = ReceiveLoopAsync(_lifetime.Token);
    }

    public async Task<JsonElement> SendCommandAsync(
        string method,
        object? parameters = null,
        string? sessionId = null,
        CancellationToken cancellationToken = default)
    {
        var id = Interlocked.Increment(ref _nextId);
        var completion = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_pending.TryAdd(id, completion))
        {
            throw new InvalidOperationException("Не удалось зарегистрировать DevTools-команду.");
        }

        var message = new Dictionary<string, object?>
        {
            ["id"] = id,
            ["method"] = method
        };
        if (parameters is not null)
        {
            message["params"] = parameters;
        }
        if (!string.IsNullOrWhiteSpace(sessionId))
        {
            message["sessionId"] = sessionId;
        }

        var payload = JsonSerializer.SerializeToUtf8Bytes(message);
        try
        {
            await _sendLock.WaitAsync(cancellationToken);
            try
            {
                await _socket.SendAsync(payload, WebSocketMessageType.Text, true, cancellationToken);
            }
            finally
            {
                _sendLock.Release();
            }

            return await completion.Task.WaitAsync(cancellationToken);
        }
        catch
        {
            _pending.TryRemove(id, out _);
            throw;
        }
    }

    private async Task ReceiveLoopAsync(CancellationToken cancellationToken)
    {
        var buffer = new byte[32 * 1024];
        Exception? terminalError = null;

        try
        {
            while (_socket.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
            {
                using var stream = new MemoryStream();
                WebSocketReceiveResult result;
                do
                {
                    result = await _socket.ReceiveAsync(buffer, cancellationToken);
                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        return;
                    }
                    stream.Write(buffer, 0, result.Count);
                } while (!result.EndOfMessage);

                using var document = JsonDocument.Parse(stream.ToArray());
                var root = document.RootElement;
                if (root.TryGetProperty("id", out var idElement)
                    && _pending.TryRemove(idElement.GetInt32(), out var completion))
                {
                    if (root.TryGetProperty("error", out var error))
                    {
                        completion.TrySetException(new InvalidOperationException(error.ToString()));
                    }
                    else if (root.TryGetProperty("result", out var commandResult))
                    {
                        completion.TrySetResult(commandResult.Clone());
                    }
                    else
                    {
                        completion.TrySetResult(JsonDocument.Parse("{}").RootElement.Clone());
                    }
                    continue;
                }

                if (root.TryGetProperty("method", out var methodElement) && EventReceived is not null)
                {
                    var method = methodElement.GetString() ?? string.Empty;
                    var parameters = root.TryGetProperty("params", out var paramsElement)
                        ? paramsElement.Clone()
                        : JsonDocument.Parse("{}").RootElement.Clone();
                    var sessionId = root.TryGetProperty("sessionId", out var sessionElement)
                        ? sessionElement.GetString()
                        : null;
                    var handler = EventReceived;
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            await handler(method, parameters, sessionId);
                        }
                        catch
                        {
                            // A single closed tab must not stop timezone handling for the profile.
                        }
                    }, CancellationToken.None);
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            terminalError = ex;
        }
        finally
        {
            var exception = terminalError ?? new IOException("DevTools-соединение закрыто.");
            foreach (var pending in _pending.Values)
            {
                pending.TrySetException(exception);
            }
            _pending.Clear();
        }
    }

    public async ValueTask DisposeAsync()
    {
        _lifetime.Cancel();
        if (_socket.State is WebSocketState.Open or WebSocketState.CloseReceived)
        {
            try
            {
                await _socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "done", CancellationToken.None);
            }
            catch
            {
            }
        }

        if (_receiveTask is not null)
        {
            try
            {
                await _receiveTask;
            }
            catch
            {
            }
        }

        _socket.Dispose();
        _sendLock.Dispose();
        _lifetime.Dispose();
    }
}
