using Microsoft.CodeAnalysis;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NLog.Config;
using NLog.Extensions.Logging;
using Oxide.CompilerServices.CSharp;
using Oxide.CompilerServices.Settings;

namespace Oxide.CompilerServices
{
    internal class Program
    {
#if DEBUG
        public const bool DEBUG = true;
#else
        public const bool DEBUG = false;
#endif

        public static void Main(string[] args) => new ApplicationBuilder()
            .WithConfiguration((config) => config.AddCommandLine(args, new Dictionary<string, string>(StringComparer.InvariantCultureIgnoreCase)
            {
                ["-log"] = "Logging:FileName",
                ["--logfile"] = "Logging:FileName",

                ["-ll"] = "Logging:Level",
                ["--loglevel"] = "Logging:Level",


                ["-std"] = "Compiler:UseStandardLibraries",
                ["--standard-libraries"] = "Compiler:UseStandardLibraries",

                ["-u"] = "Compiler:AllowUnsafe",
                ["--unsafe"] = "Compiler:AllowUnsafe",

                ["-fp"] = "Compiler:FrameworkPath",
                ["--framewor-path"] = "Compiler:FrameworkPath",

                ["-f"] = "Compiler:Force",
                ["--force-compile"] = "Compiler:Force",

                ["-m"] = "Compiler:EnableMessageStream",
                ["--enable-messages"] = "Compiler:EnableMessageStream",


                ["-cd"] = "Path:Root",
                ["--root-path"] = "Path:Root",

                ["-cp"] = "Path:Configuration",
                ["--configuration-path"] = "Path:Configuration",

                ["-lp"] = "Path:Logging",
                ["--logging-path"] = "Path:Logging",

                ["-dp"] = "Path:Data",
                ["--data-path"] = "Path:Data",

                ["-pp"] = "Path:Plugins",
                ["--plugin-path"] = "Path:Plugins",

                ["-glibs"] = "Path:Libraries",
                ["--game-libraries"] = "Path:Libraries",


                ["-e"] = "DefaultEncoding",
                ["--encoding"] = "DefaultEncoding",
            })
            .AddJsonFile(Path.Combine(AppContext.BaseDirectory, "oxide.compiler.json"), true)
            .AddEnvironmentVariables("OXIDE:"), (c, s) =>
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
                logging.AddSentry(options =>
                {
                    options.Dsn = "https://a76ae35214e14b6784cec747a47648bc@o4504825216303104.ingest.sentry.io/4504825233801216";
                    options.Debug = false;
                    options.TracesSampleRate = 1.0;
                    options.DecompressionMethods = System.Net.DecompressionMethods.All;
                    options.DetectStartupTime = Sentry.StartupTimeDetectionMode.Best;
                    options.ReportAssembliesMode = Sentry.ReportAssembliesMode.InformationalVersion;
                });
                LoggingConfiguration config = new();
                logging.AddNLog(config, new()
                {
                    AutoShutdown = true,
                    LoggingConfigurationSectionName = "Logging"
                });

#if DEBUG
                logging.AddDebug();
#endif
                if (cfg != null && !cfg.GetSection("Compiler").GetValue("EnableMessageStream", false))
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
