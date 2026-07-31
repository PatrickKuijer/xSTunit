using System;
using System.IO;
using Serilog.Events;
using xStunit.Interpreter.Logging;
using Xunit;

namespace xStunit.Interpreter.Tests.Logging
{
    public class XstunitLogTests
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
            Assert.Equal(expected, XstunitLog.IsTruthy(value));
        }

        [Theory]
        [InlineData(null, LogEventLevel.Information)]
        [InlineData("0", LogEventLevel.Information)]
        [InlineData("1", LogEventLevel.Debug)]
        [InlineData("true", LogEventLevel.Debug)]
        public void ResolveMinimumLevel_TogglesOnVerboseEnvVar(string verboseEnvValue, LogEventLevel expected)
        {
            Assert.Equal(expected, XstunitLog.ResolveMinimumLevel(verboseEnvValue));
        }

        [Fact]
        public void ResolveLogDirectory_DefaultsUnderTempWhenUnset()
        {
            var resolved = XstunitLog.ResolveLogDirectory(null);

            Assert.Equal(Path.Combine(Path.GetTempPath(), "xStunit", "logs"), resolved);
        }

        [Fact]
        public void ResolveLogDirectory_HonorsOverride()
        {
            var resolved = XstunitLog.ResolveLogDirectory(@"C:\somewhere\logs");

            Assert.Equal(@"C:\somewhere\logs", resolved);
        }

        [Fact]
        public void LogException_WritesFullExceptionToLogFile()
        {
            var logDir = Path.Combine(Path.GetTempPath(), "xStunitTests", Guid.NewGuid().ToString("N"));
            Environment.SetEnvironmentVariable("XSTUNIT_LOG_DIR", logDir);
            Environment.SetEnvironmentVariable("XSTUNIT_VERBOSE", "1");
            XstunitLog.ResetForTests();
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

                XstunitLog.LogException("unit test context", thrown);
                // Dispose the logger (closing its file sink) before reading the file back -
                // Serilog's file sink keeps its own handle open between writes, so reading
                // through a separate handle immediately after LogException can race it.
                XstunitLog.ResetForTests();

                var logFile = Assert.Single(Directory.GetFiles(logDir, "xstunit-*.log"));
                var contents = File.ReadAllText(logFile);

                Assert.Contains("unit test context", contents);
                Assert.Contains("boom - marker-9f3c", contents);
                // Full ex.ToString() (message + stack trace), not just ex.Message - the whole
                // point of TcXunit-2v8 is not discarding the stack trace.
                Assert.Contains("at ", contents);
            }
            finally
            {
                Environment.SetEnvironmentVariable("XSTUNIT_LOG_DIR", null);
                Environment.SetEnvironmentVariable("XSTUNIT_VERBOSE", null);
                XstunitLog.ResetForTests();
                if (Directory.Exists(logDir))
                    Directory.Delete(logDir, recursive: true);
            }
        }

        [Fact]
        public void LogException_IgnoresNullException()
        {
            // Must not throw - call sites pass whatever they caught.
            XstunitLog.LogException("context", null);
        }
    }
}
