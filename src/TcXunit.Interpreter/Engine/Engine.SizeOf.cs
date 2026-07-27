using System;
using System.Collections.Generic;
using System.Linq;

namespace TcXunit.Interpreter
{
    public sealed partial class Engine
    {
        // SIZEOF(x) - TcXunit-l1x: byte size of a variable/field or a bare
        // type name. x is normally a declared variable/field (resolved via
        // ResolveDeclaredTypeName, same lookup __ISVALIDREF uses); when that
        // comes back null (not a known variable) the identifier's own text
        // is tried as a type name instead, so SIZEOF(ST_Msg)/SIZEOF(DINT)
        // (naming a type directly, not a variable of that type) also work.
        //
        // STRUCT/ARRAY sizes assume TwinCAT's default natural-alignment
        // struct packing - each field aligned to its own size (no >8-byte
        // alignment case exists among the supported scalar sizes) and the
        // struct's overall size padded up to its largest member's alignment.
        // There's no {attribute 'pack_mode'} support - nothing in the
        // fixtures uses non-default packing yet (grow-on-demand).
        private static readonly IReadOnlyDictionary<string, int> ScalarByteSizes = new Dictionary<string, int>
        {
            ["SINT"] = 1,
            ["USINT"] = 1,
            ["BYTE"] = 1,
            ["INT"] = 2,
            ["UINT"] = 2,
            ["WORD"] = 2,
            ["DINT"] = 4,
            ["UDINT"] = 4,
            ["DWORD"] = 4,
            ["REAL"] = 4,
            ["TIME"] = 4,
            ["DATE"] = 4,
            ["DATE_AND_TIME"] = 4,
            ["TIME_OF_DAY"] = 4,
            ["LINT"] = 8,
            ["ULINT"] = 8,
            ["LWORD"] = 8,
            ["LREAL"] = 8,
            ["LTIME"] = 8,
        };

        private int EvaluateSizeOf(Expr argExpr, Frame frame)
        {
            var typeName = ResolveTypeNameForSizeOf(argExpr, frame);
            var (size, _) = SizeOfType(typeName, frame);
            return size;
        }

        private string ResolveTypeNameForSizeOf(Expr argExpr, Frame frame)
        {
            var declaredType = ResolveDeclaredTypeName(argExpr, frame);
            if (declaredType != null)
                return declaredType;

            if (argExpr is IdentifierExpr id)
                return id.Name;

            throw new NotSupportedException(
                $"SIZEOF() argument must be a variable, field, or bare type name; got {argExpr.GetType().Name}");
        }

        private (int Size, int Align) SizeOfType(string typeName, Frame frame)
        {
            var resolved = _registry.ResolveAlias(typeName?.Trim());
            if (resolved == null)
                throw new NotSupportedException("SIZEOF() requires a type name");

            if (resolved.StartsWith("POINTER TO", StringComparison.Ordinal) ||
                resolved.StartsWith("REFERENCE TO", StringComparison.Ordinal))
                return (4, 4);

            if (ArrayTypeInfo.IsArrayType(resolved))
            {
                var (dimensions, elementTypeName) = ArrayTypeInfo.Parse(
                    resolved,
                    boundText => Convert.ToInt32(Evaluate(Parser.ParseExpression(boundText), frame)));
                var count = dimensions.Aggregate(1, (acc, d) => acc * (d.Hi - d.Lo + 1));
                var (elementSize, elementAlign) = SizeOfType(elementTypeName, frame);
                return (count * elementSize, elementAlign);
            }

            var structAst = _registry.GetStruct(resolved);
            if (structAst != null)
                return SizeOfStruct(structAst, frame);

            if (StringTypeInfo.IsStringType(resolved))
                return (StringTypeInfo.ParseLength(resolved) + 1, 1);

            if (resolved == "BOOL")
                return (1, 1);

            if (ScalarByteSizes.TryGetValue(resolved, out var scalarSize))
                return (scalarSize, scalarSize);

            throw new NotSupportedException($"SIZEOF() doesn't know the byte size of type '{typeName}'");
        }

        private (int Size, int Align) SizeOfStruct(StructAst structAst, Frame frame)
        {
            var offset = 0;
            var maxAlign = 1;
            foreach (var field in structAst.Fields)
            {
                var (fieldSize, fieldAlign) = SizeOfType(field.TypeName, frame);
                offset = RoundUp(offset, fieldAlign);
                offset += fieldSize;
                maxAlign = Math.Max(maxAlign, fieldAlign);
            }

            return (RoundUp(offset, maxAlign), maxAlign);
        }

        private static int RoundUp(int value, int align) => (value + align - 1) / align * align;
    }
}
