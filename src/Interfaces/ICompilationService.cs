using Oxide.CompilerServices.Types.Compilation;

namespace Oxide.CompilerServices.Interfaces;

public interface ICompilationService
{
    ValueTask<CompilerMessage> GetCompilationAsync(int id, CompilerData compilerData, CancellationToken cancellationToken);
}
