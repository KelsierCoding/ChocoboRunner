using System.Text.RegularExpressions;

namespace ChocoboRunner.PlatformSteam;

public static class ParseAcf
{
    public static string Parse(string content, string key)
    {
        string pattern = $"\"{Regex.Escape(key)}\"\\s*\"([^\"]*)\"";
        Match m = Regex.Match(content, pattern, RegexOptions.IgnoreCase);
        return m.Success ? m.Groups[1].Value : string.Empty;
    }
}