using Microsoft.Extensions.Logging;

namespace Oxide.CompilerServices.Logging
{
    public static class Events
    {
        public readonly static EventId Startup = new(1, "Startup");
        public readonly static EventId Shutdown = new(2, "Shutdown");

        public readonly static EventId Command = new(3, "Command");
        public readonly static EventId Compile = new(4, "Compile");

        
    }
}
