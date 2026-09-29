using System.Collections.Generic;

namespace xStunit.Interpreter
{
    public abstract class Expr
    {
        // 1-based line within the ST body, taken from the token that STARTS
        // the expression - so a BinaryExpr reports its left operand's line.
        // See Token.Line for the convention and the file-line formula; 0
        // means "unknown", the value hand-built nodes keep.
        public int Line { get; set; }
    }

    public sealed class IntLiteralExpr : Expr
    {
        public int Value { get; }
        public IntLiteralExpr(int value) => Value = value;
    }

    // The two wider integer literals, produced only for a value that will not
    // fit the box above them - see the width rule at Parser's
    // ParseIntegerLiteral. They box the way LINT and ULINT/LWORD cells do, so
    // a 64-bit initializer reaches its cell without a widening step.
    public sealed class LintLiteralExpr : Expr
    {
        public long Value { get; }
        public LintLiteralExpr(long value) => Value = value;
    }

    public sealed class UlintLiteralExpr : Expr
    {
        public ulong Value { get; }
        public UlintLiteralExpr(ulong value) => Value = value;
    }

    public sealed class RealLiteralExpr : Expr
    {
        public float Value { get; }
        public RealLiteralExpr(float value) => Value = value;
    }

    public sealed class LrealLiteralExpr : Expr
    {
        public double Value { get; }
        public LrealLiteralExpr(double value) => Value = value;
    }

    public sealed class TimeLiteralExpr : Expr
    {
        public uint Value { get; }
        public TimeLiteralExpr(uint value) => Value = value;
    }

    public sealed class LtimeLiteralExpr : Expr
    {
        public ulong Value { get; }
        public LtimeLiteralExpr(ulong value) => Value = value;
    }

    public sealed class DateLiteralExpr : Expr
    {
        public uint Value { get; }
        public DateLiteralExpr(uint value) => Value = value;
    }

    public sealed class DateAndTimeLiteralExpr : Expr
    {
        public uint Value { get; }
        public DateAndTimeLiteralExpr(uint value) => Value = value;
    }

    public sealed class TimeOfDayLiteralExpr : Expr
    {
        public uint Value { get; }
        public TimeOfDayLiteralExpr(uint value) => Value = value;
    }

    public sealed class BoolLiteralExpr : Expr
    {
        public bool Value { get; }
        public BoolLiteralExpr(bool value) => Value = value;
    }

    public sealed class StringLiteralExpr : Expr
    {
        public string Value { get; }
        public StringLiteralExpr(string value) => Value = value;
    }

    public sealed class IdentifierExpr : Expr
    {
        public string Name { get; }
        public IdentifierExpr(string name) => Name = name;
    }

    public sealed class ThisRefExpr : Expr
    {
    }

    public sealed class SuperRefExpr : Expr
    {
    }

    // Plain .Member access with no call parens - reads a field off a receiver
    // that evaluates to an FbInstance, e.g. fbTon.Q after fbTon(IN:=..).
    public sealed class FieldAccessExpr : Expr
    {
        public Expr Receiver { get; }
        public string FieldName { get; }
        public FieldAccessExpr(Expr receiver, string fieldName)
        {
            Receiver = receiver;
            FieldName = fieldName;
        }
    }

    // arr[i] or arr[i, j, ...] - Indices.Count matches the array's declared
    // dimension count, flattened row-major against ArrayValue.Dimensions at
    // evaluation time.
    public sealed class IndexExpr : Expr
    {
        public Expr Receiver { get; }
        public IReadOnlyList<Expr> Indices { get; }
        public IndexExpr(Expr receiver, IReadOnlyList<Expr> indices)
        {
            Receiver = receiver;
            Indices = indices;
        }
    }

    public sealed class DerefExpr : Expr
    {
        public Expr Inner { get; }
        public DerefExpr(Expr inner) => Inner = inner;
    }

    public sealed class UnaryExpr : Expr
    {
        public string Op { get; }
        public Expr Operand { get; }
        public UnaryExpr(string op, Expr operand)
        {
            Op = op;
            Operand = operand;
        }
    }

    public sealed class BinaryExpr : Expr
    {
        public string Op { get; }
        public Expr Left { get; }
        public Expr Right { get; }
        public BinaryExpr(string op, Expr left, Expr right)
        {
            Op = op;
            Left = left;
            Right = right;
        }
    }

    // Struct literal initializer: (field1 := val1, field2 := val2, ...),
    // reusing NamedArg for the field-name/value pairs.
    public sealed class StructLiteralExpr : Expr
    {
        public IReadOnlyList<NamedArg> FieldInits { get; }
        public StructLiteralExpr(IReadOnlyList<NamedArg> fieldInits) => FieldInits = fieldInits;
    }

    // Array literal initializer: [v0, v1, ...]. The [n(v)] repeat shorthand
    // is expanded at parse time into n references to the SAME Expr node, so
    // Elements is always the flat, fully-expanded element list.
    public sealed class ArrayLiteralExpr : Expr
    {
        public IReadOnlyList<Expr> Elements { get; }
        public ArrayLiteralExpr(IReadOnlyList<Expr> elements) => Elements = elements;
    }

    public sealed class NamedArg
    {
        public string Name { get; }
        public Expr Value { get; }

        // True for "Name => expr" VAR_OUTPUT-binding syntax, false for the
        // ordinary "Name := expr" VAR_INPUT/VAR_IN_OUT form. BindParams
        // consults only the latter; Engine's WriteBackOutputArgs consults
        // only the former.
        public bool IsOutput { get; }

        // An output listed with no target ('Name =>'). Value is null for it,
        // so every consumer that evaluates named args must skip it.
        public bool IsUnboundOutput => IsOutput && Value == null;

        public NamedArg(string name, Expr value, bool isOutput = false)
        {
            Name = name;
            Value = value;
            IsOutput = isOutput;
        }
    }

    // A builtin call (ADR(x)) or a method call. A null Receiver covers both
    // the builtin case and an implicit self-call; otherwise it is an
    // Identifier, ThisRef or SuperRef. Fixture call sites are all-positional
    // or all-named, so mixing the two has never needed to work.
    public sealed class CallExpr : Expr
    {
        public Expr Receiver { get; }
        public string MethodName { get; }
        public IReadOnlyList<Expr> PositionalArgs { get; }
        public IReadOnlyList<NamedArg> NamedArgs { get; }

        public CallExpr(Expr receiver, string methodName, IReadOnlyList<Expr> positionalArgs, IReadOnlyList<NamedArg> namedArgs)
        {
            Receiver = receiver;
            MethodName = methodName;
            PositionalArgs = positionalArgs;
            NamedArgs = namedArgs;
        }
    }
}
