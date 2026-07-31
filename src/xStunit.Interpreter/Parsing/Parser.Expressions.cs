using System;
using System.Collections.Generic;
using System.Globalization;
using xStunit.Runner;

namespace xStunit.Interpreter
{
    public sealed partial class Parser
    {
        private Expr ParseExpr() => ParseOr();

        private Expr ParseOr()
        {
            var left = ParseXor();
            while (IsKeyword("OR") || IsKeyword("OR_ELSE"))
            {
                var op = Advance().Text;
                left = new BinaryExpr(op, left, ParseXor()) { Line = left.Line };
            }
            return left;
        }

        private Expr ParseXor()
        {
            var left = ParseAnd();
            while (IsKeyword("XOR"))
            {
                Advance();
                left = new BinaryExpr("XOR", left, ParseAnd()) { Line = left.Line };
            }
            return left;
        }

        private Expr ParseAnd()
        {
            var left = ParseComparison();
            while (IsKeyword("AND") || IsKeyword("AND_THEN"))
            {
                var op = Advance().Text;
                left = new BinaryExpr(op, left, ParseComparison()) { Line = left.Line };
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
            return new BinaryExpr(op, left, right) { Line = left.Line };
        }

        private Expr ParseAdditive()
        {
            var left = ParseMod();
            while (Current.Type == TokenType.Plus || Current.Type == TokenType.Minus)
            {
                var op = Current.Type == TokenType.Plus ? "+" : "-";
                Advance();
                var right = ParseMod();
                left = new BinaryExpr(op, left, right) { Line = left.Line };
            }
            return left;
        }

        private Expr ParseMod()
        {
            var left = ParseUnary();
            while (IsKeyword("MOD") || Current.Type == TokenType.Asterisk || Current.Type == TokenType.Slash)
            {
                // '**' (IEC 61131-3 §2.5.1.5 EXPT, highest-precedence
                // exponentiation) tokenizes as two adjacent Asterisk tokens -
                // valid grammar this parser does not implement, not malformed
                // source (TcXunit-3tx.5).
                if (Current.Type == TokenType.Asterisk && _pos + 1 < _tokens.Count && _tokens[_pos + 1].Type == TokenType.Asterisk)
                    throw new UnsupportedConstructException("**", "Exponentiation operator '**' is not supported yet");

                string op;
                if (Current.Type == TokenType.Asterisk)
                    op = "*";
                else if (Current.Type == TokenType.Slash)
                    op = "/";
                else
                    op = "MOD";
                Advance();
                left = new BinaryExpr(op, left, ParseUnary()) { Line = left.Line };
            }
            return left;
        }

        private Expr ParseUnary()
        {
            var line = Current.Line;
            if (IsKeyword("NOT"))
            {
                Advance();
                return new UnaryExpr("NOT", ParseUnary()) { Line = line };
            }
            if (Current.Type == TokenType.Minus)
            {
                Advance();
                return new UnaryExpr("-", ParseUnary()) { Line = line };
            }
            return ParsePostfix(ParsePrimary());
        }

        // Stamps every leaf/parenthesized/literal-initializer node with the
        // line of the token that opened it (TcXunit-p3t.2). Only stamps when
        // the node has no line yet: "(a + b)" hands back the inner node,
        // which already knows where it started, and the [n(v)] array-repeat
        // shorthand shares one Expr instance across n slots.
        private Expr ParsePrimary()
        {
            var line = Current.Line;
            var expr = ParsePrimaryCore();
            if (expr.Line == 0)
                expr.Line = line;
            return expr;
        }

        private Expr ParsePrimaryCore()
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
                    // Left as FormatException, not converted (TcXunit-3tx.5):
                    // every token type reaching here (Assign, Colon, operator
                    // tokens, RParen/RBracket, Eof, ...) is one an expression
                    // can never legally start with, in IEC 61131-3 or any
                    // extension of it - unlike '**' (handled by name in
                    // ParseMod before falling through here), there is no
                    // specific missing construct to name, only positions
                    // where the source itself is incomplete or wrong.
                    throw new ParseException($"Unexpected token {Current} at index {_pos}");
            }
        }

        // Applies postfix ^ (deref) and .Member(args) (call) operators to an
        // already-parsed primary/THIS^/SUPER^ node. Each wrapper node inherits
        // the receiver's line, since that is where the whole postfix chain
        // starts (TcXunit-p3t.2).
        private Expr ParsePostfix(Expr node)
        {
            while (true)
            {
                if (Current.Type == TokenType.Caret)
                {
                    Advance();
                    node = new DerefExpr(node) { Line = node.Line };
                    continue;
                }

                if (Current.Type == TokenType.Dot)
                {
                    Advance();
                    var memberName = Expect(TokenType.Identifier).Text;
                    Expr member = Current.Type == TokenType.LParen
                        ? ParseCallArgs(node, memberName)
                        : new FieldAccessExpr(node, memberName);
                    member.Line = node.Line;
                    node = member;
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
                    node = new IndexExpr(node, indices) { Line = node.Line };
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
