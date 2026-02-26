using System.Collections.Immutable;
using System.Diagnostics;
using System.Globalization;
using System.Runtime;
using System.Text;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;
using Microsoft.Extensions.Logging;
using Oxide.CompilerServices.Common;
using Oxide.CompilerServices.Interfaces;
using Oxide.CompilerServices.Serialization;
using Oxide.CompilerServices.Types.Compilation;
using Oxide.CompilerServices.Types.Configuration;
using Serilog.Events;

namespace Oxide.CompilerServices.Services;

public class CompilationService : ICompilationService
{
    private readonly ILogger _logger;
    private readonly AppConfiguration _appConfiguration;
    private readonly MessageBrokerService _messageBrokerService;
    private readonly OxideResolver _oxideResolver;

    private readonly ImmutableArray<string> _ignoredCodes =
    [
        "CS1701"
    ];

    public CompilationService(ILogger<CompilationService> logger, AppConfiguration appConfiguration,
        MessageBrokerService messageBrokerService, MetadataReferenceResolver metadataReferenceResolver)
    {
        _logger = logger;
        _appConfiguration = appConfiguration;
        _messageBrokerService = messageBrokerService;
        _oxideResolver = (OxideResolver)metadataReferenceResolver;
    }

    public async ValueTask<CompilerMessage> GetCompilationAsync(int id, CompilerData compilerData, CancellationToken cancellationToken)
    {
        Stopwatch stopwatch = Stopwatch.StartNew();
        _logger.LogInformation($"Starting compilation of job id {id} | Total Plugins: {compilerData.SourceFiles.Length}");
        string details =
            $"Settings[Encoding: {compilerData.Encoding}, CSVersion: {compilerData.GetLanguageVersion()}, Target: {compilerData.OutputKind()}, Platform: {compilerData.Platform()}, StdLib: {compilerData.StdLib}, Debug: {compilerData.Debug}, Preprocessor: {string.Join(", ", compilerData.Preprocessor)}]";

        if (Constants.ApplicationLogLevel.MinimumLevel <= LogEventLevel.Debug)
        {
            if (compilerData.ReferenceFiles.Length > 0)
            {
                details += Environment.NewLine + $"Reference Files:" + Environment.NewLine;
                for (int i = 0; i < compilerData.ReferenceFiles.Length; i++)
                {
                    CompilerFile reference = compilerData.ReferenceFiles[i];
                    if (i > 0)
                    {
                        details += Environment.NewLine;
                    }

                    details += $"  - [{i + 1}] {Path.GetFileName(reference.Name)}({reference.Data.Length})";
                }
            }

            if (compilerData.SourceFiles.Length > 0)
            {
                details += Environment.NewLine + $"Plugin Files:" + Environment.NewLine;

                for (int i = 0; i < compilerData.SourceFiles.Length; i++)
                {
                    CompilerFile plugin = compilerData.SourceFiles[i];
                    if (i > 0)
                    {
                        details += Environment.NewLine;
                    }

                    details += $"  - [{i + 1}] {Path.GetFileName(plugin.Name)}({plugin.Data.Length})";
                }
            }
        }

        _logger.LogDebug(details);

        try
        {
            CompilerMessage compilerMessage = new()
            {
                Id = id,
                Type = MessageType.Data
            };

            CompilationResult compilationResult = new();
            CompilerMessage message = Compile(compilerData, compilerMessage, compilationResult, cancellationToken);

            if (compilationResult.Data.Length > 0)
            {
                _logger.LogInformation($"Successfully compiled {compilationResult.Success}/{compilerData.SourceFiles.Length} plugins for job {id} in {stopwatch.ElapsedMilliseconds}ms");
            }
            else
            {
                _logger.LogError($"Failed to compile job {id} in {stopwatch.ElapsedMilliseconds}ms");
            }

            _logger.LogDebug($"Pushing job {id} back to parent");

            return message;
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, $"Error while compiling job {id}");
            await _messageBrokerService.SendMessageAsync(new CompilerMessage
            {
                Id = id,
                Type = MessageType.Error,
                Errors = new List<CompilerError>
                {
                    new()
                    {
                        Message = $"An error occurred while compiling: {exception}"
                    }
                }
            }, cancellationToken);

            throw;
        }
    }

    private CompilerMessage Compile(CompilerData compilerData, CompilerMessage compilerMessage,
        CompilationResult compilationResult, CancellationToken cancellationToken)
    {
        try
        {
            if (compilerData == null)
            {
                throw new ArgumentNullException(nameof(compilerData), "Missing compile data");
            }

            if (compilerData.SourceFiles == null || compilerData.SourceFiles.Length == 0)
            {
                throw new ArgumentException("No source files provided", nameof(compilerData.SourceFiles));
            }

            HashSet<MetadataReference> references = new();
            if (compilerData.StdLib)
            {
                references.Add(_oxideResolver.GetOrAddReference("System.Private.CoreLib.dll")!);
                references.Add(_oxideResolver.GetOrAddReference("netstandard.dll")!);
                references.Add(_oxideResolver.GetOrAddReference("System.Runtime.dll")!);
                references.Add(_oxideResolver.GetOrAddReference("System.Collections.dll")!);
                references.Add(_oxideResolver.GetOrAddReference("System.Collections.Immutable.dll")!);
                references.Add(_oxideResolver.GetOrAddReference("System.Linq.dll")!);
                references.Add(_oxideResolver.GetOrAddReference("System.Data.Common.dll")!);
            }

            if (compilerData.ReferenceFiles is { Length: > 0 })
            {
                foreach (CompilerFile referenceFile in compilerData.ReferenceFiles)
                {
                    string fileName = Path.GetFileName(referenceFile.Name);
                    switch (Path.GetExtension(referenceFile.Name))
                    {
                        case ".cs":
                        case ".exe":
                        case ".dll":
                        {
                            PortableExecutableReference? reference = _oxideResolver.GetOrAddReference(referenceFile);
                            if (reference == null)
                            {
                                continue;
                            }

                            references.Add(reference);
                            continue;
                        }
                        default:
                        {
                            _logger.LogWarning("Ignoring unhandled project reference: {0}", fileName);
                            continue;
                        }
                    }
                }

                _logger.LogDebug("Added {0} project references", references.Count);
            }

            Dictionary<CompilerFile, SyntaxTree> syntaxTrees = new();
            Encoding encoding = Encoding.GetEncoding(compilerData.Encoding);

            CSharpParseOptions parseOptions = new(compilerData.GetLanguageVersion(),
                preprocessorSymbols: compilerData.Preprocessor);

            foreach (CompilerFile compilerFile in compilerData.SourceFiles)
            {
                string fileName = Path.GetFileName(compilerFile.Name);
                bool isUnicode = false;

                string sourceString = RegexExtensions.UnicodeEscapePattern.Replace(
                    encoding.GetString(compilerFile.Data), match =>
                    {
                        isUnicode = true;
                        return ((char)int.Parse(match.Value.AsSpan()[2..], NumberStyles.HexNumber)).ToString();
                    });

                if (isUnicode)
                {
                    _logger.LogDebug($"Plugin {fileName} is using unicode escape sequence");
                }

                SyntaxTree syntaxTree = CSharpSyntaxTree.ParseText(sourceString, parseOptions,
                    Path.GetFullPath(compilerFile.Name), encoding, cancellationToken);

                syntaxTrees.Add(compilerFile, syntaxTree);
            }

            _logger.LogDebug("Added {0} plugins to the project", syntaxTrees.Count);

            CSharpCompilationOptions compilationOptions = new CSharpCompilationOptions(compilerData.OutputKind(),
                    metadataReferenceResolver: _oxideResolver, platform: compilerData.Platform(), allowUnsafe: true,
                    deterministic: true, optimizationLevel: OptimizationLevel.Debug)
                .WithAssemblyIdentityComparer(DesktopAssemblyIdentityComparer.Default);

            string assemblyName = Path.GetRandomFileName();
            CSharpCompilation compilation = CSharpCompilation.Create(assemblyName, syntaxTrees.Values, references,
                compilationOptions);

            compilationResult.Name = compilation.AssemblyName;

            CompileProject(compilation, compilerData, compilerMessage, compilationResult, cancellationToken);

            compilerMessage.Data = JsonSerializer.SerializeToUtf8Bytes(compilationResult,
                CompilationResultContext.Default.CompilationResult);

            return compilerMessage;
        }
        catch (Exception exception)
        {
            _logger.LogError("Error while compiling: {0}", exception);
            throw;
        }
        finally
        {
            _oxideResolver.Cleanup();

            GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Aggressive, true, true);
        }
    }

    private void CompileProject(CSharpCompilation compilation, CompilerData compilerData, CompilerMessage compilerMessage,
        CompilationResult compilationResult, CancellationToken cancellationToken)
    {
        using MemoryStream peStream = new();
        using MemoryStream pdbStream = new();

        EmitResult result = compilation.Emit(peStream, pdbStream, options: Constants.CompilationEmitOptions,
            cancellationToken: cancellationToken);

        if (result.Success)
        {
            compilationResult.Data = peStream.ToArray();
            compilationResult.Symbols = pdbStream.ToArray();
            compilationResult.Success = compilation.SyntaxTrees.Length;
            return;
        }

        bool modified = false;

        foreach (Diagnostic diagnostic in result.Diagnostics)
        {
            if (_ignoredCodes.Contains(diagnostic.Id))
            {
                continue;
            }

            string diagnosticMessage = diagnostic.GetMessage();

            _logger.LogDebug("[{0}] [{1}] Diagnostic - {2}", diagnostic.Id, diagnostic.Severity, diagnosticMessage);

            if (diagnostic.Location.SourceTree != null)
            {
                SyntaxTree syntaxTree = diagnostic.Location.SourceTree;
                string fileName = Path.GetFileNameWithoutExtension(syntaxTree.FilePath) ?? "UnknownFile";
                FileLinePositionSpan lineSpan = diagnostic.Location.GetLineSpan();
                int line = lineSpan.StartLinePosition.Line + 1;
                int position = lineSpan.StartLinePosition.Character + 1;

                switch (diagnostic.Severity)
                {
                    case DiagnosticSeverity.Warning:
                    {

                        break;
                    }
                    case DiagnosticSeverity.Error:
                    {
                        compilerMessage.Errors ??= new List<CompilerError>();
                        compilerMessage.Errors.Add(new CompilerError
                        {
                            Message = $"[{compilerMessage.Errors.Count + 1}] {diagnosticMessage} at {line}:{position}",
                            File = fileName,
                            Line = line,
                            Position = position
                        });

                        break;
                    }
                }

                if (compilation.SyntaxTrees.Contains(syntaxTree) && diagnostic.Severity == DiagnosticSeverity.Error)
                {
                    compilation = compilation.RemoveSyntaxTrees(syntaxTree);

                    _logger.LogError("Failed to compile {0} - {1} (L: {2} | P: {3}) | Removing from project",
                        fileName, diagnosticMessage, line, position);

                    modified = true;
                    compilationResult.Failed++;
                }
            }
            else
            {
                compilerMessage.Errors ??= new List<CompilerError>();
                compilerMessage.Errors.Add(new CompilerError
                {
                    Message = diagnosticMessage,
                });

                _logger.LogError($"[{diagnostic.Id}] {diagnosticMessage}");
            }
        }

        if (modified && compilation.SyntaxTrees.Length > 0)
        {
            CompileProject(compilation, compilerData, compilerMessage, compilationResult, cancellationToken);
        }
    }
}
