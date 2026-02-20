using System.Buffers;
using System.Buffers.Binary;
using System.IO.Pipes;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Oxide.CompilerServices.Serialization;
using Oxide.CompilerServices.Types.Configuration;

namespace Oxide.CompilerServices.Services;

public class MessageBrokerService
{
    private readonly ILogger<MessageBrokerService> _logger;
    private readonly AppConfiguration _appConfiguration;
    private NamedPipeClientStream _pipeClient;
    private int _messageId;
    public event Action<CompilerMessage> OnMessageReceived;

    public MessageBrokerService(ILogger<MessageBrokerService> logger, AppConfiguration appConfiguration)
    {
        _logger = logger;
        _appConfiguration = appConfiguration;
    }

    public async ValueTask StartAsync(CancellationToken cancellationToken)
    {
        _pipeClient = new NamedPipeClientStream(".", _appConfiguration.GetPipeName(), PipeDirection.InOut,
            PipeOptions.Asynchronous);

        await _pipeClient.ConnectAsync(cancellationToken);

        _ = Task.Run(() => WorkerAsync(cancellationToken), cancellationToken);
    }

    private async ValueTask WorkerAsync(CancellationToken cancellationToken)
    {
        while (_pipeClient.IsConnected)
        {
            if (OnMessageReceived == null)
            {
                await Task.Delay(1000, cancellationToken);
                continue;
            }

            try
            {
                CompilerMessage? compilerMessage = await ReadMessageAsync(cancellationToken);
                if (compilerMessage == null)
                {
                    _logger.LogError("Received null message from server, skipping");
                    continue;
                }

                OnMessageReceived(compilerMessage);
            }
            catch (Exception exception)
            {
                _logger.LogError($"Error reading message: {exception}");
            }
        }
    }

    public async ValueTask SendMessageAsync(CompilerMessage message, CancellationToken cancellationToken)
    {
        await WriteMessageAsync(message, cancellationToken);
    }

    private async ValueTask WriteMessageAsync(CompilerMessage message, CancellationToken cancellationToken)
    {
        try
        {
            ArrayBufferWriter<byte> bufferWriter = new();

            await using Utf8JsonWriter jsonWriter = new(bufferWriter);

            JsonSerializer.Serialize(jsonWriter, message, CompilerMessageContext.Default.CompilerMessage);

            ReadOnlyMemory<byte> payload = bufferWriter.WrittenMemory;

            Span<byte> lengthBuffer = stackalloc byte[sizeof(int)];
            BinaryPrimitives.WriteInt32BigEndian(lengthBuffer, payload.Length);

            _pipeClient.Write(lengthBuffer);

            await _pipeClient.WriteAsync(payload, cancellationToken);
        }
        catch (Exception exception)
        {
            _logger.LogError($"Error sending message to server: {exception}");
        }
    }

    private async ValueTask<CompilerMessage?> ReadMessageAsync(CancellationToken cancellationToken)
    {
        if (_pipeClient is not { IsConnected: true })
        {
            return null;
        }

        byte[] lengthBuffer = ArrayPool<byte>.Shared.Rent(sizeof(int));
        try
        {
            await _pipeClient.ReadExactlyAsync(lengthBuffer.AsMemory(0, sizeof(int)), cancellationToken);

            int messageLength = BinaryPrimitives.ReadInt32BigEndian(lengthBuffer.AsSpan(0, sizeof(int)));
            if (messageLength <= 0)
            {
                return null;
            }

            byte[] messageBuffer = ArrayPool<byte>.Shared.Rent(messageLength);
            try
            {
                await _pipeClient.ReadExactlyAsync(messageBuffer.AsMemory(0, messageLength), cancellationToken);

                return JsonSerializer.Deserialize<CompilerMessage>(messageBuffer.AsSpan(0, messageLength),
                    CompilerMessageContext.Default.CompilerMessage);
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(messageBuffer);
            }
        }
        catch (Exception exception)
        {
            _logger.LogError($"Error reading message from named pipe: {exception}");
            return null;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(lengthBuffer);
        }
    }

    public async ValueTask<int> SendReadyMessageAsync(CancellationToken cancellationToken)
    {
        CompilerMessage message = new()
        {
            Id = _messageId++,
            Type = MessageType.Ready,
        };

        _logger.LogInformation("Sending ready message to server");
        await SendMessageAsync(message, cancellationToken);
        return message.Id;
    }

    public void Stop()
    {
        _pipeClient.Dispose();
    }
}
