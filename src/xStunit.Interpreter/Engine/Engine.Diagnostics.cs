using System;
using System.Collections.Generic;
using System.Linq;
using xStunit.Runner.TcUnitStub;

namespace xStunit.Interpreter
{
    public sealed partial class Engine
    {
        // TcXunit-p3t.1: single entry point for "run a POU/METHOD body in its
        // own frame". Every site that used to hand-roll
        // `try { ExecuteStatements(...) } catch (MethodReturnSignal) {}` -
        // CallMethod, InvokeFbInstance, StepCycles, RunSuite - goes through
        // here so exactly one place knows how to attribute a fault to the PLC
        // body that raised it.
        //
        // The interpreter has no call stack of its own to walk after the fact
        // (the only stack is raw CLR recursion through
        // ExecuteStatements/ExecuteStatement/Evaluate), and by the time an
        // exception reaches the suite boundary every intermediate frame has
        // already unwound. So instead of reconstructing a stack, each body
        // stamps its identity onto the in-flight exception on the way out -
        // and only if nothing has stamped it yet, which makes the INNERMOST
        // body the one that wins, exactly as an acceptance criterion requires.
        //
        // TcXunit-n65: statementsFactory is a *lazy* lookup
        // (`() => _registry.GetStatements(text)`), not a resolved
        // IReadOnlyList<Stmt>, and it is called from INSIDE this method's own
        // try - not by the caller before ExecuteBody is even entered. A
        // callee's ImplementationText is parsed lazily on first use
        // (TypeRegistry.GetStatements), and if that parse itself throws (e.g.
        // a FormatException from the lexer/parser on an unsupported
        // construct), the fault happens while THIS frame - the callee's own -
        // is the innermost one on the stack, so it gets attributed here
        // rather than bubbling out unattributed to whichever caller's
        // ExecuteBody is further up the CLR stack.
        private void ExecuteBody(Func<IReadOnlyList<Stmt>> statementsFactory, Frame frame)
        {
            try
            {
                RunWithFaultAttribution(
                    () => ExecuteStatements(statementsFactory(), frame),
                    frame);
            }
            catch (MethodReturnSignal)
            {
                // RETURN ends this body only - it must not unwind into the
                // caller (a top-level RETURN in an FB's cyclic body ends that
                // cycle, not the ST call that invoked it). RecordFaultSite
                // ignores control-flow signals, so passing through the wrapper
                // above leaves this signal unannotated as before.
            }
        }

        // TcXunit-3tx.3: ExecuteBody's suite-body variant. A suite body is the
        // one body where a fault does NOT have to be fatal to everything after
        // it: if a TEST()/TEST_FINISHED() bracket was open when the fault
        // escaped, that test owns the failure and the remaining top-level
        // statements - the suite's other tests - can still run. Before this,
        // one unsupported construct in one test method reported the whole suite
        // as `tests: []`, `passed: 0`, which reads to a consuming agent as
        // "your change broke everything".
        //
        // A fault with no open bracket has no test to charge and still takes
        // the suite down, unchanged: it happened in setup or between tests, so
        // nothing after it can be trusted anyway.
        private void ExecuteSuiteBody(Func<IReadOnlyList<Stmt>> statementsFactory, Frame frame, SuiteHost host)
        {
            IReadOnlyList<Stmt> statements;
            try
            {
                statements = statementsFactory();
            }
            catch (Exception ex)
            {
                // A lazy parse failure of the suite's own body: nothing ran, so
                // there is no per-test recovery to attempt (TcXunit-n65).
                RecordFaultSite(ex, frame);
                throw;
            }

            // Set after a fault that was raised DIRECTLY in this body (rather
            // than unwinding out of a called method): the statements that
            // follow are the rest of the aborted test's own bracket, so they
            // are skipped until the next TEST()/TEST_ORDERED() opens a new one.
            // For the far more common one-METHOD-per-test shape this never
            // engages - the abandoned statements were the callee's, and the
            // next top-level statement is already the next test.
            var skippingAbortedTest = false;

            foreach (var stmt in statements)
            {
                if (skippingAbortedTest)
                {
                    if (!OpensTestBracket(stmt))
                        continue;
                    skippingAbortedTest = false;
                }

                try
                {
                    ExecuteStatement(stmt, frame);
                }
                catch (MethodReturnSignal)
                {
                    // Same contract as ExecuteBody: RETURN ends this body only.
                    return;
                }
                catch (Exception ex)
                {
                    // Whether any inner body already claimed this fault is what
                    // separates the two suite shapes, and it has to be read
                    // BEFORE this frame appends itself below.
                    var raisedInThisBody = !HasRecordedCallStack(ex);
                    RecordFaultSite(ex, frame);

                    if (!host.HasOpenTestCase)
                        throw;

                    host.AbortCurrentTestCase(ToTestFailure(ex));
                    skippingAbortedTest = raisedInThisBody;
                }
            }
        }

