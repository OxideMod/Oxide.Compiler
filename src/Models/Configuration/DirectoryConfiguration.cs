namespace Oxide.CompilerServices.Models.Configuration;

public class DirectoryConfiguration
{
    public string Root { get; set; }

    public string Logging { get; set; }

    public string Plugins { get; set; }

    public string Configuration { get; set; }

    public string Data { get; set; }

    public string Libraries { get; set; }

    public DirectoryConfiguration()
    {
        Root = Environment.CurrentDirectory;
        Logging = Root;
        Plugins = Path.Combine(Root, "plugins");
        Configuration = Root;
        Data = Root;
        Libraries = AppContext.BaseDirectory;
    }
}
