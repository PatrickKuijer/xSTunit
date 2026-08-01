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
    /// on, and a run that produced only such lines is reported as
    /// <see cref="NeedsJsonFallback"/> - the caller's cue to re-run the pre-stream
    /// command line, not a failure to show the user.
    ///
    /// Skipping also covers an event name from a NEWER CLI, and a known name whose
    /// payload this build cannot read: neither may send a run that did stream down the
    /// fallback path and execute the user's suites a second time.
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
        private bool _sawStreamEvent;

        /// <summary>
        /// The finished run, or null while it is still in flight: a streamed run is
        /// complete only once its summary (or its error) line has arrived, never at the
        /// last suite-result.
        /// </summary>
        public XstunitRunResult Result { get; private set; }

        /// <summary>
        /// True when the CLI wrote output and not one line of it was a stream event,
        /// which is what a CLI predating --stream looks like from here.
        /// </summary>
        /// <remarks>
        /// False for a CLI that wrote nothing at all: that is a broken invocation - a
        /// missing executable, say - and re-running it would only fail a second time.
        ///
        /// False, too, once any event has arrived, however the run then ends: retrying
        /// is a second full execution of the user's suites, so it is offered only for
        /// the one case it answers - a CLI that cannot stream at all. See
        /// <see cref="EndedMidStream"/>.
        /// </remarks>
        public bool NeedsJsonFallback => this.Result == null && this._sawOutput && !this._sawStreamEvent;

        /// <summary>
        /// True when the CLI streamed events and then stopped without ever reaching its
        /// summary or error line - a run that failed partway, and reported as one.
        /// </summary>
        public bool EndedMidStream => this.Result == null && this._sawStreamEvent;

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

            switch (ReadEventName(line))
            {
                case XstunitStreamEventNames.Discovery:
                    return this.Read<XstunitDiscoveryEvent>(line);
                case XstunitStreamEventNames.SuiteStart:
                    return this.Read<XstunitSuiteStartEvent>(line);
                case XstunitStreamEventNames.SuiteResult:
                    return this.Read<XstunitSuiteResultEvent>(line);
                case XstunitStreamEventNames.Summary:
                    return this.Complete(this.Read<XstunitSummaryEvent>(line), line);
                case XstunitStreamEventNames.Error:
                    return this.Complete(this.Read<XstunitErrorEvent>(line), line);
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

        // The name is recorded before the payload is read, and whether or not it reads:
        // an event name this build knows is already proof the CLI speaks --stream, which
        // is the one question the fallback turns on.
        private TEvent Read<TEvent>(string line)
            where TEvent : class, IXstunitStreamEvent
        {
            this._sawStreamEvent = true;

            try
            {
                return JsonSerializer.Deserialize<TEvent>(line, SerializerOptions);
            }
            catch (JsonException)
            {
                return null;
            }
        }

        private IXstunitStreamEvent Complete<TEvent>(TEvent completed, string line)
            where TEvent : XstunitRunResult, IXstunitStreamEvent
        {
            // A payload that would not read leaves the run unfinished rather than
            // completing it with half the counts parsed.
            if (completed == null)
            {
                return null;
            }

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
