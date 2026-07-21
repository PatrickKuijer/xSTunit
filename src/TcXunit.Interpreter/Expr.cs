using System.Collections.Generic;

namespace TcXunit.Interpreter
{
    public abstract class Expr
    {
    }

    public sealed class IntLiteralExpr : Expr
    {
        public int Value { get; }
        public IntLiteralExpr(int value) => Value = value;
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

    public sealed class NamedArg
    {
        public string Name { get; }
        public Expr Value { get; }
        public NamedArg(string name, Expr value)
        {
            Name = name;
            Value = value;
        }
    }

    // Covers both builtin calls (ADR(x), receiver == null) and method calls
    // (receiver == null means implicit self, otherwise Identifier/ThisRef/
    // SuperRef). Args are either all-positional or all-named per the fixture's
    // call sites - no mixing needed (TcXunit-w5x.8/.12).
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
