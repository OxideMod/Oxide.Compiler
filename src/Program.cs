using Microsoft.Extensions.Hosting;
using Oxide.CompilerServices.Common;

namespace Oxide.CompilerServices;

public class Program
{
    public static void Main(string[] args)
    {
        HostApplicationBuilder hostApplicationBuilder = Host.CreateApplicationBuilder(args);

        hostApplicationBuilder.Services.AddServices(hostApplicationBuilder.Configuration, args);

        using IHost host = hostApplicationBuilder.Build();
        host.Run();
    }
}
