using System;
using System.Collections.Generic;

namespace TcXunit.Interpreter
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
                var statements = statementsFactory();
                ExecuteStatements(statements, frame);
            }
            catch (MethodReturnSignal)
            {
                // RETURN ends this body only - it must not unwind into the
                // caller (a top-level RETURN in an FB's cyclic body ends that
                // cycle, not the ST call that invoked it).
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

        // Keys on Exception.Data rather than an Engine field: the annotation
        // then travels with the exception itself, so nothing has to be reset
        // between runs and a swallowed exception (e.g. the GVL default-value
        // retry loop in Engine's constructor) can't leave stale state behind.
        private const string FaultPouTypeKey = "TcXunit.Interpreter.FaultPouTypeName";
        private const string FaultMethodKey = "TcXunit.Interpreter.FaultMethodName";

        // TcXunit-p3t.4: the .TcPOU file line, written under the same
        // first-writer-wins rule as the two keys above, which is what makes it
        // the INNERMOST frame's line for free - no separate bookkeeping and no
        // way for the two halves of a location to come from different frames.
        private const string FaultLineKey = "TcXunit.Interpreter.FaultLine";

        // TcXunit-gfs: the XAE-implementation-editor-relative line - the
        // second already-known number (Frame.CurrentLine) this change
        // surfaces, stamped at the same point and under the same
        // first-writer-wins rule as FaultLineKey so both numbers always come
        // from the same (innermost) frame.
        private const string FaultBodyLineKey = "TcXunit.Interpreter.FaultBodyLine";

        private static void RecordFaultSite(Exception ex, Frame frame)
        {
            // MethodReturnSignal/LoopExitSignal are unwind *signals*, not
            // faults - annotating (and later wrapping) them would break RETURN
            // and EXIT semantics outright.
            if (IsControlFlowSignal(ex) || ex.Data == null || ex.Data.IsReadOnly)
                return;

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

            return new PlcSourceLocationException(pouTypeName, ex.Data[FaultMethodKey] as string, fileLine, bodyLine, ex);
        }

        private static int RecordedLine(Exception ex, string key) =>
            ex.Data[key] is int recordedLine ? recordedLine : PlcSourceLocationException.UnknownLine;
    }
}
