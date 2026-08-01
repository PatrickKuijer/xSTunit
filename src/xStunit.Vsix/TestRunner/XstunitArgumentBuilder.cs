using System.Collections.Generic;
using System.Text;

namespace xStunit.Vsix.TestRunner
{
    /// <summary>
    /// Builds the command line for one `xstunit &lt;path-a&gt; [&lt;path-b&gt; ...]
    /// (--stream|--format json) [--plugins &lt;dir&gt;] [--suite &lt;name&gt; ...]`
    /// invocation.
    /// </summary>
    /// <remarks>
    /// Carries no VS SDK dependency and takes plain strings rather than an
    /// <see cref="XstunitConfig"/>, so it can be source-linked into
    /// tests/xStunit.Vsix.Tests and unit tested under net8.0; the extension itself
    /// only builds under net472.
    /// </remarks>
    internal static class XstunitArgumentBuilder
    {
        // Null or empty suiteNames runs every suite found under paths; a non-empty list
        // restricts the run to those suites, one repeated --suite <name> each. That is
        // what the tool window's "rerun failed" is built on.
        public static string BuildStreamingArguments(string cliPath, IEnumerable<string> paths, IReadOnlyList<string> suiteNames, string pluginsDirectory = null) =>
            Build(cliPath, paths, suiteNames, pluginsDirectory, "--stream");

        /// <summary>
        /// The same run, asking for one buffered blob at the end instead of a stream of
        /// events - what XstunitProcessRunner retries with when a CLI turns out to
        /// predate --stream.
        /// </summary>
        public static string BuildJsonArguments(string cliPath, IEnumerable<string> paths, IReadOnlyList<string> suiteNames, string pluginsDirectory = null) =>
            Build(cliPath, paths, suiteNames, pluginsDirectory, "--format json");

        // One body for both output flags, so the retry can never differ from the run it
        // is retrying in anything but that flag.
        private static string Build(string cliPath, IEnumerable<string> paths, IReadOnlyList<string> suiteNames, string pluginsDirectory, string outputFlag)
        {
            var arguments = new StringBuilder();
            arguments.Append(EscapeArgument(cliPath)).Append(' ');
            foreach (var path in paths)
            {
                arguments.Append(EscapeArgument(path)).Append(' ');
            }
            arguments.Append(outputFlag);

            if (!string.IsNullOrEmpty(pluginsDirectory))
            {
                arguments.Append(" --plugins ").Append(EscapeArgument(pluginsDirectory));
            }

            if (suiteNames != null)
            {
                foreach (var suiteName in suiteNames)
                {
                    arguments.Append(" --suite ").Append(EscapeArgument(suiteName));
                }
            }

            return arguments.ToString();
        }

        // Win32/CommandLineToArgvW argument quoting, reimplemented because net472's
        // Process has no ArgumentList (.NET Core's does this internally). Naively
        // wrapping a value in quotes breaks as soon as it ends in a backslash (a path
        // like "C:\Foo\"), where that backslash escapes the closing quote instead of
        // separating a path component; a value containing a literal '"' would break
        // unquoted concatenation too.
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
