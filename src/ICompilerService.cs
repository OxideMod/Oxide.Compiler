using ObjectStream.Data;

namespace Oxide.CompilerServices
{
    public interface ICompilerService
    {
        Task Compile(int id, CompilerData data);
    }
}
