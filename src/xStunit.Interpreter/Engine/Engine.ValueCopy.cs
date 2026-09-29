using System;
using System.Collections.Generic;
using System.Linq;

namespace xStunit.Interpreter
{
    public sealed partial class Engine
    {
        private Func<FbInstance, string, FbInstance> _cloneFbDelegate;

        private object CopyValue(object value, string declaredTypeName = null) =>
            CellCloner.CloneValue(value, declaredTypeName, _cloneFbDelegate ?? (_cloneFbDelegate = CloneFbInstance));

        // What a store leaves in a variable: the existing STRUCT/ARRAY/FB is
        // overwritten in place, so Cells and elements other names point into
        // stay valid; only when there is nothing compatible to overwrite does
        // the target receive a fresh copy.
        private object StoredValue(object existing, object incoming, string declaredTypeName) =>
            TryCopyInto(existing, incoming, declaredTypeName)
                ? existing
                : CopyValue(incoming, declaredTypeName);

        private void AssignToCell(Cell cell, object value) =>
            cell.Value = CoerceForAssignment(
                cell.Value,
                StoredValue(cell.Value, value, cell.DeclaredTypeName),
                cell.DeclaredTypeName);

        private bool TryCopyInto(object existing, object incoming, string declaredTypeName)
        {
            if (existing == null || incoming == null)
                return false;

            if (ReferenceEquals(existing, incoming))
                return true;

            switch (existing)
            {
                case StructInstance target when incoming is StructInstance source:
                    return TryCopyStructInto(target, source);

                case ArrayValue target when incoming is ArrayValue source:
                    return TryCopyArrayInto(target, source);

                case FbInstance target when incoming is FbInstance source:
                    if (!IsCopiedByValueFbType(declaredTypeName) ||
                        source.NativeKind == NativeHostKind.Suite ||
                        !IecIdentifier.Matches(target.ActualTypeName, source.ActualTypeName))
                        return false;
                    CopyFbStateInto(target, source);
                    return true;

                default:
                    return false;
            }
        }

        private bool TryCopyStructInto(StructInstance target, StructInstance source)
        {
            if (target.TypeName == null || !IecIdentifier.Matches(target.TypeName, source.TypeName))
                return false;

            if (target.Overlay != null && source.Overlay != null)
            {
                target.Overlay.Load(source.Overlay.SettledBytes(), 0);
                return true;
            }

            if (target.Overlay != null || source.Overlay != null)
                return false;

            CopyCells(
                target.Fields,
                source.Fields,
                name => source.FieldTypeNames.TryGetValue(name, out var typeName) ? typeName : null,
                null,
                null);
            return true;
        }

        private bool TryCopyArrayInto(ArrayValue target, ArrayValue source)
        {
            if (target.Elements.Length != source.Elements.Length ||
                !target.Dimensions.SequenceEqual(source.Dimensions))
                return false;

            for (var i = 0; i < source.Elements.Length; i++)
                target.SetElement(i, StoredValue(target.Elements[i], source.Elements[i], target.ElementTypeName));
            return true;
        }

        private void CopyCells(
            Dictionary<string, Cell> target,
            IEnumerable<KeyValuePair<string, Cell>> source,
            Func<string, string> declaredTypeNameOf,
            HashSet<string> targetInOutBound,
            HashSet<string> sourceInOutBound)
        {
            foreach (var pair in source)
            {
                var name = pair.Key;
                var declaredTypeName = declaredTypeNameOf(name);
                var sourceBound = sourceInOutBound != null && sourceInOutBound.Contains(name);

                if (sourceBound || AddressTypeInfo.IsReferenceType(declaredTypeName))
                {
                    target[name] = pair.Value;
                    if (sourceBound)
                        targetInOutBound?.Add(name);
                    else
                        targetInOutBound?.Remove(name);
                    continue;
                }

                var targetWasBound = targetInOutBound != null && targetInOutBound.Remove(name);
                if (!targetWasBound && target.TryGetValue(name, out var own))
                    own.Value = StoredValue(own.Value, pair.Value.Value, declaredTypeName ?? own.DeclaredTypeName);
                else
                    target[name] = new Cell
                    {
                        Value = CopyValue(pair.Value.Value, declaredTypeName ?? pair.Value.DeclaredTypeName),
                        DeclaredTypeName = pair.Value.DeclaredTypeName,
                        StringCapacity = pair.Value.StringCapacity,
                    };
            }
        }

