using System.Collections.Immutable;
using System.Diagnostics;
using System.Globalization;
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
    private readonly MetadataReferenceResolver _metadataReferenceResolver;

    private readonly ImmutableArray<string> _ignoredCodes = ImmutableArray.Create(new[]
    {
        "CS1701"
    });

    public CompilationService(ILogger<CompilationService> logger, AppConfiguration appConfiguration,
        MessageBrokerService messageBrokerService, MetadataReferenceResolver metadataReferenceResolver)
    {
        _logger = logger;
        _appConfiguration = appConfiguration;
        _messageBrokerService = messageBrokerService;
        _metadataReferenceResolver = metadataReferenceResolver;
    }

    public async ValueTask<CompilerMessage> GetCompilationAsync(int id, CompilerData compilerData, CancellationToken cancellationToken)
    {
        Stopwatch stopwatch = Stopwatch.StartNew();
        _logger.LogInformation(Constants.CompileEventId, $"Starting compilation of job id {id} | Total Plugins: {compilerData.SourceFiles.Length}");
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

        _logger.LogDebug(Constants.CompileEventId, details);

        try
        {
            CompilerMessage compilerMessage = new()
            {
                Id = id,
                Type = MessageType.Data
            };

            CompilationResult compilationResult = new();
            CompilerMessage message = await CompileAsync(compilerData, compilerMessage, compilationResult, cancellationToken);

            if (compilationResult.Data.Length > 0)
            {
                _logger.LogInformation(Constants.CompileEventId, $"Successfully compiled {compilationResult.Success}/{compilerData.SourceFiles.Length} plugins for job {id} in {stopwatch.ElapsedMilliseconds}ms");
            }
            else
            {
                _logger.LogError(Constants.CompileEventId, $"Failed to compile job {id} in {stopwatch.ElapsedMilliseconds}ms");
            }

            _logger.LogDebug(Constants.CompileEventId, $"Pushing job {id} back to parent");

            return message;
        }
        catch (Exception exception)
        {
            _logger.LogError(Constants.CompileEventId, exception, $"Error while compiling job {id} - {exception.Message}");
            await _messageBrokerService.SendMessageAsync(new CompilerMessage
            {
                Id = id,
                Type = MessageType.Error,
                ExtraData = exception
            }, cancellationToken);

            throw;
        }
    }

    private async ValueTask<CompilerMessage> CompileAsync(CompilerData compilerData, CompilerMessage compilerMessage,
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

            Dictionary<string, MetadataReference> references = new(StringComparer.OrdinalIgnoreCase);

            OxideResolver resolver = (OxideResolver)_metadataReferenceResolver;
            if (compilerData.StdLib)
            {
                references.Add("System.Private.CoreLib.dll", resolver.Reference("System.Private.CoreLib.dll")!);
                references.Add("netstandard.dll", resolver.Reference("netstandard.dll")!);
                references.Add("System.Runtime.dll", resolver.Reference("System.Runtime.dll")!);
                references.Add("System.Collections.dll", resolver.Reference("System.Collections.dll")!);
                references.Add("System.Collections.Immutable.dll", resolver.Reference("System.Collections.Immutable.dll")!);
                references.Add("System.Linq.dll", resolver.Reference("System.Linq.dll")!);
                references.Add("System.Data.Common.dll", resolver.Reference("System.Data.Common.dll")!);
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
                            references[fileName] = File.Exists(referenceFile.Name) && (referenceFile.Data == null ||
                                referenceFile.Data.Length == 0)
                                ? MetadataReference.CreateFromFile(referenceFile.Name)
                                : MetadataReference.CreateFromImage(referenceFile.Data, filePath: referenceFile.Name);
                            continue;
                        }
                        default:
                        {
                            _logger.LogWarning(Constants.CompileEventId,
                                $"Ignoring unhandled project reference: {fileName}");
                            continue;
                        }
                    }
                }

                _logger.LogDebug(Constants.CompileEventId, $"Added {references.Count} project references");
            }

            Dictionary<CompilerFile, SyntaxTree> syntaxTrees = new();
            Encoding encoding = Encoding.GetEncoding(compilerData.Encoding);

            CSharpParseOptions parseOptions = new(compilerData.GetLanguageVersion(), preprocessorSymbols: compilerData.Preprocessor);

            foreach (CompilerFile compilerFile in compilerData.SourceFiles)
            {
                string fileName = Path.GetFileName(compilerFile.Name);
                bool isUnicode = false;

                string sourceString = RegexExtensions.UnicodeEscapePattern.Replace(
                    encoding.GetString(compilerFile.Data), match =>
                    {
                        isUnicode = true;
                        return ((char)int.Parse(match.Value.Substring(2), NumberStyles.HexNumber)).ToString();
                    });

                if (isUnicode)
                {
                    _logger.LogDebug(Constants.CompileEventId,
                        $"Plugin {fileName} is using unicode escape sequence");
                }

                SyntaxTree syntaxTree = CSharpSyntaxTree.ParseText(sourceString, parseOptions,
                    Path.GetFullPath(compilerFile.Name), encoding, cancellationToken);

                syntaxTrees.Add(compilerFile, syntaxTree);
            }

            _logger.LogDebug(Constants.CompileEventId, $"Added {syntaxTrees.Count} plugins to the project");

            CSharpCompilationOptions compilationOptions = new(compilerData.OutputKind(), metadataReferenceResolver: resolver,
                platform: compilerData.Platform(),
                allowUnsafe: true,
                deterministic: true,
                optimizationLevel: OptimizationLevel.Debug);

            string assemblyName = Path.GetRandomFileName();
            CSharpCompilation compilation = CSharpCompilation.Create(assemblyName, syntaxTrees.Values,
                references.Values, compilationOptions);

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
    }

    private void CompileProject(CSharpCompilation compilation, CompilerData compilerData, CompilerMessage compilerMessage,
        CompilationResult compilationResult, CancellationToken cancellationToken)
    {
        using MemoryStream peStream = new();
        using MemoryStream pdbStream = new();

        EmitResult result = compilation.Emit(peStream, pdbStream, options: Constants.PdbEmitOptions,
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

            if (diagnostic.Location.SourceTree != null)
            {
                SyntaxTree tree = diagnostic.Location.SourceTree;
                LocationKind kind = diagnostic.Location.Kind;
                string? fileName = tree.FilePath ?? "UnknownFile.cs";
                FileLinePositionSpan span = diagnostic.Location.GetLineSpan();
                int line = span.StartLinePosition.Line + 1;
                int charPos = span.StartLinePosition.Character + 1;

                if (compilation.SyntaxTrees.Contains(tree) && diagnostic.Severity == DiagnosticSeverity.Error)
                {
                    _logger.LogError(Constants.CompileEventId, "Failed to compile {tree} - {message} (L: {line} | P: {pos}) | Removing from project",
                        fileName, diagnostic.GetMessage(), line, charPos);

                    compilation = compilation.RemoveSyntaxTrees(tree);
                    compilerMessage.ExtraData += $"[Error][{diagnostic.Id}][{fileName}] {diagnostic.GetMessage()} | Line: {line}, Pos: {charPos} {Environment.NewLine}";
                    modified = true;
                    compilationResult.Failed++;
                }
            }
            else
            {
                _logger.LogError(Constants.CompileEventId, $"[Error][{diagnostic.Id}] {diagnostic.GetMessage()}");
            }
        }

        if (modified && compilation.SyntaxTrees.Length > 0)
        {
            CompileProject(compilation, compilerData, compilerMessage, compilationResult, cancellationToken);
        }
    }
}
