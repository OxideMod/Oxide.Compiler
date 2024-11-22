namespace Oxide.CompilerServices.Interfaces;

public interface ISerializer
{
    public byte[] Serialize<T>(T type) where T : class;

    public T Deserialize<T>(byte[] data) where T : class;
}