        private void CopyFbStateInto(FbInstance target, FbInstance source)
        {
            foreach (var pair in source.FieldTypeNames)
                target.FieldTypeNames[pair.Key] = pair.Value;

            CopyCells(
                target.Fields,
                source.Fields,
                name => source.FieldTypeNames.TryGetValue(name, out var typeName) ? typeName : null,
                target.InOutBoundFieldNames,
                source.InOutBoundFieldNames);

            target.NativeHost = CloneNativeHost(source);

            foreach (var table in source.MethodInstanceTables)
            {
                var typeNames = MethodInstanceTypeNames(table.Key.DeclaringType, table.Key.Method);
                var targetCells = target.GetOrCreateMethodInstanceCells(
                    table.Key.DeclaringType,
                    table.Key.Method,
                    () => new Dictionary<string, Cell>(IecIdentifier.Comparer));
                CopyCells(
                    targetCells,
                    table.Value,
                    name => typeNames.TryGetValue(name, out var typeName) ? typeName : null,
                    null,
                    null);
            }
        }

        private Dictionary<string, string> MethodInstanceTypeNames(string declaringType, string methodName)
        {
            var result = new Dictionary<string, string>(IecIdentifier.Comparer);
            var method = _registry.Get(declaringType)?.Methods.FirstOrDefault(m => IecIdentifier.Matches(m.Name, methodName));
            if (method == null)
                return result;

            foreach (var decl in _registry.GetDecls(method.DeclarationText).Where(d => d.Section == VarSection.MethodInstance))
                result[decl.Name] = decl.TypeName;
            return result;
        }

        private object CloneNativeHost(FbInstance source)
        {
            switch (source.NativeKind)
            {
                case NativeHostKind.Timer:
                    return source.NativeTimerHost.CloneState();
                case NativeHostKind.Edge:
                    return source.NativeEdgeTriggerHost.CloneState();
                case NativeHostKind.Counter:
                    return source.NativeCounterHost.CloneState();
                case NativeHostKind.Loopback:
                    return source.NativeLoopbackHost.CloneState();
                case NativeHostKind.Plugin:
                    return source.NativePluginFunctionBlock.CreateInstance();
                default:
                    return source.NativeHost;
            }
        }

        private bool IsCopiedByValueFbType(string declaredTypeName)
        {
            if (declaredTypeName == null)
                return false;

            var resolved = _registry.ResolveAlias(declaredTypeName);
            return _registry.Get(resolved) != null || IsNativeFbTypeName(resolved);
        }

        private FbInstance CloneFbInstance(FbInstance source, string declaredTypeName)
        {
            if (source.NativeKind == NativeHostKind.Suite || !IsCopiedByValueFbType(declaredTypeName))
                return source;

            var copy = new FbInstance(source.ActualTypeName) { NativeKind = source.NativeKind };
            CopyFbStateInto(copy, source);
            return copy;
        }

        private object EvaluateInputArgument(VarDecl decl, Expr argument, Frame callerFrame)
        {
            var value = Evaluate(argument, callerFrame);
            return AddressTypeInfo.IsAddressType(_registry.ResolveAlias(decl.TypeName))
                ? value
                : CopyValue(value, decl.TypeName);
        }

        private bool TryResolveInOutCell(VarDecl decl, Expr argument, Frame callerFrame, out Cell cell)
        {
            cell = null;
            return decl.Section == VarSection.InOut && TryResolveStorageCell(argument, callerFrame, out cell);
        }

        private bool TryResolveStorageCell(Expr expr, Frame frame, out Cell cell)
        {
            cell = null;
            switch (expr)
            {
                case IdentifierExpr id:
                    cell = frame.ResolveCell(id.Name);
                    return cell != null || TryResolveGlobalCell(id.Name, out cell);

                case FieldAccessExpr fieldAccess:
                    return TryResolveFieldCell(fieldAccess, frame, out cell);

                case IndexExpr index:
                {
                    var receiver = Evaluate(index.Receiver, frame);
                    if (receiver is ArrayValue array)
                        cell = new ArrayElementCell(array, FlattenIndex(array, index.Indices, frame));
                    else if (receiver is string)
                        cell = new StringByteCell(
                            ResolveCellForLValue(index.Receiver, frame),
                            ResolveStringIndex(index.Indices, frame));
                    return cell != null;
                }

                case DerefExpr deref:
                    cell = ((Pointer)Evaluate(deref.Inner, frame)).Target;
                    return true;

                default:
                    return false;
            }
        }

        private bool TryResolveFieldCell(FieldAccessExpr fieldAccess, Frame frame, out Cell cell)
        {
            cell = null;
            if (TryGetGvlFields(fieldAccess, frame, out var gvlFields))
                return gvlFields.TryGetValue(fieldAccess.FieldName, out cell);

            if (fieldAccess.Receiver is IdentifierExpr receiverId &&
                frame.ResolveCell(receiverId.Name) == null &&
                !TryResolveGlobalCell(receiverId.Name, out _))
                return false;

            var receiver = Evaluate(fieldAccess.Receiver, frame);
            return (receiver is FbInstance || receiver is StructInstance) &&
                FieldsOf(receiver, fieldAccess.FieldName).TryGetValue(fieldAccess.FieldName, out cell);
        }
    }
}
