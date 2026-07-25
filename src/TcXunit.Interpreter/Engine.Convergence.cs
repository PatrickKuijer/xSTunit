using System;
using System.Linq;

namespace TcXunit.Interpreter
{
    public sealed partial class Engine
    {
        // FbInstance.StepCycles(n) - re-invokes the instance's top-level body n
        // times, reusing the instance's existing Cell state across calls (same
        // persistence CallMethod relies on). No dt/scheduler: caller controls
        // ordering across multiple instances by choosing call order.
        private void StepCycles(FbInstance instance, int cycles)
        {
            var def = _registry.Get(instance.ActualTypeName);
            if (def == null)
                throw new InvalidOperationException($"Type '{instance.ActualTypeName}' not found for StepCycles");

            var statements = Parser.ParseStatements(def.ImplementationText);
            for (var i = 0; i < cycles; i++)
            {
                var frame = new Frame(instance, instance.ActualTypeName);
                try
                {
                    ExecuteStatements(statements, frame);
                }
                catch (MethodReturnSignal)
                {
                    // A top-level RETURN inside the FB's cyclic body only ends
                    // this cycle; it must not unwind into whatever ST call
                    // (e.g. a TcUnit test method) invoked StepCycles.
                }
            }
        }

        // AssertConverges/AssertConvergesAndLatches (TcXunit-w5x.15.9, T6
        // design): the helper owns the master-then-proxy stepping loop so
        // test authors don't hand-roll polling. Both throw (rather than
        // record a TcUnit-style failure) with a per-field diff, matching
        // T6's "actionable diagnosis" resolution.
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
