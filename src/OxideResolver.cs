using Microsoft.CodeAnalysis;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Oxide.CompilerServices.Logging;
using Oxide.CompilerServices.Settings;
using SingleFileExtractor.Core;
using System.Collections.Immutable;
using System.Diagnostics;

namespace Oxide.CompilerServices
{
    internal class OxideResolver : MetadataReferenceResolver, IDisposable
    {
        private readonly ILogger logger;
        private readonly ExecutableReader reader;
        private readonly DirectorySettings directories;
        private readonly string runtimePath;

        private readonly HashSet<PortableExecutableReference> referenceCache;

        public OxideResolver(ILogger<OxideResolver> logger, IOptions<DirectorySettings> settings)
        {
            this.logger = logger;
            directories = settings.Value;
            runtimePath = System.Runtime.InteropServices.RuntimeEnvironment.GetRuntimeDirectory();
            reader = new ExecutableReader(Process.GetCurrentProcess().MainModule!.FileName);
            referenceCache = new HashSet<PortableExecutableReference>();
        }

        public override bool Equals(object? other) => other?.Equals(this) ?? false;

        public override int GetHashCode() => logger.GetHashCode();

        public override ImmutableArray<PortableExecutableReference> ResolveReference(string reference, string? baseFilePath, MetadataReferenceProperties properties)
        {
            logger.LogInformation("Resolve: {Reference} {BaseFilePath}", reference, baseFilePath);
            return ImmutableArray<PortableExecutableReference>.Empty;
        }

        public override bool ResolveMissingAssemblies => true;

        public override PortableExecutableReference? ResolveMissingAssembly(MetadataReference definition, AssemblyIdentity referenceIdentity) => Reference(definition.Display!);

        public PortableExecutableReference? Reference(string? name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return null;
            }

            PortableExecutableReference? reference = referenceCache.FirstOrDefault(r => Path.GetFileName(r.Display) == name);

            if (reference != null)
            {
                return reference;
            }

            FileInfo fileSystem = new FileInfo(Path.Combine(directories.Libraries, name));

            if (fileSystem.Exists)
            {
                logger.LogDebug(Events.Compile, "Found from libraries directory: [Size: {Size}] {Name}", fileSystem.Length, fileSystem.Name);
                reference = MetadataReference.CreateFromFile(fileSystem.FullName);
                referenceCache.Add(reference);
                return reference;
            }

            fileSystem = new(Path.Combine(runtimePath, name));

            if (fileSystem.Exists)
            {
                logger.LogDebug(Events.Compile, "Found from runtime directory: [Size: {Size}] {Name}", fileSystem.Length, fileSystem.Name);
                reference = MetadataReference.CreateFromFile(fileSystem.FullName);
                referenceCache.Add(reference);
                return reference;
            }

            FileEntry? entry = reader.Bundle.Files.FirstOrDefault(f => f.RelativePath.Equals(name));

            if (entry != null)
            {
                logger.LogDebug(Events.Compile, "Found from embedded resource: [Size: {Size}] {Name}", entry.Size, entry.RelativePath);
                reference = MetadataReference.CreateFromStream(entry.AsStream());
                referenceCache.Add(reference);
                return reference;
            }

            logger.LogDebug(Events.Compile, "Missing assembly definition {name}", name);
            return null;
        }

        public void Dispose()
        {
            reader.Dispose();
        }

        public static PortableExecutableReference? ResolveFromManifest(string name)
        {
            using var reader = new ExecutableReader(Process.GetCurrentProcess().MainModule!.FileName);
            FileEntry? entry = reader.Bundle.Files.FirstOrDefault(f => f.RelativePath.Equals(name));

            if (entry != null)
            {
                return MetadataReference.CreateFromStream(entry.AsStream(), filePath: entry.RelativePath);
            }

            return null;
        }
    }
}
