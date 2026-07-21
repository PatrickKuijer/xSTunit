using System;
using System.Collections.Generic;
using System.Globalization;

namespace TcXunit.Interpreter
{
    // Recursive-descent parser for the fixture's statement/expression subset
    // (TcXunit-w5x.8/.12): assignment, REF=, IF/ELSE/END_IF, and calls
    // (builtin/self/THIS^/SUPER^/receiver-qualified) with positional or named
    // args, plus plain .Member field reads (TcXunit-w5x.15.7). No chained
    // member access beyond one level, no LHS deref - neither is exercised by
    // the fixture yet.
    public sealed class Parser
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

        private List<Stmt> ParseStatementList()
        {
            var stmts = new List<Stmt>();
            while (Current.Type != TokenType.Eof && !IsKeyword("ELSE") && !IsKeyword("END_IF"))
                stmts.Add(ParseStatement());
            return stmts;
        }

        private Stmt ParseStatement()
        {
            if (IsKeyword("IF"))
                return ParseIf();

            var target = ParsePostfix(ParsePrimary());

            if (Current.Type == TokenType.Assign)
            {
                Advance();
                var value = ParseExpr();
                Expect(TokenType.Semicolon);
                return new AssignStmt(RequireIdentifierName(target), value);
            }

            if (Current.Type == TokenType.RefAssign)
            {
                Advance();
                var value = ParseExpr();
                Expect(TokenType.Semicolon);
                return new RefAssignStmt(RequireIdentifierName(target), value);
            }

            if (target is CallExpr call)
            {
                Expect(TokenType.Semicolon);
                return new ExprStmt(call);
            }

            throw new FormatException($"Statement did not resolve to an assignment or call at token index {_pos}");
        }

        private static string RequireIdentifierName(Expr target)
        {
            if (target is IdentifierExpr id)
                return id.Name;
            throw new FormatException("Assignment target must be a plain identifier in the v1 subset");
        }

        private Stmt ParseIf()
        {
            Advance(); // IF
            var condition = ParseExpr();
            if (!IsKeyword("THEN"))
                throw new FormatException("Expected THEN");
            Advance();

            var thenBranch = ParseStatementList();
            var elseBranch = new List<Stmt>();
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

        private Expr ParseExpr() => ParseOr();

        private Expr ParseOr()
        {
            var left = ParseXor();
            while (IsKeyword("OR"))
            {
                Advance();
                left = new BinaryExpr("OR", left, ParseXor());
            }
            return left;
        }

        private Expr ParseXor()
        {
            var left = ParseAnd();
            while (IsKeyword("XOR"))
            {
                Advance();
                left = new BinaryExpr("XOR", left, ParseAnd());
            }
            return left;
        }

        private Expr ParseAnd()
        {
            var left = ParseComparison();
            while (IsKeyword("AND"))
            {
                Advance();
                left = new BinaryExpr("AND", left, ParseComparison());
            }
            return left;
        }

        private Expr ParseComparison()
        {
            var left = ParseAdditive();
            string op = Current.Type switch
            {
                TokenType.Eq => "=",
                TokenType.Lt => "<",
                TokenType.Gt => ">",
                TokenType.Le => "<=",
                TokenType.Ge => ">=",
                TokenType.Ne => "<>",
                _ => null,
            };
            if (op == null)
                return left;

            Advance();
            var right = ParseAdditive();
            return new BinaryExpr(op, left, right);
        }

        private Expr ParseAdditive()
        {
            var left = ParseMod();
            while (Current.Type == TokenType.Plus || Current.Type == TokenType.Minus)
            {
                var op = Current.Type == TokenType.Plus ? "+" : "-";
                Advance();
                var right = ParseMod();
                left = new BinaryExpr(op, left, right);
            }
            return left;
        }

        private Expr ParseMod()
        {
            var left = ParseUnary();
            while (IsKeyword("MOD"))
            {
                Advance();
                left = new BinaryExpr("MOD", left, ParseUnary());
            }
            return left;
        }

        private Expr ParseUnary()
        {
            if (IsKeyword("NOT"))
            {
                Advance();
                return new UnaryExpr("NOT", ParseUnary());
            }
            return ParsePostfix(ParsePrimary());
        }

        private Expr ParsePrimary()
        {
            switch (Current.Type)
            {
                case TokenType.IntLiteral:
                    return new IntLiteralExpr(int.Parse(Advance().Text));
                case TokenType.RealLiteral:
                    return new RealLiteralExpr(float.Parse(Advance().Text, CultureInfo.InvariantCulture));
                case TokenType.LrealLiteral:
                    return new LrealLiteralExpr(double.Parse(Advance().Text, CultureInfo.InvariantCulture));
                case TokenType.TimeLiteral:
                    return new TimeLiteralExpr(TimeLiteral.ParseTimeMs(Advance().Text));
                case TokenType.LtimeLiteral:
                    return new LtimeLiteralExpr(TimeLiteral.ParseLTimeNs(Advance().Text));
                case TokenType.StringLiteral:
                    return new StringLiteralExpr(Advance().Text);
                case TokenType.LParen:
                {
                    Advance();
                    var inner = ParseExpr();
                    Expect(TokenType.RParen);
                    return inner;
                }
                case TokenType.Identifier:
                {
                    var name = Advance().Text;

                    if (name == "THIS" && Current.Type == TokenType.Caret)
                    {
                        Advance();
                        return new ThisRefExpr();
                    }

                    if (name == "SUPER" && Current.Type == TokenType.Caret)
                    {
                        Advance();
                        var superNode = new SuperRefExpr();

                        // SUPER^(args) - TwinCAT shorthand for calling the base
                        // FB_init directly, distinct from SUPER^.Method(args).
                        if (Current.Type == TokenType.LParen)
                            return ParseCallArgs(superNode, "FB_init");

                        return superNode;
                    }

                    if (Current.Type == TokenType.LParen)
                        return ParseCallArgs(receiver: null, methodName: name);

                    return new IdentifierExpr(name);
                }
                default:
                    throw new FormatException($"Unexpected token {Current} at index {_pos}");
            }
        }

        // Applies postfix ^ (deref) and .Member(args) (call) operators to an
        // already-parsed primary/THIS^/SUPER^ node.
        private Expr ParsePostfix(Expr node)
        {
            while (true)
            {
                if (Current.Type == TokenType.Caret)
                {
                    Advance();
                    node = new DerefExpr(node);
                    continue;
                }

                if (Current.Type == TokenType.Dot)
                {
                    Advance();
                    var memberName = Expect(TokenType.Identifier).Text;
                    node = Current.Type == TokenType.LParen
                        ? ParseCallArgs(node, memberName)
                        : new FieldAccessExpr(node, memberName);
                    continue;
                }

                return node;
            }
        }

        private CallExpr ParseCallArgs(Expr receiver, string methodName)
        {
            Expect(TokenType.LParen);

            var positional = new List<Expr>();
            var named = new List<NamedArg>();

            if (Current.Type != TokenType.RParen)
            {
                do
                {
                    if (Current.Type == TokenType.Identifier && _tokens[_pos + 1].Type == TokenType.Assign)
                    {
                        var argName = Advance().Text;
                        Advance(); // :=
                        named.Add(new NamedArg(argName, ParseExpr()));
                    }
                    else
                    {
                        positional.Add(ParseExpr());
                    }
                } while (Current.Type == TokenType.Comma && Advance().Type == TokenType.Comma);
            }

            Expect(TokenType.RParen);
            return new CallExpr(receiver, methodName, positional, named);
        }
    }
}
