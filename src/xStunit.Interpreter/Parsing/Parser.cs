using System;
using System.Collections.Generic;
using xStunit.Runner;

namespace xStunit.Interpreter
{
    // Recursive-descent parser for the subset of ST the fixtures use, grown
    // on demand rather than built out to the full IEC 61131-3 grammar. A
    // construct it rejects may simply be one no fixture has needed yet.
    //
    // Precedence runs lowest to highest down the ParseOr -> ParseXor ->
    // ParseAnd -> ParseComparison -> ParseAdditive -> ParseMod -> ParseUnary
    // -> ParsePostfix chain; every binary level is left-associative except
    // comparison, which takes at most one operator and so does not chain.
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

        public static Expr ParseCompleteExpression(string text)
        {
            var parser = new Parser(Lexer.Tokenize(text));
            var expr = parser.ParseExpr();
            if (parser.Current.Type != TokenType.Eof)
                throw new ParseException(
                    $"Unexpected {parser.Current} after expression at token index {parser._pos}", parser.CurrentToken, parser.Current.Line);
            return expr;
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
                throw new ParseException(
                    $"Expected {type} but got {Current} at token index {_pos}", CurrentToken, Current.Line);
            return Advance();
        }

        // Null for a token carrying no meaningful text (e.g. Eof), matching
        // the nullable convention every other `construct` field uses.
        private string CurrentToken => string.IsNullOrEmpty(Current.Text) ? null : Current.Text;

        private bool IsKeyword(string keyword) =>
            Current.Type == TokenType.Identifier && Current.Text == keyword;

        // IEC 61131-3 doesn't require a ; after END_IF/END_FOR/END_WHILE/
        // END_CASE/END_REPEAT, but plenty of real ST writes one anyway.
        // Swallowing it here keeps ParseStatementList from mistaking it for
        // the start of a new statement.
        private void SkipOptionalSemicolon()
        {
            if (Current.Type == TokenType.Semicolon)
                Advance();
        }

        private static readonly HashSet<string> DefaultTerminators = new HashSet<string> { "ELSIF", "ELSE", "END_IF" };

        private List<Stmt> ParseStatementList(HashSet<string> terminators = null)
        {
            terminators = terminators ?? DefaultTerminators;
            var stmts = new List<Stmt>();
            while (Current.Type != TokenType.Eof && !(Current.Type == TokenType.Identifier && terminators.Contains(Current.Text)))
                stmts.Add(ParseStatement());
            return stmts;
        }

        // Stamps each statement with the in-body line of the token that
        // started it. Nested statements are stamped by their own pass through
        // here, so IF/FOR/CASE bodies get their own lines rather than the
        // enclosing statement's.
        private Stmt ParseStatement()
        {
            var line = Current.Line;
            var stmt = ParseStatementCore();
            stmt.Line = line;
            return stmt;
        }

        private Stmt ParseStatementCore()
        {
            if (Current.Type == TokenType.Semicolon)
            {
                Advance();
                return new NoOpStmt();
            }

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

            if (TryTakeSetResetOperator(out var isSet))
            {
                var value = ParseExpr();
                Expect(TokenType.Semicolon);
                return new SetResetAssignStmt(RequireSetResetTarget(target), value, isSet);
            }

            if (target is CallExpr call)
            {
                Expect(TokenType.Semicolon);
                return new ExprStmt(call);
            }

            // A ParseException rather than an UnsupportedConstructException:
            // the whole IEC 61131-3 statement grammar - assignment,
            // invocation, RETURN, IF/CASE, FOR/WHILE/REPEAT/EXIT - is
            // dispatched by name above, so no valid construct reaches here.
            // Only malformed source does (e.g. a bare non-call expression
            // used as a statement), and calling that unsupported would be a
            // guess.
            throw new ParseException(
                $"Statement did not resolve to an assignment or call at token index {_pos}", CurrentToken, Current.Line);
        }

        // The lexer has no S=/R= token - S and R are ordinary identifiers, and
        // "IF S=1" must keep meaning a comparison - so the operator is only
        // recognised here, straight after a complete assignment target, where
        // an identifier followed by '=' has no other reading.
        private bool TryTakeSetResetOperator(out bool isSet)
        {
            isSet = false;
            if (Current.Type != TokenType.Identifier || _pos + 1 >= _tokens.Count || _tokens[_pos + 1].Type != TokenType.Eq)
                return false;

            var letter = Current.Text.ToUpperInvariant();
            if (letter != "S" && letter != "R")
                return false;

            if (!Current.GluedToEquals)
                throw new ParseException(
                    $"'{letter} =' is not an operator; the set/reset assignment is written {letter}= with no space", CurrentToken, Current.Line);

            isSet = letter == "S";
            Advance();
            Advance();
            return true;
        }

