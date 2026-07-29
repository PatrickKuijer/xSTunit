using System;
using System.Collections.Generic;

namespace TcXunit.Interpreter
{
    // Recursive-descent parser for the fixture's statement/expression subset
    // (TcXunit-w5x.8/.12): assignment, REF=, IF/ELSE/END_IF, and calls
    // (builtin/self/THIS^/SUPER^/receiver-qualified) with positional or named
    // args, plus plain .Member field reads (TcXunit-w5x.15.7). No chained
    // member access beyond one level, no LHS deref - neither is exercised by
    // the fixture yet.
    public sealed partial class Parser
    {
        private readonly List<Token> _tokens;
        private int _pos;

        private Parser(List<Token> tokens)
        {
            _tokens = tokens;
        }

        public static List<Stmt> ParseStatements(string text)
        {
            var parser = new Parser(Lexer.Tokenize(text));
            return parser.ParseStatementList();
        }

        public static Expr ParseExpression(string text)
        {
            var parser = new Parser(Lexer.Tokenize(text));
            return parser.ParseExpr();
        }

        private Token Current => _tokens[_pos];

        private Token Advance()
        {
            var t = _tokens[_pos];
            if (_pos < _tokens.Count - 1)
                _pos++;
            return t;
        }

        private Token Expect(TokenType type)
        {
            if (Current.Type != type)
                throw new FormatException($"Expected {type} but got {Current} at token index {_pos}");
            return Advance();
        }

        private bool IsKeyword(string keyword) =>
            Current.Type == TokenType.Identifier && Current.Text == keyword;

        private static readonly HashSet<string> DefaultTerminators = new HashSet<string> { "ELSIF", "ELSE", "END_IF" };

        private List<Stmt> ParseStatementList(HashSet<string> terminators = null)
        {
            terminators = terminators ?? DefaultTerminators;
            var stmts = new List<Stmt>();
            while (Current.Type != TokenType.Eof && !(Current.Type == TokenType.Identifier && terminators.Contains(Current.Text)))
                stmts.Add(ParseStatement());
            return stmts;
        }

        // Stamps every parsed statement with the in-body line of the token
        // that started it (TcXunit-p3t.2). Nested statements are stamped by
        // their own pass through here, so IF/FOR/CASE bodies get real lines
        // rather than their enclosing statement's.
        private Stmt ParseStatement()
        {
            var line = Current.Line;
            var stmt = ParseStatementCore();
            stmt.Line = line;
            return stmt;
        }

        private Stmt ParseStatementCore()
        {
            if (IsKeyword("IF"))
                return ParseIf();

            if (IsKeyword("FOR"))
                return ParseFor();

            if (IsKeyword("WHILE"))
                return ParseWhile();

            if (IsKeyword("REPEAT"))
                return ParseRepeat();

            if (IsKeyword("CASE"))
                return ParseCase();

            if (IsKeyword("EXIT"))
            {
                Advance();
                Expect(TokenType.Semicolon);
                return new ExitStmt();
            }

            if (IsKeyword("RETURN"))
            {
                Advance();
                Expect(TokenType.Semicolon);
                return new ReturnStmt();
            }

            var target = ParsePostfix(ParsePrimary());

            if (Current.Type == TokenType.Assign)
            {
                Advance();
                var value = ParseExpr();
                Expect(TokenType.Semicolon);
                return new AssignStmt(RequireLValue(target), value);
            }

            if (Current.Type == TokenType.RefAssign)
            {
                Advance();
                var value = ParseExpr();
                Expect(TokenType.Semicolon);
                return new RefAssignStmt(RequireLValue(target), value);
            }

            if (target is CallExpr call)
            {
                Expect(TokenType.Semicolon);
                return new ExprStmt(call);
            }

            throw new FormatException($"Statement did not resolve to an assignment or call at token index {_pos}");
        }

        // Assignment targets: plain identifier, .Member field access, or
        // [idx] array indexing (any depth/mix of the latter two). No LHS
        // deref (x^ :=) yet - not exercised by the fixture.
        private static Expr RequireLValue(Expr target)
        {
            if (target is IdentifierExpr || target is FieldAccessExpr || target is IndexExpr)
                return target;
            throw new FormatException("Assignment target must be an identifier, field access, or array index in the v1 subset");
        }

        private Stmt ParseIf()
        {
            Advance(); // IF
            var condition = ParseExpr();
            if (!IsKeyword("THEN"))
                throw new FormatException("Expected THEN");
            Advance();

            var thenBranch = ParseStatementList();
            return ParseIfTail(condition, thenBranch);
        }

