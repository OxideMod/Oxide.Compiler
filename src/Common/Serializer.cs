using System.Text;
using Newtonsoft.Json;
using Oxide.CompilerServices.Interfaces;

namespace Oxide.CompilerServices.Common;

public class Serializer : ISerializer
{
    private readonly JsonSerializer _jsonSerializer;

    public Serializer()
    {
        _jsonSerializer = new JsonSerializer();
    }

    public byte[] Serialize<T>(T type) where T : class
    {
        using MemoryStream memoryStream = new();
        using StreamWriter streamWriter = new(memoryStream, Encoding.UTF8);

        _jsonSerializer.Serialize(streamWriter, type);
        streamWriter.Flush();

        return memoryStream.ToArray();
    }

    public T Deserialize<T>(byte[] data) where T : class
    {
        using MemoryStream memoryStream = new(data);
        using StreamReader streamReader = new(memoryStream, Encoding.UTF8);

        return (T)_jsonSerializer.Deserialize(streamReader, typeof(T));
    }
}
