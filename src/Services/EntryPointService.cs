using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Oxide.CompilerServices.Common;
using Oxide.CompilerServices.Interfaces;
using Oxide.CompilerServices.Serialization;
using Oxide.CompilerServices.Types.Compilation;
using Oxide.CompilerServices.Types.Configuration;
using ILogger = Microsoft.Extensions.Logging.ILogger;

namespace Oxide.CompilerServices.Services;

public class EntryPointService : IEntryPointService
{
    private readonly ILogger _logger;
    private readonly AppConfiguration _appConfiguration;
    private readonly IHostApplicationLifetime _appLifetime;
    private readonly MessageBrokerService _messageBrokerService;
    private readonly ICompilationService _compilationService;

    public EntryPointService(ILogger<EntryPointService> logger, AppConfiguration appConfiguration,
        IHostApplicationLifetime appLifetime, MessageBrokerService messageBrokerService,
        ICompilationService compilationService)
    {
        Constants.ApplicationLogLevel.MinimumLevel = appConfiguration.GetLoggingConfiguration().Level.ToSerilog();

        _logger = logger;
        _appConfiguration = appConfiguration;
        _appLifetime = appLifetime;
        _messageBrokerService = messageBrokerService;
        _compilationService = compilationService;
    }

    public async ValueTask StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation($"Starting compiler v{Assembly.GetExecutingAssembly().GetName().Version}. . .");
        _logger.LogInformation($"Minimal logging level is set to {Constants.ApplicationLogLevel.MinimumLevel}");

        Thread.CurrentThread.CurrentCulture = CultureInfo.InvariantCulture;
        Thread.CurrentThread.Priority = ThreadPriority.BelowNormal;
        Thread.CurrentThread.IsBackground = true;
        AppDomain.CurrentDomain.ProcessExit += (_, _) => RequestShutdown("SIGTERM");
        Console.CancelKeyPress += (_, _) => RequestShutdown("SIGINT (Ctrl + C)");

        Process? parentProcess = _appConfiguration.GetParentProcess();
        if (parentProcess == null)
        {
            _logger.LogWarning("Parent process is not defined, compiler may stay open if parent is improperly shutdown");
            return;
        }

        try
        {
            if (!parentProcess.HasExited)
            {
                parentProcess.EnableRaisingEvents = true;
                parentProcess.Exited += (_, _) => RequestShutdown("parent process shutdown");

                _logger.LogInformation("Watching parent process ([{0}] {1}) for shutdown",
                    parentProcess.Id, parentProcess.ProcessName);
            }
            else
            {
                RequestShutdown("parent process exited");
                return;
            }
        }
        catch (Exception exception)
        {
            _logger.LogError(exception,
                "Failed to attach to parent process, compiler may stay open if parent is improperly shutdown");
        }

        if (!_appConfiguration.GetCompilerConfiguration().EnableMessageStream)
        {
            _logger.LogWarning("Message stream is disabled, compiler will not receive compile jobs");
            return;
        }

        await _messageBrokerService.StartAsync(cancellationToken);
        _messageBrokerService.OnMessageReceived += compilerMessage => _ = OnMessageReceivedAsync(compilerMessage, cancellationToken);

        await Task.Delay(2000, cancellationToken);

        await _messageBrokerService.SendReadyMessageAsync(cancellationToken);
    }

    private async ValueTask OnMessageReceivedAsync(CompilerMessage compilerMessage, CancellationToken cancellationToken)
    {
        switch (compilerMessage.Type)
        {
            case MessageType.Data:
            {
                try
                {
                    CompilerData? compilerData = JsonSerializer.Deserialize<CompilerData>(compilerMessage.Data,
                        CompilerDataContext.Default.CompilerData);

                    if (compilerData == null)
                    {
                        _logger.LogError($"Received invalid compiler data for job {compilerMessage.Id}");
                        return;
                    }

                    _logger.LogDebug($"Received compile job {compilerMessage.Id} | Plugins: {compilerData.SourceFiles.Length}, References: {compilerData.ReferenceFiles.Length}");

                    CompilerMessage compilationMessage =
                        await _compilationService.GetCompilationAsync(compilerMessage.Id, compilerData,
                            cancellationToken);

                    await _messageBrokerService.SendMessageAsync(compilationMessage, cancellationToken);

                    _logger.LogInformation($"Completed compile job {compilerMessage.Id}");
                }
                catch (Exception exception)
                {
                    _logger.LogError($"Error occurred while compiling job {compilerMessage.Id}: {exception}");
                }
                break;
            }
            case MessageType.Heartbeat:
            {
                _logger.LogInformation("Received heartbeat from server");
                break;
            }
            case MessageType.Shutdown:
            {
                RequestShutdown("Server");
                break;
            }
            case MessageType.Unknown:
            {
                break;
            }
            case MessageType.Acknowledge:
            {
                break;
            }
            case MessageType.VersionInfo:
            {
                break;
            }
            case MessageType.Ready:
            {
                break;
            }
            case MessageType.Command:
            {
                break;
            }
            case MessageType.Error:
            {
                break;
            }
        }
    }

    private void RequestShutdown(string? source)
    {
        if (!string.IsNullOrEmpty(source))
        {
            StringBuilder stringBuilder = new();

            stringBuilder.AppendFormat(Constants.ShutdownMessageFormat, source);

            _logger.LogInformation(stringBuilder.ToString());
        }

        _appLifetime.StopApplication();
    }

    public async ValueTask StopAsync(CancellationToken cancellationToken)
    {
        _messageBrokerService.OnMessageReceived -= compilerMessage =>
            OnMessageReceivedAsync(compilerMessage, cancellationToken);

        _messageBrokerService.Stop();
    }
}
