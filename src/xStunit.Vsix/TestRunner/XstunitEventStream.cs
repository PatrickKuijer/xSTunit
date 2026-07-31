using System.Text.Json;

namespace xStunit.Vsix.TestRunner
{
    /// <summary>
    /// Folds the NDJSON lines of `xstunit --stream` into the same
    /// <see cref="XstunitRunResult"/> the pre-stream `--format json` blob deserializes
    /// to, classifying each line as it arrives.
    /// </summary>
    /// <remarks>
    /// Lines arrive from a process's stdout, so nothing about them is trusted: a CLI
    /// too old to know --stream reads that flag as a directory name and prints one
    /// plain-text error instead. Anything unrecognized is skipped rather than thrown
    /// on, and a run that ends without ever producing a summary or error line is
    /// reported as <see cref="NeedsJsonFallback"/> - the caller's cue to re-run the
    /// pre-stream command line, not a failure to show the user.
    ///
    /// Skipping also covers an event name from a NEWER CLI: an unknown line must not
    /// send an otherwise healthy run down the fallback path and run everything twice.
    /// </remarks>
    internal sealed class XstunitEventStream
    {
        // XstunitModels.cs's properties are PascalCase; the CLI's JSON is camelCase.
        // Shared with XstunitProcessRunner's fallback so both paths read the CLI's keys
        // by the same rules (XstunitModelsDeserializationTests asserts against a
        // matching option set).
        public static readonly JsonSerializerOptions SerializerOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
        };

        private bool _sawOutput;

        /// <summary>
        /// The finished run, or null while it is still in flight: a streamed run is
        /// complete only once its summary (or its error) line has arrived, never at the
        /// last suite-result.
        /// </summary>
        public XstunitRunResult Result { get; private set; }

        /// <summary>
        /// True when the CLI wrote something this fold could make nothing of, which is
        /// what a CLI predating --stream looks like from here.
        /// </summary>
        /// <remarks>
        /// False for a CLI that wrote nothing at all: that is a broken invocation - a
        /// missing executable, say - and re-running it would only fail a second time.
        /// </remarks>
        public bool NeedsJsonFallback => this.Result == null && this._sawOutput;

        /// <returns>
        /// The event that line carried, or null for a line that is not one - a blank
        /// line, plain text, or an event name this build does not know.
        /// </returns>
        public IXstunitStreamEvent Append(string line)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                return null;
            }

            this._sawOutput = true;

            var eventName = ReadEventName(line);
            switch (eventName)
            {
                case XstunitStreamEventNames.Discovery:
                    return JsonSerializer.Deserialize<XstunitDiscoveryEvent>(line, SerializerOptions);
                case XstunitStreamEventNames.SuiteStart:
                    return JsonSerializer.Deserialize<XstunitSuiteStartEvent>(line, SerializerOptions);
                case XstunitStreamEventNames.SuiteResult:
                    return JsonSerializer.Deserialize<XstunitSuiteResultEvent>(line, SerializerOptions);
                case XstunitStreamEventNames.Summary:
                    return this.Complete(JsonSerializer.Deserialize<XstunitSummaryEvent>(line, SerializerOptions), line);
                case XstunitStreamEventNames.Error:
                    return this.Complete(JsonSerializer.Deserialize<XstunitErrorEvent>(line, SerializerOptions), line);
                default:
                    return null;
            }
        }

        // Read off the raw JSON rather than by deserializing into a common base: which
        // type the line even IS is what is being decided here.
        private static string ReadEventName(string line)
        {
            try
            {
                using (var document = JsonDocument.Parse(line))
                {
                    if (document.RootElement.ValueKind != JsonValueKind.Object ||
                        !document.RootElement.TryGetProperty("event", out var eventProperty) ||
                        eventProperty.ValueKind != JsonValueKind.String)
                    {
                        return null;
                    }

                    return eventProperty.GetString();
                }
            }
            catch (JsonException)
            {
                return null;
            }
        }

        private IXstunitStreamEvent Complete<TEvent>(TEvent completed, string line)
            where TEvent : XstunitRunResult, IXstunitStreamEvent
        {
            // The line verbatim, exactly as the buffered path stashes its whole blob:
            // the WebView2 page is handed the CLI's own JSON rather than a
            // re-serialization of this object, which is what keeps what renders and
            // what the CLI emitted from drifting apart (see XstunitRunResult.RawJson).
            completed.RawJson = line;
            this.Result = completed;
            return completed;
        }
    }
}
