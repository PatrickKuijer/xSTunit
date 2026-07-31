using System;
using System.Linq;

namespace xStunit.Interpreter
{
    // TwinCAT PROPERTY (Get/Set) dispatch. A property is neither a Field (no
    // Cell is materialized for it at NewInstance time) nor a Method (there is
    // no VAR_INPUT/OUTPUT param list to bind), so it is invoked by running its
    // accessor body in a fresh Frame and reading or seeding a Local named after
    // the property itself - the same convention CallMethod uses for a METHOD's
    // return value.
    public sealed partial class Engine
    {
        // Same ancestry walk as CallMethod's method lookup: the first type in
        // the EXTENDS chain declaring a property of this name wins.
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

            // Without this the Local holding the return value is created by its
            // first assignment, which decides its type: a PROPERTY nGain : LREAL
            // opening its Get with 'nGain := 0.0;' gets a REAL cell (a bare
            // decimal literal lexes as REAL), and every later LREAL assignment
            // into it is then rejected as an implicit narrowing. Same hazard
            // CallMethod/CallGlobalFunction seed against.
            SeedReturnCell(frame, property.Name, property.DeclarationText);

            // ExecuteBody rather than a local try/catch(MethodReturnSignal), so
            // a parse failure in the Get accessor's own body is attributed to
            // this property's frame instead of the caller's.
            ExecuteBody(() => _registry.GetStatements(property.GetImplementationText), frame);

            return frame.Locals.TryGetValue(property.Name, out var returnCell) ? returnCell.Value : null;
        }

        private void InvokePropertySet(FbInstance instance, string definingType, xStunit.Parser.PropertyAst property, object value)
        {
            if (!property.HasSet)
                throw new InvalidOperationException($"Property '{property.Name}' has no Set accessor");

            var frame = new Frame(instance, definingType);

            // The incoming value is kept as-is - unlike the Get side there is no
            // default zero to seed - but the Cell still gets the property's
            // declared type under SeedReturnCell's rule (IEC numeric types
            // only), so that anything consulting Cell.DeclaredTypeName about
            // this Local, SIZEOF among them, can see it.
            var cell = new Cell { Value = value };
            if (TryGetNumericCallableType(property.DeclarationText, out var declaredType, out _))
            {
                cell.DeclaredTypeName = declaredType;
                frame.LocalTypeNames[property.Name] = declaredType;
            }
            frame.Locals[property.Name] = cell;

            ExecuteBody(() => _registry.GetStatements(property.SetImplementationText), frame);
        }
    }
}
