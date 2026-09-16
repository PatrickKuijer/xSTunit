using System;
using System.Linq;

namespace xStunit.Interpreter
{
    public sealed partial class Engine
    {
        // Re-invokes the instance's top-level body n times, reusing its existing
        // Cell state across calls. There is no dt and no scheduler: ordering
        // across several instances is whatever order the caller invokes them in.
        private void StepCycles(FbInstance instance, int cycles)
        {
            var def = _registry.Get(instance.ActualTypeName);
            if (def == null)
                throw new InvalidOperationException($"Type '{instance.ActualTypeName}' not found for StepCycles");

            for (var i = 0; i < cycles; i++)
            {
                // Every cycle, not just once at instantiation.
                ResetTopLevelTempFields(instance);

                // Latched here and nowhere else: the task start is a property
                // of the cycle, so a body that advances the clock part-way
                // through moves "now" without moving the time this cycle began.
                Clock.BeginCycle();

                // ExecuteBody owns the rule that a top-level RETURN in the FB's
                // cyclic body ends only this cycle and must not unwind into
                // whatever ST call invoked StepCycles, plus fault attribution.
                //
                // GetStatements stays inside the lambda rather than being
                // hoisted out of the loop, so a lazy parse failure in this
                // instance's body attributes to this instance's own frame rather
                // than to StepCycles' caller. TypeRegistry.GetStatements caches
                // by body text, so the per-cycle call is a dictionary lookup,
                // not a re-parse.
                ExecuteBody(() => _registry.GetStatements(def.ImplementationText), new Frame(instance, instance.ActualTypeName, null, def.BodyStartLine));
            }
        }

        // Owns the master-then-proxy stepping loop so test authors don't
        // hand-roll polling. Throws a per-field diff rather than recording a
        // TcUnit-style failure; see ConvergenceAssertionException.
        private void AssertConverges(FbInstance master, FbInstance proxy, string[] fieldNames, int maxCycles)
        {
            for (var i = 1; i <= maxCycles; i++)
            {
                StepCycles(master, 1);
                StepCycles(proxy, 1);
                if (FieldsConverged(master, proxy, fieldNames))
                    return;
            }

            throw new ConvergenceAssertionException(
                $"AssertConverges: fields did not converge within {maxCycles} cycles: {FieldDiff(master, proxy, fieldNames)}");
        }

        // "Flips exactly once and stays latched": once fieldNames converge,
        // they must stay converged for every remaining cycle; diverging
        // again after latching is a failure, same as never latching at all.
        private void AssertConvergesAndLatches(FbInstance master, FbInstance proxy, string[] fieldNames, int maxCycles)
        {
            var latchedAtCycle = -1;

            for (var i = 1; i <= maxCycles; i++)
            {
                StepCycles(master, 1);
                StepCycles(proxy, 1);
                var converged = FieldsConverged(master, proxy, fieldNames);

                if (latchedAtCycle >= 0 && !converged)
                    throw new ConvergenceAssertionException(
                        $"AssertConvergesAndLatches: fields converged at cycle {latchedAtCycle} but diverged again at cycle {i}: {FieldDiff(master, proxy, fieldNames)}");

                if (converged && latchedAtCycle < 0)
                    latchedAtCycle = i;
            }

            if (latchedAtCycle < 0)
                throw new ConvergenceAssertionException(
                    $"AssertConvergesAndLatches: fields never converged within {maxCycles} cycles: {FieldDiff(master, proxy, fieldNames)}");
        }

        private static bool FieldsConverged(FbInstance master, FbInstance proxy, string[] fieldNames) =>
            fieldNames.All(name => Equals(FieldValue(master, name), FieldValue(proxy, name)));

        private static string FieldDiff(FbInstance master, FbInstance proxy, string[] fieldNames) =>
            string.Join("; ", fieldNames
                .Where(name => !Equals(FieldValue(master, name), FieldValue(proxy, name)))
                .Select(name => $"{name}: master={FieldValue(master, name)}, proxy={FieldValue(proxy, name)}"));

        private static object FieldValue(FbInstance instance, string fieldName)
        {
            if (!instance.Fields.TryGetValue(fieldName, out var cell))
                throw new InvalidOperationException($"Field '{fieldName}' not found on type '{instance.ActualTypeName}'");
            return cell.Value;
        }

        private static string[] ToStringArray(object value)
        {
            if (value is ArrayValue array)
                return array.Elements.Select(e => (string)e).ToArray();

            throw new NotSupportedException($"Expected an array of field names, got {value?.GetType().Name ?? "null"}");
        }
    }
}
