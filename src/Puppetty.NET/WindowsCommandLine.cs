using System.Text;

namespace Puppetty;

/// <summary>
/// Builds a Windows command-line argument string that the MSVCRT / CommandLineToArgvW parser splits
/// back into exactly the original arguments.
/// </summary>
internal static class WindowsCommandLine
{
    public static string Join(IEnumerable<string> args) => string.Join(' ', args.Select(Quote));

    public static string Quote(string arg)
    {
        if (arg.Length > 0 && arg.IndexOfAny([' ', '\t', '\n', '\v', '"']) < 0)
            return arg;

        var sb = new StringBuilder(arg.Length + 2).Append('"');
        var backslashes = 0;
        foreach (var c in arg)
        {
            if (c == '\\')
            {
                backslashes++;
                continue;
            }
            // Backslashes are literal unless they precede a quote; then each must be doubled, and
            // the quote itself escaped.
            sb.Append('\\', c == '"' ? backslashes * 2 + 1 : backslashes);
            backslashes = 0;
            sb.Append(c);
        }
        // Trailing backslashes precede the closing quote, so they double too.
        sb.Append('\\', backslashes * 2).Append('"');
        return sb.ToString();
    }
}