        // Handles the ELSIF/ELSE/END_IF tail of an IF. An ELSIF...THEN chain
        // is represented as a nested IfStmt in the Else branch (each ELSIF
        // recurses here for its own tail), so the engine needs no changes -
        // it already executes IfStmt.Else via ExecuteStatements recursively.
        private Stmt ParseIfTail(Expr condition, List<Stmt> thenBranch)
        {
            var elseBranch = new List<Stmt>();
            if (IsKeyword("ELSIF"))
            {
                // The synthesized IfStmt for an ELSIF never passes through
                // ParseStatement, so stamp it here from the ELSIF keyword
                // itself (TcXunit-p3t.2).
                var elsifLine = Current.Line;
                Advance();
                var elsifCondition = ParseExpr();
                if (!IsKeyword("THEN"))
                    throw new FormatException("Expected THEN");
                Advance();

                var elsifThen = ParseStatementList();
                var elsifStmt = ParseIfTail(elsifCondition, elsifThen);
                elsifStmt.Line = elsifLine;
                elseBranch = new List<Stmt> { elsifStmt };
                return new IfStmt(condition, thenBranch, elseBranch);
            }

            if (IsKeyword("ELSE"))
            {
                Advance();
                elseBranch = ParseStatementList();
            }

            if (!IsKeyword("END_IF"))
                throw new FormatException("Expected END_IF");
            Advance();

            return new IfStmt(condition, thenBranch, elseBranch);
        }

        private Stmt ParseFor()
        {
            Advance(); // FOR
            var varName = Expect(TokenType.Identifier).Text;
            Expect(TokenType.Assign);
            var from = ParseExpr();

            if (!IsKeyword("TO"))
                throw new FormatException("Expected TO");
            Advance();
            var to = ParseExpr();

            Expr step = null;
            if (IsKeyword("BY"))
            {
                Advance();
                step = ParseExpr();
            }

            if (!IsKeyword("DO"))
                throw new FormatException("Expected DO");
            Advance();

            var body = ParseStatementList(new HashSet<string> { "END_FOR" });

            if (!IsKeyword("END_FOR"))
                throw new FormatException("Expected END_FOR");
            Advance();

            return new ForStmt(varName, from, to, step, body);
        }

        private Stmt ParseWhile()
        {
            Advance(); // WHILE
            var condition = ParseExpr();

            if (!IsKeyword("DO"))
                throw new FormatException("Expected DO");
            Advance();

            var body = ParseStatementList(new HashSet<string> { "END_WHILE" });

            if (!IsKeyword("END_WHILE"))
                throw new FormatException("Expected END_WHILE");
            Advance();

            return new WhileStmt(condition, body);
        }

        private Stmt ParseRepeat()
        {
            Advance(); // REPEAT
            var body = ParseStatementList(new HashSet<string> { "UNTIL" });

            if (!IsKeyword("UNTIL"))
                throw new FormatException("Expected UNTIL");
            Advance();
            var until = ParseExpr();

            if (!IsKeyword("END_REPEAT"))
                throw new FormatException("Expected END_REPEAT");
            Advance();

            return new RepeatStmt(body, until);
        }

        private Stmt ParseCase()
        {
            Advance(); // CASE
            var selector = ParseExpr();

            if (!IsKeyword("OF"))
                throw new FormatException("Expected OF");
            Advance();

            var arms = new List<CaseArm>();
            var elseBody = new List<Stmt>();

            while (!IsKeyword("END_CASE") && Current.Type != TokenType.Eof)
            {
                if (IsKeyword("ELSE"))
                {
                    Advance();
                    elseBody = ParseCaseBody();
                    break;
                }

                var labels = ParseCaseLabelList();
                Expect(TokenType.Colon);
                var body = ParseCaseBody();
                arms.Add(new CaseArm(labels, body));
            }

            if (!IsKeyword("END_CASE"))
                throw new FormatException("Expected END_CASE");
            Advance();

            return new CaseStmt(selector, arms, elseBody);
        }

        private List<Stmt> ParseCaseBody()
        {
            var stmts = new List<Stmt>();
            while (Current.Type != TokenType.Eof && !IsCaseArmBoundary())
                stmts.Add(ParseStatement());
            return stmts;
        }

        private List<CaseLabel> ParseCaseLabelList()
        {
            var labels = new List<CaseLabel>();
            do
            {
                var from = ParseExpr();
                Expr to = null;
                if (Current.Type == TokenType.DotDot)
                {
                    Advance();
                    to = ParseExpr();
                }
                labels.Add(new CaseLabel(from, to));
            } while (Current.Type == TokenType.Comma && Advance().Type == TokenType.Comma);
            return labels;
        }

        // Lookahead-only check for "does the current position start a new
        // CASE arm's label list (label[, label...]:)" without consuming any
        // tokens - distinguishes a label boundary from an ordinary statement
        // (assignment/call), which never has a bare ':' immediately after a
        // comma-separated run of literals/identifiers.
        private bool IsCaseArmBoundary()
        {
            if (IsKeyword("ELSE") || IsKeyword("END_CASE"))
                return true;

            var p = _pos;
            while (true)
            {
                if (_tokens[p].Type != TokenType.IntLiteral && _tokens[p].Type != TokenType.Identifier)
                    return false;
                p++;

                while (_tokens[p].Type == TokenType.Dot && _tokens[p + 1].Type == TokenType.Identifier)
                    p += 2;

                if (_tokens[p].Type == TokenType.DotDot)
                {
                    p++;
                    if (_tokens[p].Type != TokenType.IntLiteral && _tokens[p].Type != TokenType.Identifier)
                        return false;
                    p++;
                }

                if (_tokens[p].Type == TokenType.Comma)
                {
                    p++;
                    continue;
                }

                return _tokens[p].Type == TokenType.Colon;
            }
        }

    }
}
