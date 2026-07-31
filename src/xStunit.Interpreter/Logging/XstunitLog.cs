using System;
using System.IO;
using Serilog;
using Serilog.Events;

namespace xStunit.Interpreter.Logging
{
    /// <summary>
    /// Process-wide structured logging for the catch-and-summarize paths.
    /// CliRunner collapses an exception down to <c>ex.Message</c> for the concise failure it
    /// surfaces in console/JSON output; this writes the full <c>ex.ToString()</c> (message,
    /// stack trace, inner exceptions) to a rolling log file, so a failing suite's real call
    /// site can be recovered from the log rather than by instrumenting a catch block and
    /// rebuilding.
    /// </summary>
    /// <remarks>
    /// Configured entirely by environment variable, deliberately: <c>XSTUNIT_VERBOSE</c> raises
    /// the level from Information to Debug, and <c>XSTUNIT_LOG_DIR</c> redirects the log
    /// directory away from its <c>%TEMP%/xStunit/logs</c> default so tests never write into a
    /// developer's real temp directory.
    ///
    /// Callers use this type directly rather than an ILogger abstraction; nothing here depends
    /// on Microsoft.Extensions.Logging.
    /// </remarks>
    public static class XstunitLog
    {
        private const string VerboseEnvVar = "XSTUNIT_VERBOSE";
        private const string LogDirEnvVar = "XSTUNIT_LOG_DIR";

        private static readonly object SyncRoot = new object();
        private static ILogger _logger;

        /// <summary>
        /// Logs a caught exception's full <c>ex.ToString()</c> at Error level. A null
        /// <paramref name="exception"/> is a no-op, so call sites in a catch-and-summarize path
        /// need no null check of their own.
        /// </summary>
        /// <param name="context">Names the operation being attempted, for the log line.</param>
        /// <param name="exception">The caught exception; null is ignored.</param>
        public static void LogException(string context, Exception exception)
        {
            if (exception == null)
                return;

            GetOrCreateLogger().Error(exception, "{Context}: {ExceptionMessage}", context, exception.Message);
        }

        /// <summary>
        /// Debug-level trace for engine decision points (POU skip/reject reasons, duplicate-type
        /// resolution, suite discovery), so a failing suite's code path can be reconstructed
        /// from the log without re-instrumenting. Reaches the log file only when
        /// <c>XSTUNIT_VERBOSE</c> is set; otherwise it costs a single level check.
        /// </summary>
        /// <param name="messageTemplate">Serilog message template, with <c>{Named}</c> holes.</param>
        /// <param name="propertyValues">Values for the template's holes, in order.</param>
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

        // Pure so the env-var toggle rule can be unit tested without touching Serilog or file I/O.
        internal static LogEventLevel ResolveMinimumLevel(string verboseEnvValue) =>
            IsTruthy(verboseEnvValue) ? LogEventLevel.Debug : LogEventLevel.Information;

        internal static string ResolveLogDirectory(string logDirEnvValue) =>
            string.IsNullOrWhiteSpace(logDirEnvValue)
                ? Path.Combine(Path.GetTempPath(), "xStunit", "logs")
                : logDirEnvValue;

        // Forces the next LogException/LogDebug call to rebuild the logger from the current env
        // vars instead of the cached one. Disposes first so the file sink releases its handle -
        // a test that deletes its temp log directory immediately after would otherwise race it.
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
                            Path.Combine(logDirectory, "xstunit-.log"),
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
