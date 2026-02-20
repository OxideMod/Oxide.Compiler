namespace Oxide.CompilerServices.Interfaces;

public interface IEntryPointService
{
    ValueTask StartAsync(CancellationToken cancellationToken);

    ValueTask StopAsync(CancellationToken cancellationToken);
}
