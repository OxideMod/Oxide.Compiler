using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ObjectStream.Data;
using Oxide.CompilerServices.Logging;
using Oxide.CompilerServices.Settings;
using PolySharp.SourceGenerators;
using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

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
            logger.LogDebug(Events.Startup, "C# for {version} Initialized!", AppContext.TargetFrameworkName);
            _token = token.Token;
            _services = provider;
        }

        public async Task Compile(int id, CompilerData data)
        {
            _logger.LogInformation(Events.Compile, "====== Compilation Job {id} ======", id);
            try
            {
                CompilerMessage message = await SafeCompile(data, new CompilerMessage() { Id = id, Type = CompilerMessageType.Assembly, Client = data.Message.Client });
                if (((CompilationResult)message.Data).Data.Length > 0) _logger.LogInformation(Events.Compile, "==== Compilation Finished {id} | Success ====", id);
                else _logger.LogInformation(Events.Compile, "==== Compilation Finished {id} | Failed ====", id);
                message.Client!.PushMessage(message);

            }
            catch (Exception e)
            {
                _logger.LogError(Events.Compile, e, "==== Compilation Error {id} ====", id);
                data.Message.Client!.PushMessage(new CompilerMessage() { Id = id, Type = CompilerMessageType.Error, Data = e });
            }
        }

        private async Task<CompilerMessage> SafeCompile(CompilerData data, CompilerMessage message)
        {
            if (data == null) throw new ArgumentNullException(nameof(data), "Missing compile data");

            if (data.SourceFiles == null || data.SourceFiles.Length == 0) throw new ArgumentException("No source files provided", nameof(data.SourceFiles));
            OxideResolver resolver = (OxideResolver)_services.GetRequiredService<MetadataReferenceResolver>();
            _logger.LogDebug(Events.Compile, GetJobStructure(data));

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
                            if (references.ContainsKey(fileName))
                            {
                                _logger.LogDebug(Events.Compile, "Replacing existing project reference: {ref}", fileName);
                            }
                            else
                            {
                                _logger.LogDebug(Events.Compile, "Adding project reference: {ref}", fileName);
                            }

                            references[fileName] = File.Exists(reference.Name) && (reference.Data == null || reference.Data.Length == 0)  ? MetadataReference.CreateFromFile(reference.Name) : MetadataReference.CreateFromImage(reference.Data, filePath: reference.Name);
                            continue;

                        default:
                            _logger.LogWarning(Events.Compile, "Ignoring unhandled project reference: {ref}", fileName);
                            continue;
                    }
                }
            }

            Dictionary<CompilerFile, SyntaxTree> trees = new();
            Encoding encoding = Encoding.GetEncoding(data.Encoding);
            CSharpParseOptions options = new(data.CSharpVersion());
            _logger.LogDebug(Events.Compile, "Parsing source files using C# {version} with encoding {encoding}", data.CSharpVersion(), encoding.WebName);
            foreach (var source in data.SourceFiles)
            {
                string fileName = Path.GetFileName(source.Name);

                string sourceString = Regex.Replace(encoding.GetString(source.Data), @"\\[uU]([0-9A-F]{4})", match =>
                {
                    return ((char)int.Parse(match.Value.Substring(2), NumberStyles.HexNumber)).ToString();
                });

                SyntaxTree tree = CSharpSyntaxTree.ParseText(sourceString, options, path: fileName, encoding: encoding, cancellationToken: _token);
                trees.Add(source, tree);
                _logger.LogDebug(Events.Compile, "Added C# file {file} to the project", fileName);
            }

            CSharpCompilationOptions compOptions = new(data.OutputKind(), metadataReferenceResolver: resolver, platform: data.Platform(), allowUnsafe: true, optimizationLevel: data.Debug ? OptimizationLevel.Debug : OptimizationLevel.Release);
            CSharpCompilation comp = CSharpCompilation.Create(Path.GetRandomFileName(), trees.Values, references.Values, compOptions);

            PolyfillsGenerator generator = new();
            ISourceGenerator sourceGen = generator.AsSourceGenerator();

            GeneratorDriver driver = CSharpGeneratorDriver.Create(generators: new ISourceGenerator[] { sourceGen }
            , driverOptions: new GeneratorDriverOptions(default, trackIncrementalGeneratorSteps: true));
            driver = driver.WithUpdatedParseOptions(options);

            driver = driver.RunGenerators(comp, _token);
            GeneratorDriverRunResult genResult = driver.GetRunResult();

            if (genResult.GeneratedTrees.Length > 0)
            {
                _logger.LogInformation(Events.Compile, "Adding compiler generated classes: {classes}", string.Join(", ", genResult.GeneratedTrees.Select(t => Path.GetFileName(t.FilePath))));
                comp = comp.AddSyntaxTrees(genResult.GeneratedTrees);
            }

            CompilationResult? payload = CompileProject(comp, message);
            return message;
        }

        private CompilationResult CompileProject(CSharpCompilation compilation, CompilerMessage message)
        {
            using MemoryStream pe = new();
            //using MemoryStream pdb = new();

            EmitResult result = compilation.Emit(pe, cancellationToken: _token);
            if (result.Success)
            {
                CompilationResult data = new()
                {
                    Name = compilation.AssemblyName,
                    Data = pe.ToArray()
                };
                message.Data = data;
                return data;
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
                    }
                }
                else
                {
                    _logger.LogError(Events.Compile, $"[Error][{diag.Id}] {diag.GetMessage()}");
                }
            }

            if (modified && compilation.SyntaxTrees.Length > 0)
            {
                return CompileProject(compilation, message);
            }


            CompilationResult r = new()
            {
                Name = compilation.AssemblyName!
            };

            message.Data = r;
            return r;
        }

        private static string GetJobStructure(CompilerData data)
        {
            StringBuilder builder = new();
            builder.AppendLine($"Encoding: {data.Encoding}, Target: {data.CSharpVersion()}, Output: {data.OutputKind()}, Optimize: {!data.Debug}");
            builder.AppendLine($"== Source Files ({data.SourceFiles.Length}) ==");
            builder.AppendLine(string.Join(", ", data.SourceFiles.Select(s => $"[{s.Data.Length}] {s.Name}")));

            if (data.ReferenceFiles != null && data.ReferenceFiles.Length > 0)
            {
                builder.AppendLine($"== Reference Files ({data.ReferenceFiles.Length}) ==");
                builder.AppendLine(string.Join(", ", data.ReferenceFiles.Select(r => $"[{r.Data.Length}] {r.Name}")));
            }
            return builder.ToString();
        }
    }
}
