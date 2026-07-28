using System;
using System.Linq;

namespace TcXunit.Interpreter
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
            out TcXunit.Parser.PropertyAst property)
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

        private object InvokePropertyGet(FbInstance instance, string definingType, TcXunit.Parser.PropertyAst property)
        {
            if (!property.HasGet)
                throw new InvalidOperationException($"Property '{property.Name}' has no Get accessor");

            var frame = new Frame(instance, definingType);
            try
            {
                ExecuteStatements(_registry.GetStatements(property.GetImplementationText), frame);
            }
            catch (MethodReturnSignal)
            {
            }

            return frame.Locals.TryGetValue(property.Name, out var returnCell) ? returnCell.Value : null;
        }

        private void InvokePropertySet(FbInstance instance, string definingType, TcXunit.Parser.PropertyAst property, object value)
        {
            if (!property.HasSet)
                throw new InvalidOperationException($"Property '{property.Name}' has no Set accessor");

            var frame = new Frame(instance, definingType);
            frame.Locals[property.Name] = new Cell { Value = value };
            try
            {
                ExecuteStatements(_registry.GetStatements(property.SetImplementationText), frame);
            }
            catch (MethodReturnSignal)
            {
            }
        }
    }
}
