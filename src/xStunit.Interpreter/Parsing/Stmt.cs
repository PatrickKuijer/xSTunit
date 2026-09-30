using System.Collections.Generic;

namespace xStunit.Interpreter
{
    public abstract class Stmt
    {
        // 1-based line within the ST body, taken from the token that STARTS
        // the statement. See Token.Line for the convention and the file-line
        // formula.
        //
        // Settable rather than a constructor parameter: Stmt/Expr subclasses
        // are constructed in hundreds of places (mostly hand-built ASTs in
        // the tests) and the parser is the only caller with a line to give.
        // 0 means "unknown", which is what hand-built nodes keep.
        public int Line { get; set; }
    }

    // A bare ";" - the no-op statement IEC 61131-3 allows anywhere a
    // statement is expected, most often as an intentionally-empty CASE
    // branch or ELSE arm.
    public sealed class NoOpStmt : Stmt
    {
    }

    public sealed class AssignStmt : Stmt
    {
        public Expr Target { get; }
        public Expr Value { get; }
        public AssignStmt(Expr target, Expr value)
        {
            Target = target;
            Value = value;
        }
    }

    public sealed class RefAssignStmt : Stmt
    {
        public Expr Target { get; }
        public Expr Value { get; }
        public RefAssignStmt(Expr target, Expr value)
        {
            Target = target;
            Value = value;
        }
    }

    // x S= e / x R= e: writes TRUE (S=) or FALSE (R=) to Target when Value is
    // TRUE, and leaves Target untouched otherwise.
    public sealed class SetResetAssignStmt : Stmt
    {
        public Expr Target { get; }
        public Expr Value { get; }
        public bool IsSet { get; }
        public SetResetAssignStmt(Expr target, Expr value, bool isSet)
        {
            Target = target;
            Value = value;
            IsSet = isSet;
        }
    }

    public sealed class IfStmt : Stmt
    {
        public Expr Condition { get; }
        public IReadOnlyList<Stmt> Then { get; }
        public IReadOnlyList<Stmt> Else { get; }
        public IfStmt(Expr condition, IReadOnlyList<Stmt> thenBranch, IReadOnlyList<Stmt> elseBranch)
        {
            Condition = condition;
            Then = thenBranch;
            Else = elseBranch;
        }
    }

    public sealed class ExprStmt : Stmt
    {
        public CallExpr Call { get; }
        public ExprStmt(CallExpr call) => Call = call;
    }

    public sealed class ForStmt : Stmt
    {
        public Expr Var { get; }
        public Expr From { get; }
        public Expr To { get; }
        public Expr Step { get; }
        public IReadOnlyList<Stmt> Body { get; }
        public ForStmt(Expr var, Expr from, Expr to, Expr step, IReadOnlyList<Stmt> body)
        {
            Var = var;
            From = from;
            To = to;
            Step = step;
            Body = body;
        }
    }

    public sealed class WhileStmt : Stmt
    {
        public Expr Condition { get; }
        public IReadOnlyList<Stmt> Body { get; }
        public WhileStmt(Expr condition, IReadOnlyList<Stmt> body)
        {
            Condition = condition;
            Body = body;
        }
    }

    public sealed class RepeatStmt : Stmt
    {
        public IReadOnlyList<Stmt> Body { get; }
        public Expr Until { get; }
        public RepeatStmt(IReadOnlyList<Stmt> body, Expr until)
        {
            Body = body;
            Until = until;
        }
    }

    public sealed class CaseLabel
    {
        public Expr From { get; }
        public Expr To { get; }
        public bool IsRange => To != null;
        public CaseLabel(Expr from, Expr to = null)
        {
            From = from;
            To = to;
        }
    }

    public sealed class CaseArm
    {
        public IReadOnlyList<CaseLabel> Labels { get; }
        public IReadOnlyList<Stmt> Body { get; }
        public CaseArm(IReadOnlyList<CaseLabel> labels, IReadOnlyList<Stmt> body)
        {
            Labels = labels;
            Body = body;
        }
    }

    public sealed class CaseStmt : Stmt
    {
        public Expr Selector { get; }
        public IReadOnlyList<CaseArm> Arms { get; }
        public IReadOnlyList<Stmt> ElseBody { get; }
        public CaseStmt(Expr selector, IReadOnlyList<CaseArm> arms, IReadOnlyList<Stmt> elseBody)
        {
            Selector = selector;
            Arms = arms;
            ElseBody = elseBody;
        }
    }

    public sealed class ExitStmt : Stmt
    {
    }

    public sealed class ReturnStmt : Stmt
    {
    }
}
