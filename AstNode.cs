namespace FormulaParser;

public abstract class AstNode
{
    public abstract ValueResult Evaluate(IFormulaContext context, DateTime timestamp);
    public virtual double? EvaluateScalar(IFormulaContext context, DateTime timestamp) =>
        Evaluate(context, timestamp).AsScalar();
    public virtual (double Sum, int Count) EvaluateSumAndCount(IFormulaContext context, DateTime timestamp) =>
        Evaluate(context, timestamp).SumAndCount();
    internal virtual bool IsTimestampIndependent => false;
    public abstract void CollectParameters(HybridSet<ParameterInfo> parameters);
    internal abstract TimestampDomain GetTimestampDomain(IFormulaContext context, DateTime from, DateTime to);
}

public sealed class NumberNode : AstNode
{
    public double Value { get; }

    public NumberNode(double value)
    {
        Value = value;
    }

    public override ValueResult Evaluate(IFormulaContext context, DateTime timestamp) => ValueResult.Scalar(Value);

    public override double? EvaluateScalar(IFormulaContext context, DateTime timestamp) => Value;

    public override (double Sum, int Count) EvaluateSumAndCount(IFormulaContext context, DateTime timestamp) =>
        (Value, 1);

    internal override bool IsTimestampIndependent => true;

    public override void CollectParameters(HybridSet<ParameterInfo> parameters)
    {
    }

    internal override TimestampDomain GetTimestampDomain(IFormulaContext context, DateTime from, DateTime to) =>
        TimestampDomain.Any;

    public override string ToString() => Value.ToString();
}

public sealed class ParameterNode : AstNode
{
    public ParameterInfo Parameter { get; }

    public ParameterNode(ParameterInfo parameter)
    {
        Parameter = parameter;
    }

    public override ValueResult Evaluate(IFormulaContext context, DateTime timestamp) =>
        context.GetParameterValue(Parameter, timestamp);

    public override double? EvaluateScalar(IFormulaContext context, DateTime timestamp) =>
        context.GetParameterScalar(Parameter, timestamp);

    public override (double Sum, int Count) EvaluateSumAndCount(IFormulaContext context, DateTime timestamp)
    {
        if (Parameter.Modifier != null)
            return Evaluate(context, timestamp).SumAndCount();

        var value = EvaluateScalar(context, timestamp);
        return value.HasValue ? (value.Value, 1) : (0, 0);
    }

    internal override bool IsTimestampIndependent =>
        Parameter.Kind == ParameterKind.Custom && Parameter.Modifier == null;

    public override void CollectParameters(HybridSet<ParameterInfo> parameters)
    {
        parameters.Add(Parameter);
    }

    internal override TimestampDomain GetTimestampDomain(IFormulaContext context, DateTime from, DateTime to) =>
        Parameter.Kind == ParameterKind.Custom
            ? TimestampDomain.Any
            : TimestampDomain.From(context.GetParameterTimestamps(Parameter, from, to));

    public override string ToString() => Parameter.ToString();
}

public sealed class BinaryOperationNode : AstNode
{
    public AstNode Left { get; }
    public AstNode Right { get; }
    public BinaryOperator Operator { get; }

    public BinaryOperationNode(AstNode left, AstNode right, BinaryOperator op)
    {
        Left = left;
        Right = right;
        Operator = op;
    }

    public override ValueResult Evaluate(IFormulaContext context, DateTime timestamp)
    {
        return ValueResult.Scalar(EvaluateScalar(context, timestamp));
    }

    public override double? EvaluateScalar(IFormulaContext context, DateTime timestamp)
    {
        var leftValue = Left.EvaluateScalar(context, timestamp);
        var rightValue = Right.EvaluateScalar(context, timestamp);

        if (!leftValue.HasValue || !rightValue.HasValue)
            return null;

        return Operator switch
        {
            BinaryOperator.Add => leftValue.Value + rightValue.Value,
            BinaryOperator.Subtract => leftValue.Value - rightValue.Value,
            BinaryOperator.Multiply => leftValue.Value * rightValue.Value,
            BinaryOperator.Divide => leftValue.Value / rightValue.Value,
            _ => throw new InvalidOperationException($"Unknown operator: {Operator}")
        };
    }

