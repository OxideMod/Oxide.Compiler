using Microsoft.CodeAnalysis.Emit;
using Microsoft.Extensions.Logging;
using Serilog.Core;
using Serilog.Events;

namespace Oxide.CompilerServices.Common;

public static class Constants
{
    public static readonly EventId StartupEventId = new(1, "Startup");
    public static readonly EventId ShutdownEventId = new(2, "Shutdown");

    public static readonly EventId CommandEventId = new(3, "Command");
    public static readonly EventId CompileEventId = new(4, "Compile");

    public static readonly EmitOptions PdbEmitOptions = new(debugInformationFormat: DebugInformationFormat.Embedded);

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
