namespace FormulaParser;

public sealed class Parser
{
    private List<FormulaToken> _tokens = null!;
    private int _position;

    public AstNode Parse(string formula)
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

    private AstNode ParseExpression()
    {
        return ParseAddSubtract();
    }

    private AstNode ParseAddSubtract()
    {
        var left = ParseMultiplyDivide();

        while (Current.Type == TokenType.Plus || Current.Type == TokenType.Minus)
        {
            var op = Current.Type == TokenType.Plus ? BinaryOperator.Add : BinaryOperator.Subtract;
            _position++;
            var right = ParseMultiplyDivide();
            left = new BinaryOperationNode(left, right, op);
        }

        return left;
    }

    private AstNode ParseMultiplyDivide()
    {
        var left = ParseUnary();

        while (Current.Type == TokenType.Multiply || Current.Type == TokenType.Divide)
        {
            var op = Current.Type == TokenType.Multiply ? BinaryOperator.Multiply : BinaryOperator.Divide;
            _position++;
            var right = ParseUnary();
            left = new BinaryOperationNode(left, right, op);
        }

        return left;
    }

    private AstNode ParseUnary()
    {
        if (Current.Type == TokenType.Minus)
        {
            _position++;
            var operand = ParseUnary();
            return new BinaryOperationNode(new NumberNode(0), operand, BinaryOperator.Subtract);
        }

        if (Current.Type == TokenType.Plus)
        {
            _position++;
            return ParseUnary();
        }

        return ParsePrimary();
    }

    private AstNode ParsePrimary()
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

                    var modifier = Current.Value.ToString();
                    _position++;
                    return new ModifiedExpressionNode(expr, modifier);
                }

                return expr;

            default:
                throw new FormatException($"Unexpected token at position {Current.Position}: {Current.Type}");
        }
    }

    private AstNode ParseNumber()
    {
        var token = Consume(TokenType.Number);
        if (!double.TryParse(token.Value.Span, out var value))
            throw new FormatException($"Invalid number at position {token.Position}");

        return new NumberNode(value);
    }

    private AstNode ParseParameter()
    {
        var token = Consume(TokenType.Parameter);
        var paramText = token.Value.Span;
        
        // Remove [ and ]
        paramText = paramText.Slice(1, paramText.Length - 2);

        var content = paramText.ToString();
        var colonIndex = content.IndexOf(':');
        var body = colonIndex >= 0 ? content[..colonIndex] : content;
        var modifier = colonIndex >= 0 ? content[(colonIndex + 1)..] : null;

        var kind = ParameterKind.Custom;
        int? objectId = null;
        string parameterId;

        if (body.StartsWith(FormulaKeywords.OBJ_PREFIX, StringComparison.OrdinalIgnoreCase))
        {
            kind = ParameterKind.Parameter;

            var parameterSeparator = body.IndexOf(FormulaKeywords.OBJECT_PARAMETER_SEPARATOR, StringComparison.OrdinalIgnoreCase);
            if (parameterSeparator < 0)
                throw new FormatException($"Expected '.p.' in object parameter at position {token.Position}");

            var objectText = body[FormulaKeywords.OBJ_PREFIX.Length..parameterSeparator];
            if (!int.TryParse(objectText, out var parsedObjectId))
                throw new FormatException($"Invalid object id '{objectText}' at position {token.Position}");

            objectId = parsedObjectId;
            parameterId = body[(parameterSeparator + FormulaKeywords.OBJECT_PARAMETER_SEPARATOR.Length)..];
        }
        else if (body.StartsWith(FormulaKeywords.PARAMETER_PREFIX, StringComparison.OrdinalIgnoreCase))
        {
            kind = ParameterKind.Parameter;
            parameterId = body[FormulaKeywords.PARAMETER_PREFIX.Length..];
        }
        else
        {
            parameterId = body;
        }

        if (string.IsNullOrWhiteSpace(parameterId))
            throw new FormatException($"Parameter name is empty at position {token.Position}");

        var paramInfo = new ParameterInfo
        {
            Kind = kind,
            ObjectId = objectId,
            ParameterId = parameterId,
            Modifier = modifier
        };

        return new ParameterNode(paramInfo);
    }

    private AstNode ParseFunction()
    {
        var nameToken = Consume(TokenType.Function);
        var functionName = nameToken.Value.ToString();

        if (Current.Type != TokenType.LeftParen)
            throw new FormatException($"Expected '(' after function name at position {Current.Position}");

        _position++;

        var arguments = new List<AstNode>();

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

        return new FunctionCallNode(functionName, arguments);
    }
}
