using System.Text.Json.Serialization;

namespace Oxide.CompilerServices.Types.Compilation;

public class CompilerFile
{
    public string Name { get; set; }
    public byte[] Data { get; set; }

    [JsonConstructor]
    public CompilerFile()
    {

    }

    public CompilerFile(string name, byte[] data)
    {
        Name = name;
        Data = data;
    }
}
