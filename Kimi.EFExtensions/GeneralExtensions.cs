using System.Globalization;
using System.Text.RegularExpressions;

namespace Kimi.EFExtensions;

internal static class GeneralExtensions
{
    internal static bool ContainsSensitiveWords(this string input, string[] sensitiveWords)
    {
        return sensitiveWords.Any(word => input.IndexOf(word, StringComparison.OrdinalIgnoreCase) >= 0);
    }

    internal static bool IsDouble(this string theValue)
    {
        double retNum;
        return double.TryParse(theValue, NumberStyles.Number, NumberFormatInfo.InvariantInfo, out retNum);
    }

    internal static string ReplacePropertyNamesWithColumnNames(this string sourceString, Dictionary<string, string> args)
    {
        Regex re = new Regex(@"\[(\w+)\]", RegexOptions.Compiled);
        string output = re.Replace(sourceString,
            match => args.TryGetValue(match.Groups[1].Value, out string? _)
                ? "[" + args[match.Groups[1].Value] + "]" : match.Value
        );
        return output;
    }

}
