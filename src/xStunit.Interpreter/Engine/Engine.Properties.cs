using System;
using System.Linq;

namespace xStunit.Interpreter
{
    // TwinCAT PROPERTY (Get/Set) dispatch (TcXunit-sxv): a property is
    // neither a Field (no Cell materialized at NewInstance time, TcPouParser
    // never contributed one) nor a Method (no VAR_INPUT/OUTPUT param list to
    // bind) - it's invoked by running its accessor body in a fresh Frame and
    // reading/seeding a Local named after the property itself, mirroring the
    // existing METHOD return-value convention (CallMethod reads
    // newFrame.Locals[methodName] the same way).
    public sealed partial class Engine
    {
        // Ancestry walk identical in shape to CallMethod's method lookup -
        // first declaring type in the EXTENDS chain (starting at
        // startType) whose Properties list has a matching name wins.
        private bool TryFindProperty(
            string startType,
            string propertyName,
            out string definingType,
            out xStunit.Parser.PropertyAst property)
        {
            var type = startType;
            while (type != null)
            {
                var def = _registry.Get(type);
                if (def == null)
                    break;

                var found = def.Properties.FirstOrDefault(p => p.Name == propertyName);
                if (found != null)
                {
                    definingType = type;
                    property = found;
                    return true;
                }

                type = def.BaseTypeName;
            }

            definingType = null;
            property = null;
            return false;
        }

        private object InvokePropertyGet(FbInstance instance, string definingType, xStunit.Parser.PropertyAst property)
        {
            if (!property.HasGet)
                throw new InvalidOperationException($"Property '{property.Name}' has no Get accessor");

            var frame = new Frame(instance, definingType);

            // TcXunit-8we: same defect and same fix as CallMethod/
            // CallGlobalFunction's SeedReturnCell (TcXunit-cq6) - the Get
            // accessor's return value lives in a Local named after the
            // property, left to be created lazily by its first assignment.
            // A PROPERTY nGain : LREAL opening its Get with 'nGain := 0.0;'
            // (a bare decimal literal, which lexes as REAL) got a REAL cell,
            // and any later LREAL assignment into it was rejected as an
            // implicit narrowing.
            SeedReturnCell(frame, property.Name, property.DeclarationText);

            // TcXunit-n65: routed through ExecuteBody (rather than a local
            // try/catch(MethodReturnSignal)) so a lazy parse failure in the
            // Get accessor's own body is attributed to this property's
            // frame, not to whatever caller's frame is still on the stack.
            ExecuteBody(() => _registry.GetStatements(property.GetImplementationText), frame);

            return frame.Locals.TryGetValue(property.Name, out var returnCell) ? returnCell.Value : null;
        }

        private void InvokePropertySet(FbInstance instance, string definingType, xStunit.Parser.PropertyAst property, object value)
        {
            if (!property.HasSet)
                throw new InvalidOperationException($"Property '{property.Name}' has no Set accessor");

            var frame = new Frame(instance, definingType);

            // TcXunit-8we: tag the seeded Cell with the property's declared
            // type, same rule (IEC numeric types only) as SeedReturnCell -
            // the incoming value itself is kept as-is (unlike the Get side,
            // there's no default-zero to seed with here), but leaving
            // DeclaredTypeName null left this cell an outlier among every
            // other Cell construction site in the interpreter (fields,
            // params, locals) and meant anything consulting
            // Cell.DeclaredTypeName (e.g. SIZEOF) about this Local couldn't
            // see it.
            var cell = new Cell { Value = value };
            if (TryGetNumericCallableType(property.DeclarationText, out var declaredType, out _))
            {
                cell.DeclaredTypeName = declaredType;
                frame.LocalTypeNames[property.Name] = declaredType;
            }
            frame.Locals[property.Name] = cell;

            // TcXunit-n65: same reasoning as InvokePropertyGet above.
            ExecuteBody(() => _registry.GetStatements(property.SetImplementationText), frame);
        }
    }
}
