using Microsoft.CodeAnalysis;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Oxide.CompilerServices.Interfaces;
using Oxide.CompilerServices.Models.Configuration;
using Oxide.CompilerServices.Services;
using Serilog;

namespace Oxide.CompilerServices.Common;

public static class DependencyInjection
{
    public static void AddServices(this IServiceCollection services, IConfiguration configuration, string[] args)
    {
        services.Configure<HostOptions>(service =>
        {
            service.ServicesStartConcurrently = true;
            service.ServicesStopConcurrently = true;
        });

        ConfigurationBuilder configurationBuilder = new();

        configurationBuilder.AddCommandLine(args, Constants.SwitchMappings);
        configurationBuilder.AddJsonFile(Path.Combine(Constants.RootPath, "oxide.compiler.json"), true);
        configurationBuilder.AddEnvironmentVariables("Oxide_");

        IConfigurationRoot configurationRoot = configurationBuilder.Build();

        string mode = configurationRoot.GetValue<string>("Mode", "release");
        if (mode == "test")
        {
            string? sourcePath = configurationRoot.GetValue<string>("Source");
            //return CompileTestFilesAsync(sourcePath, outputPath, application.ServiceProvider);
        }

        services.Configure<CompilerConfiguration>(configurationRoot.GetSection("Compiler"));
        services.Configure<DirectoryConfiguration>(configurationRoot.GetSection("Path"));
        services.Configure<LoggingConfiguration>(configurationRoot.GetSection("Logging"));


        services.AddLogging(loggingBuilder =>
        {
            IConfigurationSection logSettings = configurationRoot.GetSection("Logging");
            string filePath = logSettings.GetValue("FileName", "oxide.compiler.log");

            if (filePath.Equals("oxide.compiler.log"))
            {
                IConfigurationSection pathSettings = configurationRoot.GetSection("Path");
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
            if (!configurationRoot.GetSection("Compiler").GetValue("EnableMessageStream", false))
            {
                loggingBuilder.AddSimpleConsole();
            }
        });


        services.AddSingleton(configurationRoot);
        services.AddSingleton<AppConfiguration>();
        services.AddSingleton<ICompilationService, CompilationService>();
        services.AddTransient<MetadataReferenceResolver, OxideResolver>();
        services.AddSingleton<MessageBrokerService>();
        services.AddSingleton<ISerializer, Serializer>();
        services.AddSingleton<IEntryPointService, EntryPointService>();
        services.AddHostedService<AppHostService>();
    }
}
