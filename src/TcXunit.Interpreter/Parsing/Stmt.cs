using System.Collections.Generic;

namespace TcXunit.Interpreter
{
    public abstract class Stmt
    {
        // 1-based line within the ST body this statement was parsed from -
        // the token that STARTS the statement (TcXunit-p3t.2). See Token.Line
        // for the convention and for the BodyStartLine + Line - 1 formula
        // that turns it into a .TcPOU file line.
        //
        // Settable rather than a constructor parameter: Stmt/Expr subclasses
        // are constructed in hundreds of places (mostly hand-built ASTs in
        // the tests), and the parser is the only caller that has a line to
        // give. 0 means "unknown", which is what hand-built nodes keep.
        public int Line { get; set; }
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
        public string TargetName { get; }
        public Expr Value { get; }
        public RefAssignStmt(string targetName, Expr value)
        {
            TargetName = targetName;
            Value = value;
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
        public string VarName { get; }
        public Expr From { get; }
        public Expr To { get; }
        public Expr Step { get; }
        public IReadOnlyList<Stmt> Body { get; }
        public ForStmt(string varName, Expr from, Expr to, Expr step, IReadOnlyList<Stmt> body)
        {
            VarName = varName;
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

    // Single label (constant expr) or a lo..hi range label; To is null for a
    // single-value label.
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
