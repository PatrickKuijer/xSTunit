using System;
using System.Collections.Generic;
using System.Globalization;

namespace TcXunit.Interpreter
{
    public sealed partial class Parser
    {
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
            while (IsKeyword("MOD") || Current.Type == TokenType.Asterisk || Current.Type == TokenType.Slash)
            {
                string op;
                if (Current.Type == TokenType.Asterisk)
                    op = "*";
                else if (Current.Type == TokenType.Slash)
                    op = "/";
                else
                    op = "MOD";
                Advance();
                left = new BinaryExpr(op, left, ParseUnary());
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
            if (Current.Type == TokenType.Minus)
            {
                Advance();
                return new UnaryExpr("-", ParseUnary());
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
                case TokenType.DateLiteral:
                    return new DateLiteralExpr(DateTimeLiteral.ParseDateSeconds(Advance().Text));
                case TokenType.DateAndTimeLiteral:
                    return new DateAndTimeLiteralExpr(DateTimeLiteral.ParseDateAndTimeSeconds(Advance().Text));
                case TokenType.TimeOfDayLiteral:
                    return new TimeOfDayLiteralExpr(DateTimeLiteral.ParseTimeOfDayMs(Advance().Text));
                case TokenType.StringLiteral:
                    return new StringLiteralExpr(Advance().Text);
                case TokenType.LParen:
                {
                    Advance();
                    if (Current.Type == TokenType.Identifier && _tokens[_pos + 1].Type == TokenType.Assign)
                        return ParseStructLiteral();
                    var inner = ParseExpr();
                    Expect(TokenType.RParen);
                    return inner;
                }
                case TokenType.LBracket:
                    return ParseArrayLiteral();
                case TokenType.Identifier:
                {
                    var name = Advance().Text;

                    if (string.Equals(name, "TRUE", StringComparison.OrdinalIgnoreCase))
                        return new BoolLiteralExpr(true);

                    if (string.Equals(name, "FALSE", StringComparison.OrdinalIgnoreCase))
                        return new BoolLiteralExpr(false);

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

                if (Current.Type == TokenType.LBracket)
                {
                    Advance();
                    var indices = new List<Expr> { ParseExpr() };
                    while (Current.Type == TokenType.Comma)
                    {
                        Advance();
                        indices.Add(ParseExpr());
                    }
                    Expect(TokenType.RBracket);
                    node = new IndexExpr(node, indices);
                    continue;
                }

                return node;
            }
        }

        // LParen already consumed by the caller's lookahead.
        private Expr ParseStructLiteral()
        {
            var fieldInits = new List<NamedArg>();
            do
            {
                var name = Expect(TokenType.Identifier).Text;
                Expect(TokenType.Assign);
                fieldInits.Add(new NamedArg(name, ParseExpr()));
            } while (Current.Type == TokenType.Comma && Advance().Type == TokenType.Comma);

            Expect(TokenType.RParen);
            return new StructLiteralExpr(fieldInits);
        }

        // [v0, v1, ...] with the [n(v)] repeat shorthand expanded inline.
        private Expr ParseArrayLiteral()
        {
            Expect(TokenType.LBracket);

            var elements = new List<Expr>();
            if (Current.Type != TokenType.RBracket)
            {
                do
                {
                    if (Current.Type == TokenType.IntLiteral && _tokens[_pos + 1].Type == TokenType.LParen)
                    {
                        var count = int.Parse(Advance().Text);
                        Advance(); // (
                        var value = ParseExpr();
                        Expect(TokenType.RParen);
                        for (var i = 0; i < count; i++)
                            elements.Add(value);
                    }
                    else
                    {
                        elements.Add(ParseExpr());
                    }
                } while (Current.Type == TokenType.Comma && Advance().Type == TokenType.Comma);
            }

            Expect(TokenType.RBracket);
            return new ArrayLiteralExpr(elements);
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
                    else if (Current.Type == TokenType.Identifier && _tokens[_pos + 1].Type == TokenType.Arrow)
                    {
                        // Name => expr: VAR_OUTPUT binding syntax. Recorded as
                        // a named arg with IsOutput set; BindParams' Input/
                        // InOut-only lookup ignores it, and CallMethod's
                        // WriteBackOutputArgs (TcXunit-wmh) writes the
                        // callee's output value back into this arg's lvalue
                        // after the call returns.
                        var argName = Advance().Text;
                        Advance(); // =>
                        named.Add(new NamedArg(argName, ParseExpr(), isOutput: true));
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
