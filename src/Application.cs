using System.Globalization;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ObjectStream;
using ObjectStream.Data;
using Oxide.CompilerServices.Logging;
using Oxide.CompilerServices.Settings;
using ILogger = Microsoft.Extensions.Logging.ILogger;

namespace Oxide.CompilerServices
{
    internal sealed class Application
    {
        private IServiceProvider Services { get; }
        private ILogger Logger { get; }
        private OxideSettings Settings { get; }
        private ObjectStreamClient<CompilerMessage>? ObjectStream { get; }
        private CancellationTokenSource TokenSource { get; }
        private Queue<CompilerMessage> CompilerQueue { get; }

        private Task WorkerTask { get; set; }

        public Application(IServiceProvider provider, ILogger<Application> logger, OxideSettings options, CancellationTokenSource tk)
        {
            Program.ApplicationLogLevel.MinimumLevel = options.Logging.Level.ToSerilog();
            TokenSource = tk;
            this.Logger = logger;
            Settings = options;
            Services = provider;
            CompilerQueue = new Queue<CompilerMessage>();

            if (!options.Compiler.EnableMessageStream) return;

            ObjectStream = new ObjectStreamClient<CompilerMessage>(Console.OpenStandardInput(), Console.OpenStandardOutput());
            ObjectStream.Message += (s, m) => OnMessage(m);
        }

        public void Start()
        {
            Logger.LogInformation(Events.Startup, $"Starting compiler v{Assembly.GetExecutingAssembly().GetName().Version}. . .");
            Logger.LogInformation(Events.Startup, $"Minimal logging level is set to {Program.ApplicationLogLevel.MinimumLevel}");
            Thread.CurrentThread.CurrentCulture = CultureInfo.InvariantCulture;
            Thread.CurrentThread.Priority = ThreadPriority.BelowNormal;
            Thread.CurrentThread.IsBackground = true;
            AppDomain.CurrentDomain.ProcessExit += (sender, e) => Exit("SIGTERM");
            Console.CancelKeyPress += (s, o) => Exit("SIGINT (Ctrl + C)");

            if (Settings.ParentProcess != null)
            {
                try
                {
                    if (!Settings.ParentProcess.HasExited)
                    {
                        Settings.ParentProcess.EnableRaisingEvents = true;
                        Settings.ParentProcess.Exited += (s, o) => Exit("parent process shutdown");
                        Logger.LogInformation(Events.Startup, "Watching parent process ([{id}] {name}) for shutdown", Settings.ParentProcess.Id, Settings.ParentProcess.ProcessName);
                    }
                    else
                    {
                        Exit("parent process exited");
                        return;
                    }
                }
                catch (Exception ex)
                {
                    Logger.LogWarning(Events.Startup, ex, "Failed to attach to parent process, compiler may stay open if parent is improperly shutdown");
                }
            }

            if (!Settings.Compiler.EnableMessageStream) return;

            Logger.LogDebug(Events.Startup, "Started message server. . .");
            ObjectStream!.Start();
            Logger.LogInformation(Events.Startup, "Message server has started");
            Task.Delay(TimeSpan.FromSeconds(2), TokenSource.Token).Wait();
            ObjectStream.PushMessage(new CompilerMessage() { Type = CompilerMessageType.Ready });
            Logger.LogInformation(Events.Startup, "Sent ready message to parent process");

            Task<Task> task = Task.Factory.StartNew(
                function: Worker,
                cancellationToken: TokenSource.Token,
                creationOptions: TaskCreationOptions.LongRunning,
                scheduler: TaskScheduler.Default
            );

            WorkerTask = task.Unwrap();
            Logger.LogDebug(Events.Startup, "Compiler has started successfully and is awaiting jobs. . .");
            WorkerTask.Wait();
        }

        private void OnMessage(CompilerMessage message)
        {
            if (TokenSource.IsCancellationRequested)
            {
                return;
            }

            switch (message.Type)
            {
                case CompilerMessageType.Compile:
                    lock (CompilerQueue)
                    {
                        message.Client = ObjectStream;
                        CompilerData data = (CompilerData)message.Data;
                        data.Message = message;
                        Logger.LogDebug(Events.Compile, $"Received compile job {message.Id} | Plugins: {data.SourceFiles.Length}, References: {data.ReferenceFiles.Length}");
                        CompilerQueue.Enqueue(message);
                    }
                    break;

                case CompilerMessageType.Exit:
                    Exit("compiler stream");
                    break;
            }
        }

        private async Task Worker()
        {
            CompilerMessage message = null;
            while (!TokenSource.IsCancellationRequested)
            {

                lock (CompilerQueue)
                {
                    if (CompilerQueue.Count != 0)
                    {
                        message = CompilerQueue.Dequeue();
                    }
                    else
                    {
                        message = null;
                    }
                }

                if (message == null)
                {
                    await Task.Delay(1000);
                    continue;
                }

                CompilerData data = (CompilerData)message.Data;
                ICompilerService compiler = Services.GetRequiredService<ICompilerService>();
                await compiler.Compile(message.Id, data);
            }
        }

        private void Exit(string? source)
        {
            string message = "Termination request has been received";
            if (!string.IsNullOrWhiteSpace(message))
            {
                message += $" from {source}";
            }

            Logger.LogInformation(Events.Shutdown, message);
            TokenSource.Cancel();
        }
    }
}
