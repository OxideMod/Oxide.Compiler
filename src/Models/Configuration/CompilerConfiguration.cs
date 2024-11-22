namespace Oxide.CompilerServices.Models.Configuration;

public class CompilerConfiguration
{
    public bool AllowUnsafe { get; set; }

    public bool UseStandardLibraries { get; set; }

    public string FrameworkPath { get; set; } = System.Runtime.InteropServices.RuntimeEnvironment.GetRuntimeDirectory();

    public bool Force { get; set; }

    public bool EnableMessageStream { get; set; }
}
