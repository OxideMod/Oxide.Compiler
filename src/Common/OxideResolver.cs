using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.Extensions.Logging;
using Oxide.CompilerServices.Types.Configuration;

namespace Oxide.CompilerServices.Common;

public class OxideResolver : MetadataReferenceResolver
{
    private readonly ILogger _logger;
    private readonly AppConfiguration _appConfiguration;
    private readonly string _runtimePath;
    private readonly HashSet<PortableExecutableReference> _referenceCache;
    public override bool ResolveMissingAssemblies => true;

    public OxideResolver(ILogger<OxideResolver> logger, AppConfiguration appConfiguration)
    {
        _logger = logger;
        _appConfiguration = appConfiguration;
        _runtimePath = appConfiguration.GetCompilerConfiguration().FrameworkPath;
        _referenceCache = new HashSet<PortableExecutableReference>();
    }

    public override bool Equals(object? other) => other?.Equals(this) ?? false;

    public override int GetHashCode() => GetType().GetHashCode();

    public override ImmutableArray<PortableExecutableReference> ResolveReference(string reference, string? baseFilePath,
        MetadataReferenceProperties properties)
    {
        _logger.LogInformation("Resolving: {Reference} {BaseFilePath}", reference, baseFilePath);
        return ImmutableArray<PortableExecutableReference>.Empty;
    }

    public override PortableExecutableReference? ResolveMissingAssembly(MetadataReference metadataReference, AssemblyIdentity assemblyIdentity) =>
        Resolve(metadataReference, assemblyIdentity);

    public PortableExecutableReference? Resolve(MetadataReference metadataReference, AssemblyIdentity assemblyIdentity)
    {
        string? name = metadataReference.Display;
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        PortableExecutableReference? reference = _referenceCache.FirstOrDefault(r =>
            Path.GetFileName(r.Display) == name);

        if (reference != null)
        {
            return reference;
        }

        if (name.Equals("System.Private.CoreLib.dll"))
        {
            name = "mscorlib.dll";
        }

        string path = Path.Combine(_appConfiguration.GetDirectoryConfiguration().Libraries, name);
        FileInfo fileInfo = new(path);

        _logger.LogDebug("Attempting to resolve {0} [{1}] from {2}", name, assemblyIdentity.Version, fileInfo.FullName);

        if (fileInfo.Exists)
        {
            reference = MetadataReference.CreateFromFile(fileInfo.FullName);
            _referenceCache.Add(reference);
            return reference;
        }

        fileInfo = new FileInfo(Path.Combine(_runtimePath, name));

        if (fileInfo.Exists)
        {
            reference = MetadataReference.CreateFromFile(fileInfo.FullName);
            _referenceCache.Add(reference);
            return reference;
        }

        _logger.LogError("Unable to find required dependency {0}", name);
        return null;
    }

    public PortableExecutableReference? AddReference(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        PortableExecutableReference? reference = _referenceCache.FirstOrDefault(r =>
            Path.GetFileName(r.Display) == name);

        if (reference != null)
        {
            return reference;
        }

        if (name.Equals("System.Private.CoreLib.dll"))
        {
            name = "mscorlib.dll";
        }

        string path = Path.Combine(_appConfiguration.GetDirectoryConfiguration().Libraries, name);
        FileInfo fileInfo = new(path);

        _logger.LogDebug("Adding reference {0} from {1}", name, fileInfo.FullName);

        if (fileInfo.Exists)
        {
            reference = MetadataReference.CreateFromFile(fileInfo.FullName);
            _referenceCache.Add(reference);
            return reference;
        }

        fileInfo = new FileInfo(Path.Combine(_runtimePath, name));
        if (fileInfo.Exists)
        {
            reference = MetadataReference.CreateFromFile(fileInfo.FullName);
            _referenceCache.Add(reference);
            return reference;
        }

        _logger.LogError("Unable to find required dependency {0}", name);
        return null;
    }
}
