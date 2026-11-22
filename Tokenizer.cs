using System.Buffers;

namespace FormulaParser;

public sealed class Tokenizer
{
    private ReadOnlyMemory<char> _formula;
    private int _position;

    public Tokenizer(string formula)
    {
        _formula = formula.AsMemory();
        _position = 0;
    }

    public List<FormulaToken> Tokenize()
    {
        var tokens = new List<FormulaToken>();
        
        while (_position < _formula.Length)
        {
            var token = ReadNextToken();
            if (token.Type == TokenType.End)
                break;
            tokens.Add(token);
        }

        tokens.Add(new FormulaToken(TokenType.End, ReadOnlyMemory<char>.Empty, _position));
        return tokens;
    }

    private FormulaToken ReadNextToken()
    {
        SkipWhitespace();

        if (_position >= _formula.Length)
            return new FormulaToken(TokenType.End, ReadOnlyMemory<char>.Empty, _position);

        var span = _formula.Span;
        var ch = span[_position];
        var startPos = _position;

        return ch switch
        {
            '+' => CreateSingleCharToken(TokenType.Plus),
            '-' => CreateSingleCharToken(TokenType.Minus),
            '*' => CreateSingleCharToken(TokenType.Multiply),
            '/' => CreateSingleCharToken(TokenType.Divide),
            '(' => CreateSingleCharToken(TokenType.LeftParen),
            ')' => CreateSingleCharToken(TokenType.RightParen),
            ',' => CreateSingleCharToken(TokenType.Comma),
            ':' => CreateSingleCharToken(TokenType.Colon),
            '[' => ReadParameter(),
            >= '0' and <= '9' => ReadNumber(),
            _ when char.IsLetter(ch) || ch == '_' => ReadIdentifier(),
            _ => throw new FormatException($"Unexpected character '{ch}' at position {_position}")
        };
    }

    private FormulaToken CreateSingleCharToken(TokenType type)
    {
        var token = new FormulaToken(type, _formula.Slice(_position, 1), _position);
        _position++;
        return token;
    }

    private FormulaToken ReadParameter()
    {
        var startPos = _position;
        var span = _formula.Span;
        
        _position++; // Skip '['

        while (_position < span.Length && span[_position] != ']')
            _position++;

        if (_position >= span.Length)
            throw new FormatException($"Unclosed parameter at position {startPos}");

        _position++; // Skip ']'

        var value = _formula.Slice(startPos, _position - startPos);
        return new FormulaToken(TokenType.Parameter, value, startPos);
    }

    private FormulaToken ReadNumber()
    {
        var startPos = _position;
        var span = _formula.Span;
        var hasDot = false;

        while (_position < span.Length)
        {
            var ch = span[_position];
            if (ch >= '0' && ch <= '9')
            {
                _position++;
            }
            else if (ch == '.' && !hasDot)
            {
                hasDot = true;
                _position++;
            }
            else
            {
                break;
            }
        }

        var value = _formula.Slice(startPos, _position - startPos);
        return new FormulaToken(TokenType.Number, value, startPos);
    }

    private FormulaToken ReadIdentifier()
    {
        var startPos = _position;
        var span = _formula.Span;

        while (_position < span.Length)
        {
            var ch = span[_position];
            if (char.IsLetterOrDigit(ch) || ch == '_')
            {
                _position++;
            }
            else
            {
                break;
            }
        }

        var value = _formula.Slice(startPos, _position - startPos);
        return new FormulaToken(TokenType.Function, value, startPos);
    }

    private void SkipWhitespace()
    {
        var span = _formula.Span;
        while (_position < span.Length && char.IsWhiteSpace(span[_position]))
            _position++;
    }
}
