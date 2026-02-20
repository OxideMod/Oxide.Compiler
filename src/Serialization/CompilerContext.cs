using System.Text.Json.Serialization;
using Oxide.CompilerServices.Types.Compilation;

namespace Oxide.CompilerServices.Serialization;

[JsonSerializable(typeof(CompilerMessage))]
public partial class CompilerMessageContext : JsonSerializerContext;

[JsonSerializable(typeof(CompilerData))]
public partial class CompilerDataContext : JsonSerializerContext;

[JsonSerializable(typeof(CompilationResult))]
public partial class CompilationResultContext : JsonSerializerContext;