    public override (double Sum, int Count) EvaluateSumAndCount(IFormulaContext context, DateTime timestamp)
    {
        var value = EvaluateScalar(context, timestamp);
        return value.HasValue ? (value.Value, 1) : (0, 0);
    }

    internal override bool IsTimestampIndependent =>
        Left.IsTimestampIndependent && Right.IsTimestampIndependent;

    public override void CollectParameters(HybridSet<ParameterInfo> parameters)
    {
        Left.CollectParameters(parameters);
        Right.CollectParameters(parameters);
    }

    internal override TimestampDomain GetTimestampDomain(IFormulaContext context, DateTime from, DateTime to) =>
        Left.GetTimestampDomain(context, from, to).Intersect(Right.GetTimestampDomain(context, from, to));

    public override string ToString() => $"({Left} {Operator} {Right})";
}

public sealed class FunctionCallNode : AstNode
{
    public string FunctionName { get; }
    private readonly string _normalizedName;
    public IReadOnlyList<AstNode> Arguments { get; }

    public FunctionCallNode(string functionName, List<AstNode> arguments)
    {
        FunctionName = functionName;
        _normalizedName = functionName.ToLowerInvariant();
        Arguments = arguments;
    }

    public override ValueResult Evaluate(IFormulaContext context, DateTime timestamp)
    {
        return ValueResult.Scalar(EvaluateScalar(context, timestamp));
    }

    public override double? EvaluateScalar(IFormulaContext context, DateTime timestamp)
    {
        return _normalizedName switch
        {
            FormulaKeywords.AVG => EvaluateAvg(context, timestamp),
            FormulaKeywords.SUM => EvaluateSum(context, timestamp),
            FormulaKeywords.MONTH_DAYS => context.GetMonthDays(timestamp),
            _ => throw new InvalidOperationException($"Unknown function: {FunctionName}")
        };
    }

    private double? EvaluateAvg(IFormulaContext context, DateTime timestamp)
    {
        if (Arguments.Count == 0)
            throw new InvalidOperationException("avg() requires at least one argument");

        double totalSum = 0;
        int totalCount = 0;

        foreach (var arg in Arguments)
        {
            var (s, c) = arg.EvaluateSumAndCount(context, timestamp);
            totalSum += s;
            totalCount += c;
        }

        return totalCount == 0 ? null : totalSum / totalCount;
    }

    private double? EvaluateSum(IFormulaContext context, DateTime timestamp)
    {
        var sum = 0.0;
        var count = 0;

        foreach (var arg in Arguments)
        {
            var (argumentSum, argumentCount) = arg.EvaluateSumAndCount(context, timestamp);
            if (argumentCount == 0)
                continue;

            sum += argumentSum;
            count += argumentCount;
        }

        return count == 0 ? null : sum;
    }

    public override void CollectParameters(HybridSet<ParameterInfo> parameters)
    {
        foreach (var arg in Arguments)
            arg.CollectParameters(parameters);
    }

    internal override TimestampDomain GetTimestampDomain(IFormulaContext context, DateTime from, DateTime to)
    {
        if (_normalizedName == FormulaKeywords.MONTH_DAYS)
            return TimestampDomain.Any;

        var result = TimestampDomain.Empty;
        var hasConcreteDomain = false;

        foreach (var arg in Arguments)
        {
            var domain = arg.GetTimestampDomain(context, from, to);
            if (domain.IsAny)
                continue;

            result = hasConcreteDomain ? result.Union(domain) : domain;
            hasConcreteDomain = true;
        }

        return hasConcreteDomain ? result : TimestampDomain.Any;
    }

    public override string ToString() => $"{FunctionName}({string.Join(", ", Arguments)})";
}

