using Microsoft.CodeAnalysis;
using Microsoft.Extensions.Logging;
using Oxide.CompilerServices.Types.Compilation;

namespace Oxide.CompilerServices.Common;

public static class ExtensionMethods
{
    public static Serilog.Events.LogEventLevel ToSerilog(this LogLevel level)
    {
        return level switch
        {
            LogLevel.Trace => Serilog.Events.LogEventLevel.Verbose,
            LogLevel.Debug => Serilog.Events.LogEventLevel.Debug,
            LogLevel.Information => Serilog.Events.LogEventLevel.Information,
            LogLevel.Warning => Serilog.Events.LogEventLevel.Warning,
            LogLevel.Error => Serilog.Events.LogEventLevel.Error,
            LogLevel.Critical => Serilog.Events.LogEventLevel.Fatal,
            _ => Serilog.Events.LogEventLevel.Information
        };
    }

    public static Platform Platform(this CompilerData data)
    {
        return data.Platform switch
        {
            CompilerPlatform.AnyCPU32Preferred => Microsoft.CodeAnalysis.Platform.AnyCpu32BitPreferred,
            CompilerPlatform.Arm => Microsoft.CodeAnalysis.Platform.Arm,
            CompilerPlatform.X64 => Microsoft.CodeAnalysis.Platform.X64,
            CompilerPlatform.X86 => Microsoft.CodeAnalysis.Platform.X86,
            CompilerPlatform.IA64 => Microsoft.CodeAnalysis.Platform.Itanium,
            _ => Microsoft.CodeAnalysis.Platform.AnyCpu,
        };
    }

    public static OutputKind OutputKind(this CompilerData data)
    {
        return data.Target switch
        {
            CompilerTarget.Module => Microsoft.CodeAnalysis.OutputKind.NetModule,
            CompilerTarget.WinExe => Microsoft.CodeAnalysis.OutputKind.WindowsApplication,
            CompilerTarget.Exe => Microsoft.CodeAnalysis.OutputKind.ConsoleApplication,
            _ => Microsoft.CodeAnalysis.OutputKind.DynamicallyLinkedLibrary,
        };
    }

    public static Microsoft.CodeAnalysis.CSharp.LanguageVersion GetLanguageVersion(this CompilerData data)
    {
        return data.Version switch
        {
            CompilerLanguageVersion.Latest => Microsoft.CodeAnalysis.CSharp.LanguageVersion.Latest,
            CompilerLanguageVersion.Preview => Microsoft.CodeAnalysis.CSharp.LanguageVersion.Preview,
            CompilerLanguageVersion.V14 => Microsoft.CodeAnalysis.CSharp.LanguageVersion.CSharp14,
            CompilerLanguageVersion.V13 => Microsoft.CodeAnalysis.CSharp.LanguageVersion.CSharp13,
            CompilerLanguageVersion.V12 => Microsoft.CodeAnalysis.CSharp.LanguageVersion.CSharp12,
            CompilerLanguageVersion.V11 => Microsoft.CodeAnalysis.CSharp.LanguageVersion.CSharp11,
            CompilerLanguageVersion.V10 => Microsoft.CodeAnalysis.CSharp.LanguageVersion.CSharp10,
            CompilerLanguageVersion.V9 => Microsoft.CodeAnalysis.CSharp.LanguageVersion.CSharp9,
            CompilerLanguageVersion.V8 => Microsoft.CodeAnalysis.CSharp.LanguageVersion.CSharp8,
            CompilerLanguageVersion.V7 => Microsoft.CodeAnalysis.CSharp.LanguageVersion.CSharp7,
            CompilerLanguageVersion.V6 => Microsoft.CodeAnalysis.CSharp.LanguageVersion.CSharp6,
            CompilerLanguageVersion.V5 => Microsoft.CodeAnalysis.CSharp.LanguageVersion.CSharp5,
            CompilerLanguageVersion.V4 => Microsoft.CodeAnalysis.CSharp.LanguageVersion.CSharp4,
            CompilerLanguageVersion.V3 => Microsoft.CodeAnalysis.CSharp.LanguageVersion.CSharp3,
            CompilerLanguageVersion.V2 => Microsoft.CodeAnalysis.CSharp.LanguageVersion.CSharp2,
            CompilerLanguageVersion.V1 => Microsoft.CodeAnalysis.CSharp.LanguageVersion.CSharp1,
            _ => Microsoft.CodeAnalysis.CSharp.LanguageVersion.Latest,
        };
    }

    //TODO: Add logger
    public static async ValueTask Forget(this ValueTask task)
    {
        try
        {
            await task;
        }
        catch (Exception exception)
        {
            Console.WriteLine(exception);
        }
    }

    public static async Task Forget(this Task task)
    {
        try
        {
            await task;
        }
        catch (Exception exception)
        {
            Console.WriteLine(exception);
        }
    }
}
