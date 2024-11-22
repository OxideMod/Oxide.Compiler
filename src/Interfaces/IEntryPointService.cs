namespace Oxide.CompilerServices.Interfaces;

public interface IEntryPointService
{
    public ValueTask StartAsync(CancellationToken cancellationToken);

    public ValueTask StopAsync(CancellationToken cancellationToken);
}