public enum BinaryOperator
{
    Add,
    Subtract,
    Multiply,
    Divide
}

/// <summary>
/// Узел для выражений с модификатором времени: (expression):modifier
/// </summary>
public sealed class ModifiedExpressionNode : AstNode
{
    public AstNode Expression { get; }
    public string Modifier { get; }
    private readonly string _normalizedModifier;

    public ModifiedExpressionNode(AstNode expression, string modifier)
    {
        Expression = expression;
        Modifier = modifier;
        _normalizedModifier = modifier.ToLowerInvariant();
    }

    public override ValueResult Evaluate(IFormulaContext context, DateTime timestamp)
    {
        var (startDate, endDate) = GetDateRange(timestamp);
        var timestampDomain = Expression.GetTimestampDomain(context, startDate, endDate);
        var timestamps = timestampDomain.IsAny
            ? ReadOnlySpan<DateTime>.Empty
            : timestampDomain.Timestamps.Span;
        var results = new double[timestamps.Length];
        var hasValue = new bool[timestamps.Length];

        for (var i = 0; i < timestamps.Length; i++)
        {
            var value = Expression.EvaluateScalar(context, timestamps[i]);
            if (!value.HasValue)
                continue;

            results[i] = value.Value;
            hasValue[i] = true;
        }

        return ValueResult.Array(results, hasValue);
    }

    public override (double Sum, int Count) EvaluateSumAndCount(IFormulaContext context, DateTime timestamp)
    {
        var (startDate, endDate) = GetDateRange(timestamp);
        if (TryEvaluateScaledSum(context, timestamp, startDate, endDate, out var result))
            return result;

        return EvaluateExpressionSumAndCount(context, timestamp, Expression, startDate, endDate);
    }

    private static (double Sum, int Count) EvaluateExpressionSumAndCount(
        IFormulaContext context,
        DateTime timestamp,
        AstNode expression,
        DateTime startDate,
        DateTime endDate)
    {
        if (TryEvaluatePlainParameterBinarySum(context, expression, startDate, endDate, out var directResult))
            return directResult;

        var timestampDomain = expression.GetTimestampDomain(context, startDate, endDate);
        if (timestampDomain.IsAny)
            return (0, 0);

        var timestamps = timestampDomain.Timestamps.Span;
        var sum = 0.0;
        var count = 0;

        for (var i = 0; i < timestamps.Length; i++)
        {
            var value = expression.EvaluateScalar(context, timestamps[i]);
            if (!value.HasValue)
                continue;

            sum += value.Value;
            count++;
        }

        return (sum, count);
    }

    private static bool TryEvaluatePlainParameterBinarySum(
        IFormulaContext context,
        AstNode expression,
        DateTime startDate,
        DateTime endDate,
        out (double Sum, int Count) result)
    {
        result = default;

        if (expression is not BinaryOperationNode binary)
            return false;

        if (!TryGetPlainRegularParameter(binary.Left, out var leftParameter) ||
            !TryGetPlainRegularParameter(binary.Right, out var rightParameter))
        {
            return false;
        }

        var leftSeries = context.GetParameterDataSeries(leftParameter);
        var rightSeries = context.GetParameterDataSeries(rightParameter);

        var (leftStart, leftEndExclusive) = leftSeries.GetRangeBounds(startDate, endDate);
        var (rightStart, rightEndExclusive) = rightSeries.GetRangeBounds(startDate, endDate);

        var leftTimestamps = leftSeries.Timestamps.Span.Slice(leftStart, leftEndExclusive - leftStart);
        var leftValues = leftSeries.Values.Span.Slice(leftStart, leftEndExclusive - leftStart);
        var rightTimestamps = rightSeries.Timestamps.Span.Slice(rightStart, rightEndExclusive - rightStart);
        var rightValues = rightSeries.Values.Span.Slice(rightStart, rightEndExclusive - rightStart);

        var leftIndex = 0;
        var rightIndex = 0;
        var sum = 0.0;
        var count = 0;

        while (leftIndex < leftTimestamps.Length && rightIndex < rightTimestamps.Length)
        {
            var leftTimestamp = leftTimestamps[leftIndex];
            var rightTimestamp = rightTimestamps[rightIndex];

            if (leftTimestamp == rightTimestamp)
            {
                var leftValue = leftValues[leftIndex];
                var rightValue = rightValues[rightIndex];
                sum += binary.Operator switch
                {
                    BinaryOperator.Add => leftValue + rightValue,
                    BinaryOperator.Subtract => leftValue - rightValue,
                    BinaryOperator.Multiply => leftValue * rightValue,
                    BinaryOperator.Divide => leftValue / rightValue,
                    _ => throw new InvalidOperationException($"Unknown operator: {binary.Operator}")
                };

                count++;
                leftIndex++;
                rightIndex++;
            }
            else if (leftTimestamp < rightTimestamp)
            {
                leftIndex++;
            }
            else
            {
                rightIndex++;
            }
        }

        result = (sum, count);
        return true;
    }

