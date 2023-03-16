namespace Oxide.CompilerServices.Settings
{
    public class CompilerSettings
    {
        public bool AllowUnsafe { get; set; } = false;

        public bool UseStandardLibraries { get; set; } = false;

        public string FrameworkPath { get; set; } = AppContext.BaseDirectory;

        public bool Force { get; set; } = false;

        public bool EnableMessageStream { get; set; } = false;
    }
}
