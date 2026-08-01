using System;
using System.Collections.Generic;
using System.Linq;

namespace xStunit.Interpreter
{
    public sealed partial class Engine
    {
        // The byte sizes SIZEOF() and the MEMCPY byte-layout packing are built
        // from.
        //
        // STRUCT and ARRAY sizes assume TwinCAT's default natural-alignment
        // packing: each field aligned to its own size (no alignment above 8
        // bytes arises among the supported scalars), and the struct's overall
        // size padded up to its largest member's alignment. A struct's
        // {attribute 'pack_mode' := 'N'} pragma caps that per-field alignment
        // at N bytes instead - see PackBound/SizeOfStruct below.
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

        // SIZEOF's argument is normally a declared variable or field, but when
        // it doesn't resolve as one, the identifier's own text is tried as a
        // type name - so SIZEOF(ST_Msg) and SIZEOF(DINT), naming a type rather
        // than a variable of that type, work too.
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
            {
                // The length may be a non-literal constant expression (e.g. a
                // GVL-qualified constant), so it goes through Evaluate() rather
                // than a bare int.Parse, same as the ARRAY bound above.
                var length = StringTypeInfo.ParseLength(
                    resolved, boundText => Convert.ToInt32(Evaluate(Parser.ParseExpression(boundText), frame)));

                // Latin-1 narrow STRING is one byte per character plus a
                // one-byte terminator; UCS-2 WSTRING is two bytes per character
                // plus a two-byte terminator, and its elements are WORD-aligned,
                // so a WSTRING member pads itself and everything after it.
                var charWidth = StringTypeInfo.CharWidth(resolved);
                return (charWidth * (length + 1), charWidth);
            }

            if (resolved == "BOOL")
                return (1, 1);

            if (ScalarByteSizes.TryGetValue(resolved, out var scalarSize))
                return (scalarSize, scalarSize);

            throw new NotSupportedException($"SIZEOF() doesn't know the byte size of type '{typeName}'");
        }

        private (int Size, int Align) SizeOfStruct(StructAst structAst, Frame frame)
        {
            var packBound = PackBound(structAst);
            var offset = 0;
            var maxAlign = 1;
            foreach (var field in structAst.Fields)
            {
                var (fieldSize, fieldAlign) = SizeOfType(field.TypeName, frame);
                var effectiveAlign = Math.Min(fieldAlign, packBound);
                offset = RoundUp(offset, effectiveAlign);
                offset += fieldSize;
                maxAlign = Math.Max(maxAlign, effectiveAlign);
            }

            return (RoundUp(offset, maxAlign), maxAlign);
        }

        // pack_mode 0 (absent) means no cap, i.e. natural alignment. A positive
        // pack_mode caps every field's alignment at that many bytes; pack_mode 1
        // is fully byte-packed, with no padding anywhere.
        private static int PackBound(StructAst structAst) =>
            structAst.PackMode > 0 ? structAst.PackMode : int.MaxValue;

        private static int RoundUp(int value, int align) => (value + align - 1) / align * align;
    }
}
