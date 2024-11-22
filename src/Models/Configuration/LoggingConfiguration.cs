using Microsoft.Extensions.Logging;
using Oxide.CompilerServices.Common;

namespace Oxide.CompilerServices.Models.Configuration;

public class LoggingConfiguration
{
    public string FileName { get; init; } = Constants.Debug ? "compiler-debug.log" : "compiler.log";

    public LogLevel Level { get; init; } = LogLevel.Debug;
}
