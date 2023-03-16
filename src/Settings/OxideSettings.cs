using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using System.Text;

namespace Oxide.CompilerServices.Settings
{
    public class OxideSettings
    {
        public Encoding DefaultEncoding { get; }

        public CompilerSettings Compiler { get; }

        public LogSettings Logging { get; }

        public DirectorySettings Path { get; }

        public OxideSettings(IConfigurationRoot root, IOptions<CompilerSettings> compiler, IOptions<LogSettings> logging, IOptions<DirectorySettings> dirctory)
        {
            DefaultEncoding = Encoding.GetEncoding(root.GetValue("DefaultEncoding", Encoding.UTF8.WebName) ?? Encoding.UTF8.WebName);
            Compiler = compiler.Value;
            Logging = logging.Value;
            Path = dirctory.Value;
        }
    }
}
