using Microsoft.CodeAnalysis;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Oxide.CompilerServices.CSharp;
using Oxide.CompilerServices.Settings;
using Serilog;
using Serilog.Core;
using Serilog.Events;

namespace Oxide.CompilerServices
{
    internal class Program
    {
#if DEBUG
        public static LoggingLevelSwitch ApplicationLogLevel { get; } = new(LogEventLevel.Debug);
        public const bool DEBUG = true;
#else
        public static LoggingLevelSwitch ApplicationLogLevel { get; } = new(LogEventLevel.Information);
        public const bool DEBUG = false;
#endif

        public static void Main(string[] args) => new ApplicationBuilder()
            .WithConfiguration((config) => config.AddCommandLine(args, new Dictionary<string, string>(StringComparer.InvariantCultureIgnoreCase)
            {
                ["-l:file"]     = "Logging:FileName",
                ["-v"]          = "Logging:Level",
                ["--logging"]   = "Logging",
                ["--verbose"]   = "Logging:Level",

                ["-unsafe"]     = "Compiler:AllowUnsafe",
                ["-std"]        = "Compiler:UseStandardLibraries",
                ["-ms"]         = "Compiler:EnableMessageStream",
                ["--setting"]   = "Compiler",

                ["--path"]      = "Path",
                ["--parent"]    = "MainProcess"
            })
            .AddJsonFile(Path.Combine(AppContext.BaseDirectory, "oxide.compiler.json"), true)
            .AddEnvironmentVariables("Oxide:"), (c, s) =>
            {
                s.Configure<LogSettings>(c.GetSection("Logging"))
                .AddScoped(cfg => cfg.GetRequiredService<IOptions<LogSettings>>().Value);
                s.Configure<CompilerSettings>(c.GetSection("Compiler"))
                .AddScoped(cfg => cfg.GetRequiredService<IOptions<CompilerSettings>>().Value);
                s.Configure<DirectorySettings>(c.GetSection("Path"))
                .AddScoped(cfg => cfg.GetRequiredService<IOptions<DirectorySettings>>().Value);

                s.AddSingleton<OxideSettings>();
            })
            .WithLogging((logging, cfg) =>
            {
                IConfigurationSection logSettings = cfg.GetSection("Logging");
                Log.Logger = new LoggerConfiguration()
                    .MinimumLevel.ControlledBy(ApplicationLogLevel)
                    .Enrich.FromLogContext()
                    .WriteTo.File(logSettings.GetValue<string>("FileName", "compiler.log"),
                        rollOnFileSizeLimit: true,
                        fileSizeLimitBytes: (long)5e+6,
                        retainedFileCountLimit: 5,
                        shared: true)
                    .CreateLogger();
                logging.AddSerilog(null, true);
#if DEBUG
                logging.AddDebug();
#endif
                if (!cfg.GetSection("Compiler").GetValue("EnableMessageStream", false))
                {
                    logging.AddSimpleConsole();
                }
            })
            .WithServices(s => s.AddSingleton<CancellationTokenSource>()
            .AddSingleton<ICompilerService, CSharpLanguage>()
            .AddTransient<MetadataReferenceResolver, OxideResolver>())
            .Build()
            .Start();
    }
}
