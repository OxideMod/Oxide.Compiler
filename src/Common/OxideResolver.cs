using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Runtime.CompilerServices;
using Microsoft.CodeAnalysis;
using Microsoft.Extensions.Logging;
using Oxide.CompilerServices.Types.Compilation;
using Oxide.CompilerServices.Types.Configuration;

namespace Oxide.CompilerServices.Common;

public class OxideResolver : MetadataReferenceResolver
{
    private readonly ILogger _logger;
    private readonly AppConfiguration _appConfiguration;
    private readonly string _runtimePath;
    private readonly Dictionary<string, AssemblyMetadata> _references;
    private readonly HashSet<string>? _mergedAssemblyNames;
    public override bool ResolveMissingAssemblies => true;

    public OxideResolver(ILogger<OxideResolver> logger, AppConfiguration appConfiguration)
    {
        _logger = logger;
        _appConfiguration = appConfiguration;
        _runtimePath = appConfiguration.GetCompilerConfiguration().FrameworkPath;
        _references = new Dictionary<string, AssemblyMetadata>();
        _mergedAssemblyNames =
            GetMergedAssemblies(Path.Combine(appConfiguration.GetDirectoryConfiguration().Libraries, "Oxide.References.dll"));
    }

    public override bool Equals(object? other) => ReferenceEquals(this, other);

    public override int GetHashCode() => RuntimeHelpers.GetHashCode(this);

    public override ImmutableArray<PortableExecutableReference> ResolveReference(string reference, string? baseFilePath,
        MetadataReferenceProperties properties) => ImmutableArray<PortableExecutableReference>.Empty;

    public override PortableExecutableReference? ResolveMissingAssembly(MetadataReference metadataReference,
        AssemblyIdentity assemblyIdentity) => Resolve(metadataReference, assemblyIdentity);

    public PortableExecutableReference? GetOrAddReference(string name)
    {
        if (name.Equals("System.Private.CoreLib.dll"))
        {
            name = "mscorlib.dll";
        }

        PortableExecutableReference? cachedReference = GetCachedReference(name);
        if (cachedReference != null)
        {
            return cachedReference;
        }

        string libraryPath = GetLibraryPath(name);
        FileInfo fileInfo = new(libraryPath);

        _logger.LogDebug("Adding reference {0} from {1}", name, fileInfo.FullName);

        if (fileInfo.Exists)
        {
            _logger.LogDebug("Creating reference from {0}", fileInfo.FullName);
            return GetMetadataReferenceFromFile(name, fileInfo.FullName);
        }

        fileInfo = new FileInfo(Path.Combine(_runtimePath, name));
        if (fileInfo.Exists)
        {
            _logger.LogDebug("Creating reference from {0}", fileInfo.FullName);
            return GetMetadataReferenceFromFile(name, fileInfo.FullName);
        }

        _logger.LogError("Unable to find required dependency {0}", name);
        return null;
    }

    public PortableExecutableReference? GetOrAddReference(CompilerFile compilerFile)
    {
        string name = compilerFile.Name;

        _logger.LogDebug("Adding reference {0}", name);

        if (IsMergedAssembly(name))
        {
            _logger.LogDebug("Assembly {0} is a merged assembly, routing to {1}", name, "Oxide.References.dll");
            name = "Oxide.References.dll";
        }

        PortableExecutableReference? cachedReference = GetCachedReference(name);
        if (cachedReference != null)
        {
            return cachedReference;
        }

        if (ShouldLoadFromFile(compilerFile))
        {
            _logger.LogDebug("Creating reference {0} from file", name);
            return GetMetadataReferenceFromFile(name);
        }

        _logger.LogDebug("Creating reference from image {0}", name);
        return GetMetadataReferenceFromImage(compilerFile.Data, name);
    }

    private PortableExecutableReference? Resolve(MetadataReference metadataReference, AssemblyIdentity assemblyIdentity)
    {
        _logger.LogDebug("{0} requires {1}", metadataReference.Display, assemblyIdentity.GetDisplayName());

        string name = $"{assemblyIdentity.Name}.dll";
        if (name == "System.Private.CoreLib.dll")
        {
            name = "mscorlib.dll";
        }

        if (IsMergedAssembly(name))
        {
            _logger.LogDebug("Assembly {0} is a merged assembly, routing to {1}", name, "Oxide.References.dll");
            name = "Oxide.References.dll";
        }

        PortableExecutableReference? cachedReference = GetCachedReference(name);
        if (cachedReference != null)
        {
            return cachedReference;
        }

        string libraryPath = GetLibraryPath(name);
        FileInfo fileInfo = new(libraryPath);

        _logger.LogDebug("Resolving {0} from {1}", name, fileInfo.FullName);

        if (fileInfo.Exists)
        {
            _logger.LogDebug("Creating reference from {0}", fileInfo.FullName);
            return GetMetadataReferenceFromFile(name, fileInfo.FullName);
        }

        fileInfo = new FileInfo(Path.Combine(_runtimePath, name));
        if (fileInfo.Exists)
        {
            _logger.LogDebug("Creating reference from {0}", fileInfo.FullName);
            return GetMetadataReferenceFromFile(name, fileInfo.FullName);
        }

        _logger.LogError("Unable to find required dependency {0}", name);
        return null;
    }

