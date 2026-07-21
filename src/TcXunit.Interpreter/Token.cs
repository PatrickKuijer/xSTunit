namespace TcXunit.Interpreter
{
    public enum TokenType
    {
        Identifier,
        IntLiteral,
        RealLiteral,
        LrealLiteral,
        TimeLiteral,
        LtimeLiteral,
        StringLiteral,
        Assign,      // :=
        RefAssign,   // REF=
        Eq,          // =
        Lt,          // <
        Gt,          // >
        Le,          // <=
        Ge,          // >=
        Ne,          // <>
        Plus,
        Minus,
        Caret,       // ^
        Dot,
        Comma,
        Semicolon,
        LParen,
        RParen,
        LBracket,
        RBracket,
        Eof,
    }

    public struct Token
    {
        public TokenType Type;
        public string Text;

        public Token(TokenType type, string text)
        {
            Type = type;
            Text = text;
        }

        public override string ToString() => $"{Type}:{Text}";
    }
}