    private static bool TryGetPlainRegularParameter(AstNode node, out ParameterInfo parameter)
    {
        if (node is ParameterNode parameterNode &&
            parameterNode.Parameter.Kind == ParameterKind.Parameter &&
            parameterNode.Parameter.Modifier == null)
        {
            parameter = parameterNode.Parameter;
            return true;
        }

        parameter = null!;
        return false;
    }

    private bool TryEvaluateScaledSum(
        IFormulaContext context,
        DateTime timestamp,
        DateTime startDate,
        DateTime endDate,
        out (double Sum, int Count) result)
    {
        result = default;

        if (Expression is not BinaryOperationNode { Operator: BinaryOperator.Multiply } binary)
            return false;

        if (binary.Left.IsTimestampIndependent == binary.Right.IsTimestampIndependent)
            return false;

        var scaleExpression = binary.Left.IsTimestampIndependent ? binary.Left : binary.Right;
        var seriesExpression = binary.Left.IsTimestampIndependent ? binary.Right : binary.Left;
        var scale = scaleExpression.EvaluateScalar(context, timestamp);
        if (!scale.HasValue)
        {
            result = (0, 0);
            return true;
        }

        var (sum, count) = EvaluateExpressionSumAndCount(
            context,
            timestamp,
            seriesExpression,
            startDate,
            endDate);

        result = (sum * scale.Value, count);
        return true;
    }

    private (DateTime Start, DateTime End) GetDateRange(DateTime calculationDate)
    {
        var yearBegin = new DateTime(calculationDate.Year, 1, 1);
        var monthBegin = new DateTime(calculationDate.Year, calculationDate.Month, 1);

        return _normalizedModifier switch
        {
            FormulaKeywords.YEAR_NOW => (yearBegin, calculationDate),
            FormulaKeywords.MONTH_NOW => (monthBegin, calculationDate),
            FormulaKeywords.MONTH_BEGIN => (monthBegin, monthBegin),
            _ => throw new InvalidOperationException($"Unknown time modifier: {Modifier}")
        };
    }

    public override void CollectParameters(HybridSet<ParameterInfo> parameters)
    {
        // Собираем параметры из выражения
        var innerParams = new HybridSet<ParameterInfo>();
        Expression.CollectParameters(innerParams);

        // Проверяем, что внутри выражения нет параметров с модификаторами времени
        foreach (var param in innerParams.ToArray())
        {
            if (param.Modifier != null)
            {
                throw new InvalidOperationException(
                    $"Parameter {param} inside modified expression ({Expression}:{Modifier}) " +
                    "cannot have time modifier. Use the same parameter without modifier.");
            }
            parameters.Add(param);
        }
    }

    public override string ToString() => $"({Expression}:{Modifier})";

    internal override TimestampDomain GetTimestampDomain(IFormulaContext context, DateTime from, DateTime to) =>
        TimestampDomain.Empty;
}
