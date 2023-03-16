using Microsoft.Extensions.Logging;

namespace Oxide.CompilerServices.Settings
{
    public class LogSettings
    {
        public string FileName { get; set; } = Path.Combine(AppContext.BaseDirectory, Program.DEBUG ? "compiler-debug.log" : "compiler.log");

        public LogLevel Level { get; set; } = LogLevel.Debug;
    }
}
