using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

using Mono.CSharp;

namespace Oxide.Wrapper
{
    class Wrapper
    {
        static int Main(string[] args)
        {
            for (var i = 0; i < args.Length; i++)
            {
                args[i] = Encoding.UTF8.GetString(Encoding.Unicode.GetBytes(args[i])).TrimEnd();
            }
            var result = 0;
            var outString = new StringBuilder();
            var error = new StringWriter();
            var tries = 0;
            var regex = new Regex(@"([\w\.]+)\(\d+,\d+\): error|error \w+: Source file `[\\\./]*([\w\.]+)", RegexOptions.Compiled);
            while (!CompilerCallableEntryPoint.InvokeCompiler(args, error))
            {
                var cmd = new CommandLineParser(error);
                var settings = cmd.ParseArguments(args);
                if (settings == null || settings.FirstSourceFile == null)
                {
                    result = 1;
                    break;
                }
                var matches = regex.Matches(error.GetStringBuilder().ToString());
                var files = new HashSet<string>();
                foreach (Match match in matches)
                {
                    for (var i = 1; i < match.Groups.Count; i++)
                    {
                        if (string.IsNullOrWhiteSpace(match.Groups[i].Value)) continue;
                        files.Add(match.Groups[i].Value);
                    }
                }
                if (files.Count == 0 || files.Count >= settings.SourceFiles.Count || tries++ > 10)
                {
                    result = 1;
                    break;
                }
                args = files.Aggregate(args, (current, file) => current.Where(arg => arg.StartsWith("/") || !arg.EndsWith(file)).ToArray());
                error.WriteLine("Warning: restarting compilation");
                outString.Append(error.GetStringBuilder());
                error.GetStringBuilder().Clear();
            }
            Console.Write(outString.Append(error.GetStringBuilder()));
            Environment.Exit(result);
            return result;
        }
    }
}
