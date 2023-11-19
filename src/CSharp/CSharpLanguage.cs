using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ObjectStream.Data;
using Oxide.CompilerServices.Logging;
using Oxide.CompilerServices.Settings;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Serilog.Events;

namespace Oxide.CompilerServices.CSharp
{
    internal class CSharpLanguage : ICompilerService
    {
        private readonly ILogger _logger;

        private readonly OxideSettings _settings;

        private readonly CancellationToken _token;

        private readonly IServiceProvider _services;

        private readonly ImmutableArray<string> ignoredCodes = ImmutableArray.Create(new string[]
        {
            "CS1701"
        });

        public CSharpLanguage(ILogger<CSharpLanguage> logger, OxideSettings settings, IServiceProvider provider, CancellationTokenSource token)
        {
            _logger = logger;
            _settings = settings;
            _token = token.Token;
            _services = provider;
        }

        public async Task Compile(int id, CompilerData data)
        {
            Stopwatch stopwatch = Stopwatch.StartNew();
            _logger.LogInformation(Events.Compile, $"Starting compilation of job {id} | Total Plugins: {data.SourceFiles.Length}");
            string details =
                $"Settings[Encoding: {data.Encoding}, CSVersion: {data.CSharpVersion()}, Target: {data.OutputKind()}, Platform: {data.Platform()}, StdLib: {data.StdLib}, Debug: {data.Debug}, Preprocessor: {string.Join(", ", data.Preprocessor)}]";

            if (Program.ApplicationLogLevel.MinimumLevel <= LogEventLevel.Debug)
            {
                if (data.ReferenceFiles.Length > 0)
                {
                    details += Environment.NewLine + $"Reference Files:" + Environment.NewLine;
                    for (int i = 0; i < data.ReferenceFiles.Length; i++)
                    {
                        CompilerFile reference = data.ReferenceFiles[i];
                        if (i > 0)
                        {
                            details += Environment.NewLine;
                        }

                        details += $"  - [{i + 1}] {Path.GetFileName(reference.Name)}({reference.Data.Length})";
                    }
                }

                if (data.SourceFiles.Length > 0)
                {
                    details += Environment.NewLine + $"Plugin Files:" + Environment.NewLine;

                    for (int i = 0; i < data.SourceFiles.Length; i++)
                    {
                        CompilerFile plugin = data.SourceFiles[i];
                        if (i > 0)
                        {
                            details += Environment.NewLine;
                        }

                        details += $"  - [{i + 1}] {Path.GetFileName(plugin.Name)}({plugin.Data.Length})";
                    }
                }
            }



            _logger.LogDebug(Events.Compile, details);

            try
            {
                CompilerMessage message = await SafeCompile(data, new CompilerMessage() { Id = id, Type = CompilerMessageType.Assembly, Client = data.Message.Client });
                CompilationResult result = message.Data as CompilationResult;

                if (result.Data.Length > 0)
                {
                    _logger.LogInformation(Events.Compile, $"Successfully compiled {result.Success}/{data.SourceFiles.Length} plugins for job {id} in {stopwatch.ElapsedMilliseconds}ms");
                }
                else
                {
                    _logger.LogError(Events.Compile, $"Failed to compile job {id} in {stopwatch.ElapsedMilliseconds}ms");
                }

                message.Client!.PushMessage(message);
                _logger.LogDebug(Events.Compile, $"Pushing job {id} back to parent");
            }
            catch (Exception e)
            {
                _logger.LogError(Events.Compile, e, $"Error while compiling job {id} - {e.Message}");
                data.Message.Client!.PushMessage(new CompilerMessage() { Id = id, Type = CompilerMessageType.Error, Data = e });
            }
        }

