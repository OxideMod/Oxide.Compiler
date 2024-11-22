using Oxide.CompilerServices.Enums;

namespace Oxide.CompilerServices.Models.Compiler;

[Serializable]
public sealed class CompilerMessage
{
    public int Id { get; set; }
    public MessageType Type { get; set; }
    public byte[] Data { get; set; }
    public object ExtraData { get; set; }
}
