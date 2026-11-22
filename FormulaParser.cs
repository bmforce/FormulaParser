using System.Numerics;
using System.Globalization;

namespace FormulaParser;

public sealed class Parser<T>
    where T : struct, IFloatingPoint<T>
{
    private List<FormulaToken> _tokens = null!;
    private int _position;
    private readonly FormulaSyntax _syntax;

    public Parser(FormulaSyntax? syntax = null)
    {
        _syntax = syntax ?? FormulaSyntax.Default;
        _syntax.Validate();
    }

    public AstNode<T> Parse(string formula)
    {
        var tokenizer = new Tokenizer(formula);
        _tokens = tokenizer.Tokenize();
        _position = 0;

        var result = ParseExpression();

        if (Current.Type != TokenType.End)
            throw new FormatException($"Unexpected token at position {Current.Position}: {Current.Type}");

        return result;
    }

    private FormulaToken Current => _tokens[_position];

    private FormulaToken Consume(TokenType expected)
    {
        if (Current.Type != expected)
            throw new FormatException($"Expected {expected} at position {Current.Position}, but got {Current.Type}");

        var token = Current;
        _position++;
        return token;
    }

    private AstNode<T> ParseExpression()
    {
        return ParseAddSubtract();
    }

    private AstNode<T> ParseAddSubtract()
    {
        var left = ParseMultiplyDivide();

        while (Current.Type == TokenType.Plus || Current.Type == TokenType.Minus)
        {
            var op = Current.Type == TokenType.Plus ? BinaryOperator.Add : BinaryOperator.Subtract;
            _position++;
            var right = ParseMultiplyDivide();
            left = new BinaryOperationNode<T>(left, right, op);
        }

        return left;
    }

    private AstNode<T> ParseMultiplyDivide()
    {
        var left = ParseUnary();

        while (Current.Type == TokenType.Multiply || Current.Type == TokenType.Divide)
        {
            var op = Current.Type == TokenType.Multiply ? BinaryOperator.Multiply : BinaryOperator.Divide;
            _position++;
            var right = ParseUnary();
            left = new BinaryOperationNode<T>(left, right, op);
        }

        return left;
    }

    private AstNode<T> ParseUnary()
    {
        if (Current.Type == TokenType.Minus)
        {
            _position++;
            var operand = ParseUnary();
            return new BinaryOperationNode<T>(new NumberNode<T>(T.Zero), operand, BinaryOperator.Subtract);
        }

        if (Current.Type == TokenType.Plus)
        {
            _position++;
            return ParseUnary();
        }

        return ParsePrimary();
    }

    private AstNode<T> ParsePrimary()
    {
        switch (Current.Type)
        {
            case TokenType.Number:
                return ParseNumber();

            case TokenType.Parameter:
                return ParseParameter();

            case TokenType.Function:
                return ParseFunction();

            case TokenType.LeftParen:
                _position++;
                var expr = ParseExpression();
                Consume(TokenType.RightParen);

                // Проверяем, есть ли модификатор после скобки: ):modifier
                if (Current.Type == TokenType.Colon)
                {
                    _position++; // Skip ':'

                    if (Current.Type != TokenType.Function)
                        throw new FormatException($"Expected modifier identifier after ':' at position {Current.Position}");

                    var modifier = _syntax.Modifier(Current.Value.ToString());
                    _position++;
                    return new ModifiedExpressionNode<T>(expr, modifier);
                }

                return expr;

            default:
                throw new FormatException($"Unexpected token at position {Current.Position}: {Current.Type}");
        }
    }

    private AstNode<T> ParseNumber()
    {
        var token = Consume(TokenType.Number);
        if (!T.TryParse(token.Value.Span, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var value))
            throw new FormatException($"Invalid number at position {token.Position}");

        return new NumberNode<T>(value);
    }

    private AstNode<T> ParseParameter()
    {
        var token = Consume(TokenType.Parameter);
        var paramText = token.Value.Span;
        
        // Remove [ and ]
        paramText = paramText.Slice(1, paramText.Length - 2);

        var content = paramText.ToString();
        var colonIndex = content.IndexOf(':');
        var body = colonIndex >= 0 ? content[..colonIndex] : content;
        var modifier = colonIndex >= 0 ? content[(colonIndex + 1)..] : null;

        if (Current.Type == TokenType.Colon)
        {
            if (modifier != null)
                throw new FormatException($"Parameter already has a time modifier at position {Current.Position}");
            _position++;
            modifier = Consume(TokenType.Function).Value.ToString();
        }

        if (modifier != null) modifier = _syntax.Modifier(modifier);
        var parts = body.Split('.');
        var cursor = 0;
        long? objectId = null;
        ObjectSelectionStep? ancestor = null, descendant = null;
        var kind = ParameterKind.Custom;
        string parameterId;

        bool Is(string value, string keyword) => FormulaSyntax.Matches(value, keyword);
        bool Direction(string value) => Is(value, _syntax.Ancestors) || Is(value, _syntax.Descendants);
        if (Is(parts[0], _syntax.Object))
        {
            if (parts.Length < 4 || !long.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var id))
                throw new FormatException($"Invalid object reference at position {token.Position}");
            objectId = id;
            cursor = 2;
        }
        else if (_syntax.IsRetiredPrefix(parts[0]))
            throw new FormatException($"Keyword '{parts[0]}' has been replaced in the configured syntax.");

        if (Direction(parts[cursor]))
        {
            kind = ParameterKind.Parameter;
            while (cursor < parts.Length && Direction(parts[cursor]))
            {
                var direction = Is(parts[cursor++], _syntax.Ancestors) ? ObjectDirection.Ancestors : ObjectDirection.Descendants;
                var first = cursor;
                while (cursor < parts.Length && !Direction(parts[cursor]) && !Is(parts[cursor], _syntax.Parameter)) cursor++;
                var length = cursor - first;
                if (length is < 1 or > 2 || parts.Skip(first).Take(length).Any(string.IsNullOrWhiteSpace))
                    throw new FormatException("Expected [relation type.]object type after selection direction.");
                var step = new ObjectSelectionStep(direction, TypeIdentifier(parts[cursor - 1]), length == 2 ? TypeIdentifier(parts[first]) : null);
                if (direction == ObjectDirection.Ancestors)
                {
                    if (ancestor.HasValue || descendant.HasValue) throw new FormatException("Only anc, desc or anc -> desc are allowed.");
                    ancestor = step;
                }
                else
                {
                    if (descendant.HasValue) throw new FormatException("Only anc, desc or anc -> desc are allowed.");
                    descendant = step;
                }
            }
            if (cursor >= parts.Length || !Is(parts[cursor], _syntax.Parameter))
                throw new FormatException($"Expected '{_syntax.Parameter}' before parameter identifier.");
            parameterId = string.Join('.', parts.Skip(cursor + 1));
        }
        else if (Is(parts[cursor], _syntax.Parameter))
        {
            kind = ParameterKind.Parameter;
            parameterId = string.Join('.', parts.Skip(cursor + 1));
        }
        else
        {
            if (objectId.HasValue) throw new FormatException("Expected parameter prefix or object selection after object id.");
            parameterId = body;
        }
        if (string.IsNullOrWhiteSpace(parameterId))
            throw new FormatException($"Parameter name is empty at position {token.Position}");
        return new ParameterNode<T>(new ParameterInfo
        {
            Kind = kind, ObjectId = objectId, ParameterId = parameterId,
            Modifier = modifier, Ancestor = ancestor, Descendant = descendant
        });
    }

    private static string TypeIdentifier(string value)
    {
        if (long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id))
            return id.ToString(CultureInfo.InvariantCulture);
        if (FormulaSyntax.IsIdentifier(value)) return value;
        throw new FormatException($"Invalid type identifier '{value}'.");
    }

    private AstNode<T> ParseFunction()
    {
        var nameToken = Consume(TokenType.Function);
        var functionName = _syntax.Function(nameToken.Value.ToString());

        if (Current.Type != TokenType.LeftParen)
            throw new FormatException($"Expected '(' after function name at position {Current.Position}");

        _position++;

        var arguments = new List<AstNode<T>>();

        if (Current.Type != TokenType.RightParen)
        {
            arguments.Add(ParseExpression());

            while (Current.Type == TokenType.Comma)
            {
                _position++;
                arguments.Add(ParseExpression());
            }
        }

        Consume(TokenType.RightParen);

        return new FunctionCallNode<T>(functionName, arguments);
    }
}