        // Renders an escaping fault as the failure line of the test it is
        // charged to (TcXunit-3tx.3), reusing the same location rendering and
        // the same FailureKind vocabulary a suite-level error gets - a fault is
        // no less a fault for having been contained.
        private static AssertionFailure ToTestFailure(Exception ex)
        {
            var located = TryCreateSourceLocationException(ex);
            var kind = FailureClassifier.Classify(located ?? ex, out var construct);

            // TcXunit-3tx.2/.3: a contained fault has no expected/actual pair -
            // it never got as far as comparing anything - but it knows exactly
            // as much about WHERE as the suite-level error it replaces did, and
            // must carry all of it: the innermost frame plus the full chain
            // (TcXunit-7s6/1am). Containing a fault must not cost the
            // diagnostics that made it debuggable.
            var site = located != null
                ? ToAssertSite(located.CallStack[0])
                : default(AssertSite);

            // xstunit-fpw8: a parse failure raised while lazily parsing a
            // callee's body (TcXunit-n65) is attributed to that callee's OWN
            // frame, which never ran a statement of its own - so the
            // interpreter's own line tracking (the source of `site` above)
            // is genuinely unknown here. The front end's own ParseException
            // carries a real line in that case; fold it in rather than
            // leaving a parse-error's bodyLine null when a better answer
            // already exists.
            if (kind == xStunit.Runner.FailureKind.ParseError && site.BodyLine == PlcSourceLocationException.UnknownLine
                && FailureClassifier.UnwrapParseException(located ?? ex) is ParseException parseFailure
                && parseFailure.BodyLine != PlcSourceLocationException.UnknownLine)
            {
                site = new AssertSite(site.PouTypeName, site.MethodName, site.Line, parseFailure.BodyLine);
            }

            var callStack = located?.CallStack.Select(ToAssertSite).ToArray();

            return AssertionFailure.Fault((located ?? ex).Message, kind, construct, site, callStack);
        }

        private static AssertSite ToAssertSite(PlcCallStackFrame frame) =>
            new AssertSite(frame.PouTypeName, frame.MethodName, frame.Line, frame.BodyLine);

        // Whether some inner ExecuteBody already appended a frame, i.e. the
        // fault unwound out of a called body rather than being raised by the
        // statement this frame just ran.
        private static bool HasRecordedCallStack(Exception ex) =>
            ex.Data != null && ex.Data[FaultCallStackKey] is List<PlcCallStackFrame> frames && frames.Count > 0;

        // A top-level statement that opens a new TEST()/TEST_ORDERED() bracket:
        // the resume point after a test was aborted mid-bracket. Receiverless
        // by construction - inside a suite body these are the suite's own
        // inherited TcUnit calls, never a call on some other instance.
        private static bool OpensTestBracket(Stmt stmt) =>
            stmt is ExprStmt exprStmt &&
            exprStmt.Call.Receiver == null &&
            (exprStmt.Call.MethodName == "TEST" || exprStmt.Call.MethodName == "TEST_ORDERED");

        // Keys on Exception.Data rather than an Engine field: the annotation
        // then travels with the exception itself, so nothing has to be reset
        // between runs and a swallowed exception (e.g. the GVL default-value
        // retry loop in Engine's constructor) can't leave stale state behind.
        private const string FaultPouTypeKey = "xStunit.Interpreter.FaultPouTypeName";
        private const string FaultMethodKey = "xStunit.Interpreter.FaultMethodName";

        // TcXunit-p3t.4: the .TcPOU file line, written under the same
        // first-writer-wins rule as the two keys above, which is what makes it
        // the INNERMOST frame's line for free - no separate bookkeeping and no
        // way for the two halves of a location to come from different frames.
        private const string FaultLineKey = "xStunit.Interpreter.FaultLine";

        // TcXunit-gfs: the XAE-implementation-editor-relative line - the
        // second already-known number (Frame.CurrentLine) this change
        // surfaces, stamped at the same point and under the same
        // first-writer-wins rule as FaultLineKey so both numbers always come
        // from the same (innermost) frame.
        private const string FaultBodyLineKey = "xStunit.Interpreter.FaultBodyLine";

        // TcXunit-1am: unlike the four keys above (first-writer-wins, so they
        // always describe the innermost frame), this list gets a frame
        // appended by EVERY ExecuteBody level the exception passes through -
        // the innermost body appends first (it catches first), each caller's
        // ExecuteBody appends next as the exception keeps unwinding outward,
        // and RunSuite's own ExecuteBody (the outermost) appends last. That
        // unwind order is exactly the "innermost first, suite entry point
        // last" order TryCreateSourceLocationException hands to
        // PlcSourceLocationException - no sorting needed.
        private const string FaultCallStackKey = "xStunit.Interpreter.FaultCallStack";

