using Microsoft.CodeAnalysis;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Oxide.CompilerServices.Interfaces;
using Oxide.CompilerServices.Services;
using Oxide.CompilerServices.Types.Configuration;
using Serilog;

namespace Oxide.CompilerServices.Common;

public static class DependencyInjection
{
    public static void AddServices(this IServiceCollection services, ConfigurationManager configuration, string[] args)
    {
        services.Configure<HostOptions>(service =>
        {
            service.ServicesStartConcurrently = true;
            service.ServicesStopConcurrently = true;
        });

        configuration.AddCommandLine(args, Constants.SwitchMappings);
        configuration.AddJsonFile(Path.Combine(Constants.RootPath, "oxide.compiler.json"), true);
        configuration.AddEnvironmentVariables("Oxide_");

        string mode = configuration.GetValue<string>("Mode", "release");
        if (mode == "test")
        {
            string? sourcePath = configuration.GetValue<string>("Source");
            //return CompileTestFilesAsync(sourcePath, outputPath, application.ServiceProvider);
        }

        services.Configure<CompilerConfiguration>(configuration.GetSection("Compiler"));
        services.Configure<DirectoryConfiguration>(configuration.GetSection("Path"));
        services.Configure<LoggingConfiguration>(configuration.GetSection("Logging"));

        services.AddLogging(loggingBuilder =>
        {
            IConfigurationSection logSettings = configuration.GetSection("Logging");
            string filePath = logSettings.GetValue("FileName", "oxide.compiler.log");

            if (filePath.Equals("oxide.compiler.log"))
            {
                IConfigurationSection pathSettings = configuration.GetSection("Path");
                string startDirectory = pathSettings.GetValue("Logging", Environment.CurrentDirectory);
                filePath = Path.Combine(startDirectory, filePath);
            }

            Log.Logger = new LoggerConfiguration().MinimumLevel.ControlledBy(Constants.ApplicationLogLevel)
                .Enrich.FromLogContext().WriteTo.File(filePath, rollOnFileSizeLimit: true, fileSizeLimitBytes: (long)5e+6,
                    retainedFileCountLimit: 5, shared: true).CreateLogger();

            loggingBuilder.AddSerilog(null, true);
#if DEBUG
            loggingBuilder.AddDebug();
#endif
            if (!configuration.GetSection("Compiler").GetValue("EnableMessageStream", false))
            {
                loggingBuilder.AddSimpleConsole();
            }
        });


        services.AddSingleton<IConfigurationRoot>(configuration);
        services.AddSingleton<AppConfiguration>();
        services.AddSingleton<ICompilationService, CompilationService>();
        services.AddTransient<MetadataReferenceResolver, OxideResolver>();
        services.AddSingleton<MessageBrokerService>();
        services.AddSingleton<IEntryPointService, EntryPointService>();
        services.AddHostedService<AppHostService>();
    }
}
