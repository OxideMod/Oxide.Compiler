using System.Text.RegularExpressions;

namespace Oxide.CompilerServices.Common;

public static partial class RegexExtensions
{
    [GeneratedRegex(@"\\[uU]([0-9A-F]{4})", RegexOptions.IgnoreCase, "en-US")]
    public static partial Regex UnicodeEscapePattern { get; }
}
