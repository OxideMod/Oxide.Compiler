using System.IO.Pipes;
using Microsoft.Extensions.Logging;
using Oxide.CompilerServices.Enums;
using Oxide.CompilerServices.Interfaces;
using Oxide.CompilerServices.Models.Compiler;
using Oxide.CompilerServices.Models.Configuration;

namespace Oxide.CompilerServices.Services;

public class MessageBrokerService
{
    private const int DefaultMaxBufferSize = 1024;

    private readonly ILogger<MessageBrokerService> _logger;
    private readonly AppConfiguration _appConfiguration;
    private readonly ISerializer _serializer;
    private readonly Pooling.IArrayPool<byte> _arrayPool;

    private NamedPipeClientStream _pipeClient;

    private int _messageId;

    public event Action<CompilerMessage> OnMessageReceived;

    public MessageBrokerService(ILogger<MessageBrokerService> logger, AppConfiguration appConfiguration, ISerializer serializer)
    {
        _logger = logger;
        _appConfiguration = appConfiguration;
        _serializer = serializer;
        _arrayPool = Pooling.ArrayPool<byte>.Shared;
    }

    public async ValueTask StartAsync(CancellationToken cancellationToken)
    {
        _pipeClient = new NamedPipeClientStream(".", _appConfiguration.GetPipeName(), PipeDirection.InOut,
            PipeOptions.Asynchronous);

        await _pipeClient.ConnectAsync(cancellationToken);

        Task.Run(() => WorkerAsync(cancellationToken), cancellationToken);
    }

    private async ValueTask WorkerAsync(CancellationToken cancellationToken)
    {
        while (_pipeClient.IsConnected)
        {
            bool processed = false;

            if (OnMessageReceived != null)
            {
                try
                {
                    CompilerMessage? compilerMessage = await ReadMessageAsync(cancellationToken);
                    if (compilerMessage != null)
                    {
                        _logger.LogInformation($"Received message from server: {compilerMessage.Type}");
                        OnMessageReceived(compilerMessage);
                        processed = true;
                    }
                }
                catch (Exception exception)
                {
                    _logger.LogError($"Error reading message: {exception}");
                }
            }

            if (!processed)
            {
                await Task.Delay(1000, cancellationToken);
            }
        }
    }

    public async ValueTask SendMessageAsync(CompilerMessage message, CancellationToken cancellationToken)
    {
        if (message == null)
        {
            throw new ArgumentNullException(nameof(message));
        }

        await WriteMessageAsync(message, cancellationToken);
    }

    private async ValueTask WriteMessageAsync(CompilerMessage message, CancellationToken cancellationToken)
    {
        byte[] data = _serializer.Serialize(message);
        byte[] buffer = _arrayPool.Take(data.Length + sizeof(int));
        try
        {
            int destinationIndex = data.Length.WriteBigEndian(buffer);
            Array.Copy(data, 0, buffer, destinationIndex, data.Length);
            await OnWriteAsync(buffer, 0, buffer.Length, cancellationToken);
        }
        catch (Exception exception)
        {
            _logger.LogError($"Error sending message to server: {exception}");
        }
        finally
        {
            _arrayPool.Return(buffer);
        }
    }

    private async ValueTask<CompilerMessage?> ReadMessageAsync(CancellationToken cancellationToken)
    {
        byte[] buffer = _arrayPool.Take(sizeof(int));
        int read = 0;
        try
        {
            while (read < buffer.Length)
            {
                read += await OnReadAsync(buffer, read, buffer.Length - read, cancellationToken);
                if (read == 0)
                {
                    return null;
                }
            }

            int length = buffer.ReadBigEndian();
            byte[] buffer2 = _arrayPool.Take(length);
            read = 0;
            try
            {
                while (read < length)
                {
                    read += await OnReadAsync(buffer2, read, length - read, cancellationToken);
                }

                return _serializer.Deserialize<CompilerMessage>(buffer2);
            }
            finally
            {
                _arrayPool.Return(buffer2);
            }
        }
        catch (Exception exception)
        {
            _logger.LogError($"Error reading message: {exception}");
            return null;
        }
        finally
        {
            _arrayPool.Return(buffer);
        }
    }

    private async ValueTask OnWriteAsync(byte[] buffer, int index, int count, CancellationToken cancellationToken)
    {
        Validate(buffer, index, count);

        int remaining = count;
        int written = 0;
        while (remaining > 0)
        {
            int toWrite = Math.Min(DefaultMaxBufferSize, remaining);
            await _pipeClient.WriteAsync(buffer.AsMemory(index + written, toWrite), cancellationToken);
            remaining -= toWrite;
            written += toWrite;
            await _pipeClient.FlushAsync(cancellationToken);
        }
    }

    private async ValueTask<int> OnReadAsync(byte[] buffer, int index, int count, CancellationToken cancellationToken)
    {
        Validate(buffer, index, count);

        int read = 0;
        int remaining = count;
        while (remaining > 0)
        {
            int toRead = Math.Min(DefaultMaxBufferSize, remaining);
            int r = await _pipeClient.ReadAsync(buffer.AsMemory(index + read, toRead), cancellationToken);

            if (r == 0 && read == 0)
            {
                return 0;
            }

            read += r;
            remaining -= r;
        }

        return read;
    }

    public async ValueTask<int> SendReadyMessageAsync(CancellationToken cancellationToken)
    {
        CompilerMessage message = new()
        {
            Id = _messageId++,
            Type = MessageType.Ready,
        };

        await SendMessageAsync(message, cancellationToken);
        return message.Id;
    }

    public void Stop()
    {
        _pipeClient.Dispose();
    }

    private void Validate(byte[] buffer, int index, int count)
    {
        if (buffer == null)
        {
            throw new ArgumentNullException(nameof(buffer));
        }

        if (index < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(index), "Value must be zero or greater");
        }

        if (count < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(count), "Value must be zero or greater");
        }

        if (index + count > buffer.Length)
        {
            throw new ArgumentOutOfRangeException($"{nameof(index)} + {nameof(count)}",
                "Attempted to read more than buffer can allow");
        }
    }
}
