using System;
using System.IO;
using Serilog;
using Serilog.Events;

namespace TcXunit.Interpreter.Logging
{
    /// <summary>
    /// Process-wide structured logging for TcXunit's catch-and-summarize paths (TcXunit-2v8).
    /// CliRunner collapses exceptions down to ex.Message for the concise failure
    /// surfaced in its console/JSON output; this additionally logs the full ex.ToString() (message +
    /// stack trace + inner exceptions) to a rolling log file, so a failing suite's actual call
    /// site can be reconstructed from the log instead of hand-editing a catch block, rebuilding,
    /// and reverting.
    ///
    /// Verbosity is toggled by the TCXUNIT_VERBOSE env var (Information by default, Debug when
    /// set to a truthy value) rather than a config file, per the original proposal. The log
    /// directory can be overridden via TCXUNIT_LOG_DIR (defaults to %TEMP%/TcXunit/logs) so tests
    /// don't write into a developer's real temp directory.
    ///
    /// No dependency on Microsoft.Extensions.Logging here, unlike the sibling tcagentplugin repo's
    /// AppLogging/SerilogLoggerAdapter: that repo hand-rolls an MEL bridge because its VSIX host
    /// has no app.config for a binding redirect between its own MEL reference and the one
    /// Serilog.Extensions.Logging would pull in. TcXunit is plain SDK-style/PackageReference with
    /// no such constraint, so callers use this type directly instead of an ILogger abstraction.
    /// </summary>
    public static class TcXunitLog
    {
        private const string VerboseEnvVar = "TCXUNIT_VERBOSE";
        private const string LogDirEnvVar = "TCXUNIT_LOG_DIR";

        private static readonly object SyncRoot = new object();
        private static ILogger _logger;

        /// <summary>
        /// Logs a caught exception's full ex.ToString() (message + stack trace + inner
        /// exceptions) at Error level. Safe to call with a null exception (no-op) so call sites
        /// don't need an extra null check around it.
        /// </summary>
        public static void LogException(string context, Exception exception)
        {
            if (exception == null)
                return;

            GetOrCreateLogger().Error(exception, "{Context}: {ExceptionMessage}", context, exception.Message);
        }

        /// <summary>
        /// Debug-level trace for engine decision points (POU skip/reject reasons, duplicate-type
        /// resolution, suite discovery) so a failing suite's actual code path can be reconstructed
        /// from the log without re-instrumenting. Only reaches the log file when TCXUNIT_VERBOSE
        /// is set - otherwise a no-op cost of a single IsEnabled check.
        /// </summary>
        public static void LogDebug(string messageTemplate, params object[] propertyValues)
        {
            GetOrCreateLogger().Debug(messageTemplate, propertyValues);
        }

        internal static bool IsTruthy(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return false;

            return value == "1"
                || string.Equals(value, "true", StringComparison.OrdinalIgnoreCase)
                || string.Equals(value, "yes", StringComparison.OrdinalIgnoreCase);
        }

        // Pure so the env-var toggle rule can be unit tested without touching Serilog/file I/O.
        internal static LogEventLevel ResolveMinimumLevel(string verboseEnvValue) =>
            IsTruthy(verboseEnvValue) ? LogEventLevel.Debug : LogEventLevel.Information;

        internal static string ResolveLogDirectory(string logDirEnvValue) =>
            string.IsNullOrWhiteSpace(logDirEnvValue)
                ? Path.Combine(Path.GetTempPath(), "TcXunit", "logs")
                : logDirEnvValue;

        // Test seam only: forces the next LogException/LogDebug call to rebuild the logger from
        // the current env vars instead of reusing whatever was cached by an earlier call. Disposes
        // the current logger first so its file sink releases the log file - otherwise a test that
        // deletes its temp log directory right after this call would race the sink's file handle.
        internal static void ResetForTests()
        {
            lock (SyncRoot)
            {
                (_logger as IDisposable)?.Dispose();
                _logger = null;
            }
        }

        private static ILogger GetOrCreateLogger()
        {
            if (_logger != null)
                return _logger;

            lock (SyncRoot)
            {
                if (_logger == null)
                {
                    var minimumLevel = ResolveMinimumLevel(Environment.GetEnvironmentVariable(VerboseEnvVar));
                    var logDirectory = ResolveLogDirectory(Environment.GetEnvironmentVariable(LogDirEnvVar));

                    _logger = new LoggerConfiguration()
                        .MinimumLevel.Is(minimumLevel)
                        .Enrich.FromLogContext()
                        .WriteTo.File(
                            Path.Combine(logDirectory, "tcxunit-.log"),
                            rollingInterval: RollingInterval.Day,
                            retainedFileCountLimit: 14,
                            outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {Message:lj}{NewLine}{Exception}",
                            shared: true)
                        .CreateLogger();
                }

                return _logger;
            }
        }
    }
}