    private HashSet<string>? GetMergedAssemblies(string path)
    {
        using AssemblyMetadata metadata = AssemblyMetadata.CreateFromFile(path);
        ImmutableArray<ModuleMetadata> metadataModules = metadata.GetModules();
        if (metadataModules.Length == 0)
        {
            return null;
        }

        ModuleMetadata metadataModule = metadataModules[0];
        MetadataReader metadataReader = metadataModule.GetMetadataReader();

        AssemblyDefinition assemblyDefinition = metadataReader.GetAssemblyDefinition();
        CustomAttributeHandleCollection customAttributes = assemblyDefinition.GetCustomAttributes();

        HashSet<string> collection = new();
        foreach (CustomAttributeHandle customAttributeHandle in customAttributes)
        {
            CustomAttribute customAttribute = metadataReader.GetCustomAttribute(customAttributeHandle);
            if (!IsAssemblyMetadataAttribute(metadataReader, customAttribute))
            {
                continue;
            }

            BlobReader blobReader = metadataReader.GetBlobReader(customAttribute.Value);

            blobReader.ReadUInt16();

            string? key = blobReader.ReadSerializedString();
            if (key != "Oxide.MergedAssembly")
            {
                continue;
            }

            string? value = blobReader.ReadSerializedString();
            if (string.IsNullOrEmpty(value))
            {
                continue;
            }

            collection.Add($"{value}.dll");
        }

        return collection;
    }

    private bool IsAssemblyMetadataAttribute(MetadataReader metadataReader, CustomAttribute customAttribute)
    {
        EntityHandle constructor = customAttribute.Constructor;
        if (constructor.Kind != HandleKind.MemberReference)
        {
            return false;
        }

        MemberReference memberReference = metadataReader.GetMemberReference((MemberReferenceHandle)constructor);
        if (memberReference.Parent.Kind != HandleKind.TypeReference)
        {
            return false;
        }

        TypeReference typeRef = metadataReader.GetTypeReference((TypeReferenceHandle)memberReference.Parent);

        return metadataReader.GetString(typeRef.Namespace) == "System.Reflection"
               && metadataReader.GetString(typeRef.Name) == "AssemblyMetadataAttribute";
    }

    private bool IsMergedAssembly(string name)
    {
        if (_mergedAssemblyNames != null)
        {
            return _mergedAssemblyNames.Contains(name);
        }

        _logger.LogError("Merged assembly names were not found");
        return false;
    }

    private PortableExecutableReference? GetCachedReference(string name)
    {
        AssemblyMetadata? reference = _references.GetValueOrDefault(name);
        if (reference == null)
        {
            return null;
        }

        _logger.LogDebug("Found cached reference for {0}", name);
        return reference.GetReference(filePath: name);
    }

    private PortableExecutableReference GetMetadataReferenceFromImage(byte[] data, string name)
    {
        AssemblyMetadata assemblyMetadata = AssemblyMetadata.CreateFromImage(data);
        _references.Add(name, assemblyMetadata);
        return assemblyMetadata.GetReference(filePath: name);
    }

    private PortableExecutableReference GetMetadataReferenceFromFile(string name, string? path = null)
    {
        AssemblyMetadata assemblyMetadata = AssemblyMetadata.CreateFromFile(path ?? name);
        _references.Add(name, assemblyMetadata);
        return assemblyMetadata.GetReference(filePath: name);
    }

    private bool ShouldLoadFromFile(CompilerFile compilerFile) =>
        File.Exists(compilerFile.Name) && (compilerFile.Data == null || compilerFile.Data.Length == 0);

    private string GetLibraryPath(string name) =>
        Path.Combine(_appConfiguration.GetDirectoryConfiguration().Libraries, name);

    public void Cleanup()
    {
        _logger.LogDebug("Cleaning up {0} references", _references.Count);
        foreach (KeyValuePair<string, AssemblyMetadata> entry in _references)
        {
            entry.Value.Dispose();
        }

        _references.Clear();
    }
}
