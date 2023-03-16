using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging;
using ObjectStream;
using ObjectStream.Data;
using Oxide.CompilerServices.Logging;
using Oxide.CompilerServices.Settings;
using Sentry;
using SingleFileExtractor.Core;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.Loader;

namespace Oxide.CompilerServices
{
    internal sealed class Application
    {
        public IServiceProvider Services { get; }
        public ILogger logger { get; }
        private OxideSettings settings { get; }
        private ObjectStreamClient<CompilerMessage>? objectStream { get; }
        private CancellationTokenSource tokenSource { get; }
        private Queue<CompilerMessage> compilerQueue { get; }

        public Application(IServiceProvider provider, ILogger<Application> logger, OxideSettings options, CancellationTokenSource tk)
        {
            tokenSource = tk;
            this.logger = logger;
            settings = options;
            Services = provider;
            compilerQueue = new Queue<CompilerMessage>();
            ConfigureLogging(options);

            if (options.Compiler.EnableMessageStream)
            {
                objectStream = new ObjectStreamClient<CompilerMessage>(Console.OpenStandardInput(), Console.OpenStandardOutput());
                objectStream.Message += (s, m) => OnMessage(m);
            }    
        }

        public void Start()
        {
            AppDomain.CurrentDomain.ProcessExit += (sender, e) => Exit("SIGTERM");
            Console.CancelKeyPress += (s, o) => Exit("SIGINT (Ctrl + C)");

            if (!settings.Compiler.EnableMessageStream)
            {
                logger.LogInformation(Events.Startup, "Compiler startup complete");
                LoopConsoleInput();
            }
            else
            {
                objectStream!.Start();
                logger.LogDebug(Events.Startup, "Hooked into standard input/output for interprocess communication");
                logger.LogInformation(Events.Startup, "Compiler startup complete");
                Task.Delay(TimeSpan.FromSeconds(2), tokenSource.Token).Wait();
                objectStream.PushMessage(new CompilerMessage() { Type = CompilerMessageType.Ready });
                var task = new Task(Worker, TaskCreationOptions.LongRunning);
                task.Start();
                task.Wait();
            }
        }

        private void LoopConsoleInput()
        {
            if (tokenSource.IsCancellationRequested)
            {
                logger.LogInformation(Events.Shutdown, "Shutdown requested, killing console input loop.");
                return;
            }
            string command = string.Empty;
            Console.ForegroundColor = ConsoleColor.White;
            Console.Write("Please type a command: ");
            Console.ResetColor();

            while (true)
            {
                if (tokenSource.IsCancellationRequested)
                {
                    break;
                }
                ConsoleKeyInfo key = Console.ReadKey(true);

                switch (key.Key)
                {
                    case ConsoleKey.Escape:
                        Console.Write(new string('\b', Console.CursorLeft));
                        tokenSource.Cancel();
                        break;

                    case ConsoleKey.Enter:
                        if (!string.IsNullOrWhiteSpace(command))
                        {
                            Console.Write(new string('\b', Console.CursorLeft));
                            string[] args = new string[0];
                            string value = OnCommand(command, args);
                            logger.LogInformation(Events.Command, $"Command: {command} | Result: {value}");
                            Thread.Sleep(50);
                        }
                        else
                        {
                            continue;
                        }
                        break;

                    case ConsoleKey.Backspace:
                        command = command.Substring(0, command.Length - 1);
                        Console.Write('\b');
                        continue;

                    default:
                        command += key.KeyChar;
                        Console.ForegroundColor = ConsoleColor.Yellow;
                        Console.Write(key.KeyChar);
                        Console.ResetColor();
                        continue;
                }
                break;
            }
            LoopConsoleInput();
        }

        private string OnCommand(string command, string[] args)
        {
            return "Unhandled";
        }

        private void OnMessage(CompilerMessage message)
        {
            if (tokenSource.IsCancellationRequested)
            {
                logger.LogDebug(Events.Command, "OnMessage: Cancel has been requested");
                return;
            }

            switch (message.Type)
            {
                case CompilerMessageType.Compile:
                    lock (compilerQueue)
                    {
                        message.Client = objectStream;
                        ((CompilerData)message.Data).Message = message;
                        compilerQueue.Enqueue(message);
                    }
                    break;

                case CompilerMessageType.Exit:
                    Exit("compiler stream");
                    break;
            }
        }

        private async void Worker()
        {
            CancellationToken token = tokenSource.Token;

            while (!token.IsCancellationRequested)
            {
                CompilerMessage message;
                lock (compilerQueue)
                {
                    if (compilerQueue.Count == 0)
                    {
                        continue;
                    }

                    message = compilerQueue.Dequeue();
                }

                ITransaction transaction = SentrySdk.StartTransaction("Compile", "compile", "Compilation of project");
                CompilerData data = (CompilerData)message.Data;
                data.LogTransaction = transaction;
                ICompilerService compiler = Services.GetRequiredService<ICompilerService>();
                logger.LogDebug(Events.Compile, "Starting compile job {id}", message.Id);
                await compiler.Compile(message.Id, data);
                transaction.Finish();
            }
        }

        public void Exit(string? source)
        {
            string message = "Termination request has been received";
            if (!string.IsNullOrWhiteSpace(message))
            {
                message += $" from {source}";
            }

            logger.LogInformation(Events.Shutdown, message);
            tokenSource.Cancel();
        }

        private void ConfigureLogging(OxideSettings settings)
        {
            NLog.Config.LoggingConfiguration config = NLog.LogManager.Configuration;
            NLog.Targets.FileTarget file = new();
            config.AddTarget("file", file);
            file.FileName = Path.Combine(settings.Path.Logging, settings.Logging.FileName);
            file.Layout = "(${time})[${level}] ${logger:shortName=true}[${event-properties:item=EventId}]: ${message}${onexception:inner= ${newline}${exception:format=ToString,Data}}";
            file.AutoFlush = true;
            file.CreateDirs = true;
            file.DeleteOldFileOnStartup = true;
            file.Encoding = settings.DefaultEncoding;
            NLog.LogLevel level = settings.Logging.Level switch
            {
                LogLevel.Debug => NLog.LogLevel.Debug,
                LogLevel.Warning => NLog.LogLevel.Warn,
                LogLevel.Critical or LogLevel.Error => NLog.LogLevel.Error,
                LogLevel.Trace => NLog.LogLevel.Trace,
                _ => NLog.LogLevel.Info,
            };
            NLog.Config.LoggingRule rule = new("*", level, file);
            config.LoggingRules.Add(rule);
            NLog.LogManager.Configuration = config;
            logger.LogDebug(Events.Startup, "Configured file logging for '{0}' and higher to {1}", settings.Logging.Level, file.FileName.ToString());
        }
    }
}
