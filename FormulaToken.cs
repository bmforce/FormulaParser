namespace FormulaParser;

public enum TokenType
{
    Number,
    Parameter,
    Plus,
    Minus,
    Multiply,
    Divide,
    LeftParen,
    RightParen,
    Function,
    Comma,
    Colon,
    End
}

public readonly struct FormulaToken
{
    public TokenType Type { get; init; }
    public ReadOnlyMemory<char> Value { get; init; }
    public int Position { get; init; }

    public FormulaToken(TokenType type, ReadOnlyMemory<char> value, int position)
    {
        Type = type;
        Value = value;
        Position = position;
    }

    public override string ToString() => $"{Type}: {Value}";
}
