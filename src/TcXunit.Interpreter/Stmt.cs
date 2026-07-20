using System.Collections.Generic;

namespace TcXunit.Interpreter
{
    public abstract class Stmt
    {
    }

    public sealed class AssignStmt : Stmt
    {
        public string TargetName { get; }
        public Expr Value { get; }
        public AssignStmt(string targetName, Expr value)
        {
            TargetName = targetName;
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
}