        // TcXunit-8ldh: the one shape shared by ExecuteBody and BindParams's
        // default-value construction - "run this, and if it faults charge the
        // fault to `frame` on the way out". Both are pure attribution: they add
        // a frame to the in-flight exception and change nothing else about it.
        //
        // ExecuteSuiteBody's catches are deliberately NOT expressed in terms of
        // this: they look the same but also decide whether the fault can be
        // charged to an open test case and how much of the body to skip
        // afterwards, which is a different fact about a different boundary.
        private static void RunWithFaultAttribution(Action action, Frame frame)
        {
            RunWithFaultAttribution<object>(() => { action(); return null; }, frame);
        }

        private static T RunWithFaultAttribution<T>(Func<T> action, Frame frame)
        {
            try
            {
                return action();
            }
            catch (Exception ex)
            {
                RecordFaultSite(ex, frame);

                // Bare rethrow, not `throw ex`: the original exception - type,
                // message and CLR stack trace - reaches the caller untouched.
                // Wrapping deliberately does NOT happen here; see
                // TryCreateSourceLocationException.
                throw;
            }
        }

        private static void RecordFaultSite(Exception ex, Frame frame)
        {
            // MethodReturnSignal/LoopExitSignal are unwind *signals*, not
            // faults - annotating (and later wrapping) them would break RETURN
            // and EXIT semantics outright.
            if (IsControlFlowSignal(ex) || ex.Data == null || ex.Data.IsReadOnly)
                return;

            AppendCallStackFrame(ex, frame);

            // First writer wins == innermost body wins: outer frames see the
            // key already present and leave it alone as the exception passes.
            if (ex.Data.Contains(FaultPouTypeKey))
                return;

            ex.Data[FaultPouTypeKey] = frame.DeclaringTypeName;
            ex.Data[FaultMethodKey] = frame.MethodName;

            // Already translated to a file line by the frame, so the
            // BodyStartLine + Line - 1 arithmetic lives in exactly one place
            // (Frame.CurrentFileLine) and nothing downstream can re-apply it.
            ex.Data[FaultLineKey] = frame.CurrentFileLine;
            ex.Data[FaultBodyLineKey] = frame.CurrentLine;
        }

        private static void AppendCallStackFrame(Exception ex, Frame frame)
        {
            if (!(ex.Data[FaultCallStackKey] is List<PlcCallStackFrame> callStack))
            {
                callStack = new List<PlcCallStackFrame>();
                ex.Data[FaultCallStackKey] = callStack;
            }

            callStack.Add(new PlcCallStackFrame(frame.DeclaringTypeName, frame.MethodName, frame.CurrentFileLine, frame.CurrentLine));
        }

        private static bool IsControlFlowSignal(Exception ex) =>
            ex is MethodReturnSignal || ex is LoopExitSignal;

        // Wraps ex in a PlcSourceLocationException iff some interpreted body
        // claimed it, otherwise returns null so the caller can `throw;` the
        // original untouched (a load/parse/host failure that never entered an
        // ST body has no PLC location to report).
        //
        // Called only from the outermost boundary - RunSuite - so the public
        // Engine.CallMethod contract is unchanged and callers that switch on
        // concrete interpreter exception types keep working.
        private static PlcSourceLocationException TryCreateSourceLocationException(Exception ex)
        {
            if (ex is PlcSourceLocationException || ex.Data == null || !ex.Data.Contains(FaultPouTypeKey))
                return null;

            var pouTypeName = ex.Data[FaultPouTypeKey] as string;
            if (pouTypeName == null)
                return null;

            // TcXunit-p3t.4/gfs: a fault stamped by an older/hand-built path may
            // carry no line at all, hence the UnknownLine fallback rather than
            // an unchecked unbox - the rendering then degrades to exactly the
            // POU.Method shape p3t.1 produced.
            var fileLine = RecordedLine(ex, FaultLineKey);
            var bodyLine = RecordedLine(ex, FaultBodyLineKey);
            var callStack = ex.Data[FaultCallStackKey] as List<PlcCallStackFrame>;

            return new PlcSourceLocationException(pouTypeName, ex.Data[FaultMethodKey] as string, fileLine, bodyLine, callStack, ex);
        }

        private static int RecordedLine(Exception ex, string key) =>
            ex.Data[key] is int recordedLine ? recordedLine : PlcSourceLocationException.UnknownLine;
    }
}
