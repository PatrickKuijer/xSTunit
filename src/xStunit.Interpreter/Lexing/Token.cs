namespace xStunit.Interpreter
{
    public enum TokenType
    {
        Identifier,
        IntLiteral,
        RealLiteral,
        LrealLiteral,
        TimeLiteral,
        LtimeLiteral,
        DateLiteral,
        DateAndTimeLiteral,
        TimeOfDayLiteral,
        StringLiteral,
        Assign,      // :=
        RefAssign,   // REF=
        Colon,       // :
        DotDot,      // ..
        Eq,          // =
        Lt,          // <
        Gt,          // >
        Le,          // <=
        Ge,          // >=
        Ne,          // <>
        Plus,
        Minus,
        Asterisk,    // *
        Slash,       // /
        Arrow,       // => (VAR_OUTPUT call-arg binding)
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

        // 1-based line WITHIN THE ST BODY STRING passed to Lexer.Tokenize -
        // the body's first line is line 1, NOT the .TcPOU file line
        // (TcXunit-p3t.2). For a token spanning lines (multi-line string
        // literal) this is the line it starts on.
        //
        // To turn this into a real file line, combine it with the body's
        // origin recorded by the .TcPOU parser (TcXunit-p3t.3):
        //
        //     fileLine = MethodAst.BodyStartLine + node.Line - 1
        //
        // (same formula for PouAst.BodyStartLine), because BodyStartLine is
        // itself the 1-based file line of body line 1. Do NOT add the two
        // raw numbers - that double-counts the first line.
        //
        // 0 means "unknown": hand-built tokens that never went through the
        // lexer. Every token the lexer emits has Line >= 1.
        public int Line;

        public Token(TokenType type, string text, int line = 0)
        {
            Type = type;
            Text = text;
            Line = line;
        }

        public override string ToString() => $"{Type}:{Text}";
    }
}