        // A dereferenced pointer is let through to the engine, which rejects it
        // as unsupported at run time: rejecting it here would fail the whole
        // body it sits in - for a suite body, every test in the suite.
        private static Expr RequireSetResetTarget(Expr target) =>
            target is DerefExpr ? target : RequireLValue(target);

        private static Expr RequireLValue(Expr target)
        {
            if (target is IdentifierExpr || target is FieldAccessExpr || target is IndexExpr)
                return target;

            // x^ := ... is valid IEC 61131-3 pointer-dereference assignment,
            // and the read side already works (Engine evaluates DerefExpr) -
            // only the write side is missing. Rejecting it as an unsupported
            // construct rather than a parse error keeps it from being
            // reported as a defect in the source under test.
            if (target is DerefExpr)
                throw new UnsupportedConstructException("x^ :=", "Pointer dereference on the assignment left-hand side (x^ := ...) is not supported in the v1 subset");

            throw new ParseException(
                "Assignment target must be an identifier, field access, or array index in the v1 subset",
                target.GetType().Name, target.Line);
        }

        private Stmt ParseIf()
        {
            Advance(); // IF
            var condition = ParseExpr();
            if (!IsKeyword("THEN"))
                throw new ParseException("Expected THEN", CurrentToken, Current.Line);
            Advance();

            var thenBranch = ParseStatementList();
            return ParseIfTail(condition, thenBranch);
        }

        // Handles the ELSIF/ELSE/END_IF tail of an IF. An ELSIF...THEN chain
        // is desugared into a nested IfStmt in the Else branch, so the engine
        // needs no ELSIF concept of its own - executing IfStmt.Else
        // recursively already covers it.
        private Stmt ParseIfTail(Expr condition, List<Stmt> thenBranch)
        {
            var elseBranch = new List<Stmt>();
            if (IsKeyword("ELSIF"))
            {
                // The synthesized IfStmt never passes through ParseStatement,
                // so it has to be stamped here from the ELSIF keyword itself.
                var elsifLine = Current.Line;
                Advance();
                var elsifCondition = ParseExpr();
                if (!IsKeyword("THEN"))
                    throw new ParseException("Expected THEN", CurrentToken, Current.Line);
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
                throw new ParseException("Expected END_IF", CurrentToken, Current.Line);
            Advance();
            SkipOptionalSemicolon();

            return new IfStmt(condition, thenBranch, elseBranch);
        }

        private Stmt ParseFor()
        {
            Advance(); // FOR
            var loopVar = RequireLValue(ParsePostfix(ParsePrimary()));
            Expect(TokenType.Assign);
            var from = ParseExpr();

            if (!IsKeyword("TO"))
                throw new ParseException("Expected TO", CurrentToken, Current.Line);
            Advance();
            var to = ParseExpr();

            Expr step = null;
            if (IsKeyword("BY"))
            {
                Advance();
                step = ParseExpr();
            }

            if (!IsKeyword("DO"))
                throw new ParseException("Expected DO", CurrentToken, Current.Line);
            Advance();

            var body = ParseStatementList(new HashSet<string> { "END_FOR" });

            if (!IsKeyword("END_FOR"))
                throw new ParseException("Expected END_FOR", CurrentToken, Current.Line);
            Advance();
            SkipOptionalSemicolon();

            return new ForStmt(loopVar, from, to, step, body);
        }

        private Stmt ParseWhile()
        {
            Advance(); // WHILE
            var condition = ParseExpr();

            if (!IsKeyword("DO"))
                throw new ParseException("Expected DO", CurrentToken, Current.Line);
            Advance();

            var body = ParseStatementList(new HashSet<string> { "END_WHILE" });

            if (!IsKeyword("END_WHILE"))
                throw new ParseException("Expected END_WHILE", CurrentToken, Current.Line);
            Advance();
            SkipOptionalSemicolon();

            return new WhileStmt(condition, body);
        }

        private Stmt ParseRepeat()
        {
            Advance(); // REPEAT
            var body = ParseStatementList(new HashSet<string> { "UNTIL" });

            if (!IsKeyword("UNTIL"))
                throw new ParseException("Expected UNTIL", CurrentToken, Current.Line);
            Advance();
            var until = ParseExpr();

            if (!IsKeyword("END_REPEAT"))
                throw new ParseException("Expected END_REPEAT", CurrentToken, Current.Line);
            Advance();
            SkipOptionalSemicolon();

            return new RepeatStmt(body, until);
        }

        private Stmt ParseCase()
        {
            Advance(); // CASE
            var selector = ParseExpr();

            if (!IsKeyword("OF"))
                throw new ParseException("Expected OF", CurrentToken, Current.Line);
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
                throw new ParseException("Expected END_CASE", CurrentToken, Current.Line);
            Advance();
            SkipOptionalSemicolon();

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

        // Whether the current position starts a new CASE arm's label list,
        // decided by lookahead without consuming anything. The distinguishing
        // shape is a bare ':' after a comma-separated run of literals/
        // identifiers, which an ordinary statement never has.
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
