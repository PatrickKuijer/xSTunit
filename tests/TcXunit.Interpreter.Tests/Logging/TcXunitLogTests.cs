using System;
using System.IO;
using Serilog.Events;
using TcXunit.Interpreter.Logging;
using Xunit;

namespace TcXunit.Interpreter.Tests.Logging
{
    public class TcXunitLogTests
    {
        [Theory]
        [InlineData(null, false)]
        [InlineData("", false)]
        [InlineData("0", false)]
        [InlineData("false", false)]
        [InlineData("1", true)]
        [InlineData("true", true)]
        [InlineData("TRUE", true)]
        [InlineData("yes", true)]
        public void IsTruthy_RecognizesExpectedVerboseValues(string value, bool expected)
        {
            Assert.Equal(expected, TcXunitLog.IsTruthy(value));
        }

        [Theory]
        [InlineData(null, LogEventLevel.Information)]
        [InlineData("0", LogEventLevel.Information)]
        [InlineData("1", LogEventLevel.Debug)]
        [InlineData("true", LogEventLevel.Debug)]
        public void ResolveMinimumLevel_TogglesOnVerboseEnvVar(string verboseEnvValue, LogEventLevel expected)
        {
            Assert.Equal(expected, TcXunitLog.ResolveMinimumLevel(verboseEnvValue));
        }

        [Fact]
        public void ResolveLogDirectory_DefaultsUnderTempWhenUnset()
        {
            var resolved = TcXunitLog.ResolveLogDirectory(null);

            Assert.Equal(Path.Combine(Path.GetTempPath(), "TcXunit", "logs"), resolved);
        }

        [Fact]
        public void ResolveLogDirectory_HonorsOverride()
        {
            var resolved = TcXunitLog.ResolveLogDirectory(@"C:\somewhere\logs");

            Assert.Equal(@"C:\somewhere\logs", resolved);
        }

        [Fact]
        public void LogException_WritesFullExceptionToLogFile()
        {
            var logDir = Path.Combine(Path.GetTempPath(), "TcXunitTests", Guid.NewGuid().ToString("N"));
            Environment.SetEnvironmentVariable("TCXUNIT_LOG_DIR", logDir);
            Environment.SetEnvironmentVariable("TCXUNIT_VERBOSE", "1");
            TcXunitLog.ResetForTests();
            try
            {
                Exception thrown;
                try
                {
                    throw new InvalidOperationException("boom - marker-9f3c");
                }
                catch (Exception ex)
                {
                    thrown = ex;
                }

                TcXunitLog.LogException("unit test context", thrown);
                // Dispose the logger (closing its file sink) before reading the file back -
                // Serilog's file sink keeps its own handle open between writes, so reading
                // through a separate handle immediately after LogException can race it.
                TcXunitLog.ResetForTests();

                var logFile = Assert.Single(Directory.GetFiles(logDir, "tcxunit-*.log"));
                var contents = File.ReadAllText(logFile);

                Assert.Contains("unit test context", contents);
                Assert.Contains("boom - marker-9f3c", contents);
                // Full ex.ToString() (message + stack trace), not just ex.Message - the whole
                // point of TcXunit-2v8 is not discarding the stack trace.
                Assert.Contains("at ", contents);
            }
            finally
            {
                Environment.SetEnvironmentVariable("TCXUNIT_LOG_DIR", null);
                Environment.SetEnvironmentVariable("TCXUNIT_VERBOSE", null);
                TcXunitLog.ResetForTests();
                if (Directory.Exists(logDir))
                    Directory.Delete(logDir, recursive: true);
            }
        }

        [Fact]
        public void LogException_IgnoresNullException()
        {
            // Must not throw - call sites pass whatever they caught.
            TcXunitLog.LogException("context", null);
        }
    }
}
