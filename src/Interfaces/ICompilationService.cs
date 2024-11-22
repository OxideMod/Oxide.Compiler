using Oxide.CompilerServices.Models.Compiler;

namespace Oxide.CompilerServices.Interfaces;

public interface ICompilationService
{
    public ValueTask<CompilerMessage> GetCompilationAsync(int id, CompilerData compilerData, CancellationToken cancellationToken);
}
