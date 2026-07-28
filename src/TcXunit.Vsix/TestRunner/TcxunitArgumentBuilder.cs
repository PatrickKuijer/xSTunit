using System.Collections.Generic;
using System.Text;

namespace TcXunit.Vsix.TestRunner
{
    /// <summary>
    /// Pure command-line construction for a `tcxunit &lt;path-a&gt; [&lt;path-b&gt; ...]
    /// --format json [--suite &lt;name&gt; ...]` invocation. Split out of
    /// TcxunitProcessRunner (TcXunit-1tt.8) so it can be unit tested under net8.0. At the
    /// time this class was split out, TcxunitProcessRunner.cs and TcxunitConfig.cs both
    /// used System.Web.Script.Serialization.JavaScriptSerializer (net472-only, no net8.0
    /// package), which blocked source-linking either into tests/TcXunit.Vsix.Tests --
    /// TcXunit-cmp later swapped both to System.Text.Json, removing that blocker (see
    /// TcxunitConfig.cs, now linked into that test project too). This class still takes
    /// plain strings/lists rather than a TcxunitConfig instance regardless, since that
    /// remains the simpler seam for pure argument-construction logic.
    ///
    /// suiteNames is TcXunit-1tt.8's "rerun failed" feature: the WPF host passes the last
    /// run's failed suite names back in here, one repeated --suite &lt;name&gt; per name
    /// (TcXunit-6fb.3's CLI flag), restricting the next run to just those suites. Null or
    /// empty reproduces the exact argument string a normal run has always used.
    /// </summary>
    internal static class TcxunitArgumentBuilder
    {
        public static string BuildArguments(string cliPath, IEnumerable<string> paths, IReadOnlyList<string> suiteNames)
        {
            var arguments = new StringBuilder();
            arguments.Append(EscapeArgument(cliPath)).Append(' ');
            foreach (var path in paths)
            {
                arguments.Append(EscapeArgument(path)).Append(' ');
            }
            arguments.Append("--format json");

            if (suiteNames != null)
            {
                foreach (var suiteName in suiteNames)
                {
                    arguments.Append(" --suite ").Append(EscapeArgument(suiteName));
                }
            }

            return arguments.ToString();
        }

        // Win32/CommandLineToArgvW argument-quoting algorithm (the same one .NET Core's
        // ProcessStartInfo.ArgumentList uses internally - not available on net472's
        // Process, so it's reimplemented here). Naively wrapping a value in quotes breaks
        // as soon as it ends in a backslash (e.g. a path like "C:\Foo\") because that
        // backslash then escapes the closing quote instead of being a path separator; a
        // value containing a literal '"' would break unquoted concatenation too. This
        // always quotes and doubles any run of backslashes that's immediately followed by
        // a quote (embedded or closing).
        public static string EscapeArgument(string argument)
        {
            var result = new StringBuilder();
            result.Append('"');

            var backslashCount = 0;
            foreach (var c in argument)
            {
                if (c == '\\')
                {
                    backslashCount++;
                    continue;
                }

                if (c == '"')
                {
                    // Backslashes immediately before a quote must be doubled, plus one
                    // more to escape the quote itself.
                    result.Append('\\', backslashCount * 2 + 1);
                    result.Append('"');
                    backslashCount = 0;
                    continue;
                }

                if (backslashCount > 0)
                {
                    result.Append('\\', backslashCount);
                    backslashCount = 0;
                }

                result.Append(c);
            }

            // Backslashes immediately before the closing quote must be doubled so they
            // aren't read as escaping it.
            result.Append('\\', backslashCount * 2);
            result.Append('"');
            return result.ToString();
        }
    }
}
