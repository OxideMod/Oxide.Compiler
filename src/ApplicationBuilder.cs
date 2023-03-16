using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Oxide.CompilerServices
{
    internal class ApplicationBuilder
    {
        private readonly IServiceCollection services;
        private IConfigurationBuilder? configuration;
        private IConfigurationRoot? configRoot;

        public ApplicationBuilder()
        {
            services = new ServiceCollection();
        }

        public ApplicationBuilder WithConfiguration(Action<IConfigurationBuilder> configure, Action<IConfiguration, IServiceCollection>? addconfigs = null)
        {
            services.AddOptions();
            configuration = new ConfigurationBuilder();
            configure(configuration);
            configRoot = configuration.Build();
            addconfigs?.Invoke(configRoot, services);
            return this;
        }

        public ApplicationBuilder WithLogging(Action<ILoggingBuilder, IConfiguration?> configure)
        {
            services.AddLogging(s => configure(s, configRoot));
            return this;
        }

        public ApplicationBuilder WithServices(Action<IServiceCollection> services)
        {
            services?.Invoke(this.services);
            return this;
        }

        public Application Build()
        {
            if (configRoot != null)
            {
                services.AddSingleton(configRoot);
            }

            services.AddSingleton<Application>();
            return services.BuildServiceProvider().GetRequiredService<Application>();
        }
    }
}
