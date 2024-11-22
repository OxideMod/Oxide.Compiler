using System.Diagnostics;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace Oxide.CompilerServices.Models.Configuration;

public class AppConfiguration
{
    private readonly IOptionsMonitor<CompilerConfiguration> _compilerConfiguration;
    private readonly IOptionsMonitor<DirectoryConfiguration> _directoryConfiguration;
    private readonly IOptionsMonitor<LoggingConfiguration> _loggingConfiguration;

    private readonly Encoding _defaultEncoding;
    private readonly string _pipeName;
    private readonly Process? _parentProcess;

    public AppConfiguration(IConfigurationRoot configurationRoot, IOptionsMonitor<CompilerConfiguration> compilerConfiguration,
        IOptionsMonitor<DirectoryConfiguration> directoryConfiguration, IOptionsMonitor<LoggingConfiguration> loggingConfiguration)
    {
        _compilerConfiguration = compilerConfiguration;
        _directoryConfiguration = directoryConfiguration;
        _loggingConfiguration = loggingConfiguration;
        _defaultEncoding = Encoding.GetEncoding(configurationRoot.GetValue("DefaultEncoding", Encoding.UTF8.WebName));
        _pipeName = configurationRoot.GetValue("PipeName", string.Empty);

        try
        {
            int processId = configurationRoot.GetValue<int>("MainProcess");
            _parentProcess = Process.GetProcessById(processId);
        }
        catch (Exception)
        {

        }
    }

    public CompilerConfiguration GetCompilerConfiguration() => _compilerConfiguration.CurrentValue;

    public DirectoryConfiguration GetDirectoryConfiguration() => _directoryConfiguration.CurrentValue;

    public LoggingConfiguration GetLoggingConfiguration() => _loggingConfiguration.CurrentValue;

    public Encoding GetDefaultEncoding() => _defaultEncoding;

    public string GetPipeName() => _pipeName;

    public Process? GetParentProcess() => _parentProcess;
}