        private async Task<CompilerMessage> SafeCompile(CompilerData data, CompilerMessage message)
        {
            if (data == null) throw new ArgumentNullException(nameof(data), "Missing compile data");

            if (data.SourceFiles == null || data.SourceFiles.Length == 0) throw new ArgumentException("No source files provided", nameof(data.SourceFiles));
            OxideResolver resolver = (OxideResolver)_services.GetRequiredService<MetadataReferenceResolver>();

            Dictionary<string, MetadataReference> references = new(StringComparer.OrdinalIgnoreCase);

            if (data.StdLib)
            {
                references.Add("System.Private.CoreLib.dll", resolver.Reference("System.Private.CoreLib.dll")!);
                references.Add("netstandard.dll", resolver.Reference("netstandard.dll")!);
                references.Add("System.Runtime.dll", resolver.Reference("System.Runtime.dll")!);
                references.Add("System.Collections.dll", resolver.Reference("System.Collections.dll")!);
                references.Add("System.Collections.Immutable.dll", resolver.Reference("System.Collections.Immutable.dll")!);
                references.Add("System.Linq.dll", resolver.Reference("System.Linq.dll")!);
                references.Add("System.Data.Common.dll", resolver.Reference("System.Data.Common.dll")!);
            }

            if (data.ReferenceFiles != null && data.ReferenceFiles.Length > 0)
            {
                foreach (var reference in data.ReferenceFiles)
                {
                    string fileName = Path.GetFileName(reference.Name);
                    switch (Path.GetExtension(reference.Name))
                    {
                        case ".cs":
                        case ".exe":
                        case ".dll":
                            references[fileName] = File.Exists(reference.Name) && (reference.Data == null || reference.Data.Length == 0)  ? MetadataReference.CreateFromFile(reference.Name) : MetadataReference.CreateFromImage(reference.Data, filePath: reference.Name);
                            continue;

                        default:
                            _logger.LogWarning(Events.Compile, "Ignoring unhandled project reference: {ref}", fileName);
                            continue;
                    }
                }
                _logger.LogDebug(Events.Compile, $"Added {references.Count} project references");
            }

            Dictionary<CompilerFile, SyntaxTree> trees = new();
            Encoding encoding = Encoding.GetEncoding(data.Encoding);
            CSharpParseOptions options = new(data.CSharpVersion(), preprocessorSymbols: data.Preprocessor);
            foreach (var source in data.SourceFiles)
            {
                string fileName = Path.GetFileName(source.Name);
                bool isUnicode = false;
                string sourceString = Regex.Replace(encoding.GetString(source.Data), @"\\[uU]([0-9A-F]{4})", match =>
                {
                    isUnicode = true;
                    return ((char)int.Parse(match.Value.Substring(2), NumberStyles.HexNumber)).ToString();
                }, RegexOptions.Compiled | RegexOptions.IgnoreCase);

                if (isUnicode)
                {
                    _logger.LogDebug(Events.Compile, $"Plugin {fileName} is using unicode escape sequence");
                }

                SyntaxTree tree = CSharpSyntaxTree.ParseText(sourceString, options, path: fileName, encoding: encoding, cancellationToken: _token);
                trees.Add(source, tree);
            }
            _logger.LogDebug(Events.Compile, $"Added {trees.Count} plugins to the project");
            CSharpCompilationOptions compOptions = new(data.OutputKind(), metadataReferenceResolver: resolver, platform: data.Platform(), allowUnsafe: true, optimizationLevel: data.Debug ? OptimizationLevel.Debug : OptimizationLevel.Release);
            CSharpCompilation comp = CSharpCompilation.Create(Path.GetRandomFileName(), trees.Values, references.Values, compOptions);

            CompilationResult result = new()
            {
                Name = comp.AssemblyName
            };

            message.Data = result;
            CompileProject(comp, message, result);
            return message;
        }

        private void CompileProject(CSharpCompilation compilation, CompilerMessage message, CompilationResult compResult)
        {
            using MemoryStream pe = new();
            EmitResult result = compilation.Emit(pe, cancellationToken: _token);

            if (result.Success)
            {
                compResult.Data = pe.ToArray();
                compResult.Success = compilation.SyntaxTrees.Length;
                return;
            }

            bool modified = false;

            foreach (var diag in result.Diagnostics)
            {
                if (ignoredCodes.Contains(diag.Id))
                {
                    continue;
                }

                if (diag.Location.SourceTree != null)
                {
                    SyntaxTree tree = diag.Location.SourceTree;
                    LocationKind kind = diag.Location.Kind;
                    string? fileName = tree.FilePath ?? "UnknownFile.cs";
                    FileLinePositionSpan span = diag.Location.GetLineSpan();
                    int line = span.StartLinePosition.Line + 1;
                    int charPos = span.StartLinePosition.Character + 1;

                    if (compilation.SyntaxTrees.Contains(tree) && diag.Severity == DiagnosticSeverity.Error)
                    {
                        _logger.LogWarning(Events.Compile, "Failed to compile {tree} - {message} (L: {line} | P: {pos}) | Removing from project", fileName, diag.GetMessage(), line, charPos);
                        compilation = compilation.RemoveSyntaxTrees(tree);
                        message.ExtraData += $"[Error][{diag.Id}][{fileName}] {diag.GetMessage()} | Line: {line}, Pos: {charPos} {Environment.NewLine}";
                        modified = true;
                        compResult.Failed++;
                    }
                }
                else
                {
                    _logger.LogError(Events.Compile, $"[Error][{diag.Id}] {diag.GetMessage()}");
                }
            }

            if (modified && compilation.SyntaxTrees.Length > 0)
            {
                CompileProject(compilation, message, compResult);
            }
        }
    }
}
