using Microsoft.CodeAnalysis.Emit;
using Serilog.Core;

namespace Oxide.CompilerServices.Common;

public static class Constants
{
    public static readonly EmitOptions CompilationEmitOptions = new(debugInformationFormat: DebugInformationFormat.PortablePdb);

    public static readonly Dictionary<string, string> SwitchMappings = new(StringComparer.InvariantCultureIgnoreCase)
    {
        ["-l:file"] = "Logging:FileName",
        ["-v"] = "Logging:Level",
        ["--logging"] = "Logging",
        ["--verbose"] = "Logging:Level",

        ["--setting"] = "Compiler",
        ["-unsafe"] = "Compiler:AllowUnsafe",
        ["-std"] = "Compiler:UseStandardLibraries",
        ["-ms"] = "Compiler:EnableMessageStream",

        ["--path"] = "Path",
        ["--parent"] = "MainProcess",
        ["--pipe"] = "PipeName",
        ["--mode"] = "Mode",
        ["--source"] = "Source",
    };

    public static readonly string RootPath = AppContext.BaseDirectory;

#if DEBUG
    public static readonly LoggingLevelSwitch ApplicationLogLevel = new(LogEventLevel.Debug);
    public const bool Debug = true;
#else
    public static readonly LoggingLevelSwitch ApplicationLogLevel = new();
    public const bool Debug = false;
#endif

    public const string ShutdownMessageFormat = "Received shutdown signal from {0}";
}
