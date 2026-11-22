using System.Numerics;
namespace FormulaParser;

public abstract class AstNode<T>
    where T : struct, IFloatingPoint<T>
{
    public abstract ValueResult<T> Evaluate(IFormulaContext<T> context, DateTime timestamp);
    public virtual T? EvaluateScalar(IFormulaContext<T> context, DateTime timestamp) =>
        Evaluate(context, timestamp).AsScalar();
    public virtual Aggregate<T> EvaluateAggregate(IFormulaContext<T> context, DateTime timestamp) =>
        Evaluate(context, timestamp).GetAggregate();
    internal virtual bool IsTimestampIndependent => false;
    public abstract void CollectParameters(HybridSet<ParameterInfo> parameters);
    internal abstract TimestampDomain GetTimestampDomain(IFormulaContext<T> context, DateTime from, DateTime to);
}

public sealed class NumberNode<T> : AstNode<T>
    where T : struct, IFloatingPoint<T>
{
    public T Value { get; }

    public NumberNode(T value)
    {
        Value = value;
    }

    public override ValueResult<T> Evaluate(IFormulaContext<T> context, DateTime timestamp) => ValueResult<T>.Scalar(Value);

    public override T? EvaluateScalar(IFormulaContext<T> context, DateTime timestamp) => Value;

    public override Aggregate<T> EvaluateAggregate(IFormulaContext<T> context, DateTime timestamp) =>
        Aggregate<T>.FromValue(Value);

    internal override bool IsTimestampIndependent => true;

    public override void CollectParameters(HybridSet<ParameterInfo> parameters)
    {
    }

    internal override TimestampDomain GetTimestampDomain(IFormulaContext<T> context, DateTime from, DateTime to) =>
        TimestampDomain.Any;

    public override string ToString() => Value.ToString() ?? string.Empty;
}

public sealed class ParameterNode<T> : AstNode<T>
    where T : struct, IFloatingPoint<T>
{
    public ParameterInfo Parameter { get; }

    public ParameterNode(ParameterInfo parameter)
    {
        Parameter = parameter;
    }

    public override ValueResult<T> Evaluate(IFormulaContext<T> context, DateTime timestamp) =>
        context.GetParameterValue(Parameter, timestamp);

    public override T? EvaluateScalar(IFormulaContext<T> context, DateTime timestamp) =>
        context.GetParameterScalar(Parameter, timestamp);

    public override Aggregate<T> EvaluateAggregate(IFormulaContext<T> context, DateTime timestamp)
    {
        if (Parameter.Modifier != null)
            return Evaluate(context, timestamp).GetAggregate();

        var value = EvaluateScalar(context, timestamp);
        return Aggregate<T>.FromValue(value);
    }

    internal override bool IsTimestampIndependent =>
        Parameter.Kind == ParameterKind.Custom && Parameter.Modifier == null;

    public override void CollectParameters(HybridSet<ParameterInfo> parameters)
    {
        parameters.Add(Parameter);
    }

    internal override TimestampDomain GetTimestampDomain(IFormulaContext<T> context, DateTime from, DateTime to) =>
        Parameter.Kind == ParameterKind.Custom
            ? TimestampDomain.Any
            : TimestampDomain.From(context.GetParameterTimestamps(Parameter, from, to));

    public override string ToString() => Parameter.ToString();
}

public sealed class BinaryOperationNode<T> : AstNode<T>
    where T : struct, IFloatingPoint<T>
{
    public AstNode<T> Left { get; }
    public AstNode<T> Right { get; }
    public BinaryOperator Operator { get; }

    public BinaryOperationNode(AstNode<T> left, AstNode<T> right, BinaryOperator op)
    {
        Left = left;
        Right = right;
        Operator = op;
    }

    public override ValueResult<T> Evaluate(IFormulaContext<T> context, DateTime timestamp)
    {
        return ValueResult<T>.Scalar(EvaluateScalar(context, timestamp));
    }

    public override T? EvaluateScalar(IFormulaContext<T> context, DateTime timestamp)
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

    public override Aggregate<T> EvaluateAggregate(IFormulaContext<T> context, DateTime timestamp)
    {
        var value = EvaluateScalar(context, timestamp);
        return Aggregate<T>.FromValue(value);
    }

    internal override bool IsTimestampIndependent =>
        Left.IsTimestampIndependent && Right.IsTimestampIndependent;

    public override void CollectParameters(HybridSet<ParameterInfo> parameters)
    {
        Left.CollectParameters(parameters);
        Right.CollectParameters(parameters);
    }

    internal override TimestampDomain GetTimestampDomain(IFormulaContext<T> context, DateTime from, DateTime to) =>
        Left.GetTimestampDomain(context, from, to).Intersect(Right.GetTimestampDomain(context, from, to));

    public override string ToString() => $"({Left} {Operator} {Right})";
}

public sealed class FunctionCallNode<T> : AstNode<T>
    where T : struct, IFloatingPoint<T>
{
    public string FunctionName { get; }
    private readonly string _normalizedName;
    public IReadOnlyList<AstNode<T>> Arguments { get; }

    public FunctionCallNode(string functionName, List<AstNode<T>> arguments)
    {
        FunctionName = functionName;
        _normalizedName = functionName.ToLowerInvariant();
        if (_normalizedName == FormulaKeywords.MONTH_DAYS && arguments.Count != 0)
            throw new FormatException("month_days() does not accept arguments.");
        Arguments = arguments;
    }

    public override ValueResult<T> Evaluate(IFormulaContext<T> context, DateTime timestamp)
    {
        return ValueResult<T>.Scalar(EvaluateScalar(context, timestamp));
    }

    public override T? EvaluateScalar(IFormulaContext<T> context, DateTime timestamp)
    {
        return _normalizedName switch
        {
            FormulaKeywords.AVG => EvaluateAvg(context, timestamp),
            FormulaKeywords.SUM => EvaluateSum(context, timestamp),
            FormulaKeywords.MONTH_DAYS => T.CreateChecked(context.GetMonthDays(timestamp)),
            _ => throw new InvalidOperationException($"Unknown function: {FunctionName}")
        };
    }

    private T? EvaluateAvg(IFormulaContext<T> context, DateTime timestamp)
    {
        if (Arguments.Count == 0)
            throw new InvalidOperationException("avg() requires at least one argument");

        var aggregate = new Aggregate<T>();
        foreach (var arg in Arguments)
            aggregate.Merge(arg.EvaluateAggregate(context, timestamp));
        return aggregate.GetAverage();
    }

    private T? EvaluateSum(IFormulaContext<T> context, DateTime timestamp)
    {
        var aggregate = new Aggregate<T>();
        foreach (var arg in Arguments)
            aggregate.Merge(arg.EvaluateAggregate(context, timestamp));
        return aggregate.GetSum();
    }

    public override void CollectParameters(HybridSet<ParameterInfo> parameters)
    {
        foreach (var arg in Arguments)
            arg.CollectParameters(parameters);
    }

    internal override TimestampDomain GetTimestampDomain(IFormulaContext<T> context, DateTime from, DateTime to)
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
public sealed class ModifiedExpressionNode<T> : AstNode<T>
    where T : struct, IFloatingPoint<T>
{
    public AstNode<T> Expression { get; }
    public string Modifier { get; }
    private readonly string _normalizedModifier;

    public ModifiedExpressionNode(AstNode<T> expression, string modifier)
    {
        Expression = expression;
        Modifier = modifier;
        _normalizedModifier = modifier.ToLowerInvariant();
    }

    public override ValueResult<T> Evaluate(IFormulaContext<T> context, DateTime timestamp)
    {
        var (startDate, endDate) = GetDateRange(timestamp);
        if (_normalizedModifier == FormulaKeywords.MONTH_BEGIN)
            return ValueResult<T>.Scalar(Expression.EvaluateScalar(context, startDate));
        var timestampDomain = Expression.GetTimestampDomain(context, startDate, endDate);
        var timestamps = timestampDomain.IsAny
            ? ReadOnlySpan<DateTime>.Empty
            : timestampDomain.Timestamps.Span;
        var results = new T[timestamps.Length];
        var hasValue = new bool[timestamps.Length];

        for (var i = 0; i < timestamps.Length; i++)
        {
            var value = Expression.EvaluateScalar(context, timestamps[i]);
            if (!value.HasValue)
                continue;

            results[i] = value.Value;
            hasValue[i] = true;
        }

        return ValueResult<T>.Array(results, hasValue);
    }

    public override Aggregate<T> EvaluateAggregate(IFormulaContext<T> context, DateTime timestamp)
    {
        var (startDate, endDate) = GetDateRange(timestamp);
        if (_normalizedModifier == FormulaKeywords.MONTH_BEGIN)
        {
            var value = Expression.EvaluateScalar(context, startDate);
            return Aggregate<T>.FromValue(value);
        }
        if (TryEvaluateScaledSum(context, timestamp, startDate, endDate, out var result))
            return result;

        return EvaluateExpressionSumAndCount(context, timestamp, Expression, startDate, endDate);
    }

    private static Aggregate<T> EvaluateExpressionSumAndCount(
        IFormulaContext<T> context,
        DateTime timestamp,
        AstNode<T> expression,
        DateTime startDate,
        DateTime endDate)
    {
        if (TryEvaluateParameterBinarySum(context, expression, startDate, endDate, out var directResult))
            return directResult;

        var timestampDomain = expression.GetTimestampDomain(context, startDate, endDate);
        if (timestampDomain.IsAny)
            return new Aggregate<T>();

        var timestamps = timestampDomain.Timestamps.Span;
        var aggregate = new Aggregate<T>();

        for (var i = 0; i < timestamps.Length; i++)
        {
            var value = expression.EvaluateScalar(context, timestamps[i]);
            if (!value.HasValue)
                continue;

            aggregate.Add(value.Value);
        }

        return aggregate;
    }

    private static bool TryEvaluateParameterBinarySum(
        IFormulaContext<T> context,
        AstNode<T> expression,
        DateTime startDate,
        DateTime endDate,
        out Aggregate<T> result)
    {
        result = default;

        if (expression is not BinaryOperationNode<T> binary)
            return false;

        if (TryGetPlainRegularParameter(binary.Left, out var parameter) && binary.Right.IsTimestampIndependent)
        {
            result = EvaluateParameterConstantSum(context, binary, parameter, binary.Right, false, startDate, endDate);
            return true;
        }
        if (TryGetPlainRegularParameter(binary.Right, out parameter) && binary.Left.IsTimestampIndependent)
        {
            result = EvaluateParameterConstantSum(context, binary, parameter, binary.Left, true, startDate, endDate);
            return true;
        }

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
        var aggregate = new Aggregate<T>();

        while (leftIndex < leftTimestamps.Length && rightIndex < rightTimestamps.Length)
        {
            var leftTimestamp = leftTimestamps[leftIndex];
            var rightTimestamp = rightTimestamps[rightIndex];

            if (leftTimestamp == rightTimestamp)
            {
                var leftValue = leftValues[leftIndex];
                var rightValue = rightValues[rightIndex];
                aggregate.Add(binary.Operator switch
                {
                    BinaryOperator.Add => leftValue + rightValue,
                    BinaryOperator.Subtract => leftValue - rightValue,
                    BinaryOperator.Multiply => leftValue * rightValue,
                    BinaryOperator.Divide => leftValue / rightValue,
                    _ => throw new InvalidOperationException($"Unknown operator: {binary.Operator}")
                });
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

        result = aggregate;
        return true;
    }

    private static Aggregate<T> EvaluateParameterConstantSum(
        IFormulaContext<T> context, BinaryOperationNode<T> binary, ParameterInfo parameter,
        AstNode<T> constantExpression, bool constantOnLeft, DateTime startDate, DateTime endDate)
    {
        var series = context.GetParameterDataSeries(parameter);
        var (start, end) = series.GetRangeBounds(startDate, endDate);
        var values = series.Values.Span.Slice(start, end - start);
        if (values.Length == 0)
            return new Aggregate<T>();
        var constant = constantExpression.EvaluateScalar(context, startDate);
        if (!constant.HasValue)
            return new Aggregate<T>();
        var aggregate = new Aggregate<T>();
        foreach (var value in values)
        {
            var left = constantOnLeft ? constant.Value : value;
            var right = constantOnLeft ? value : constant.Value;
            // Preserve each operation and accumulation order, including double rounding.
            aggregate.Add(binary.Operator switch
            {
                BinaryOperator.Add => left + right,
                BinaryOperator.Subtract => left - right,
                BinaryOperator.Multiply => left * right,
                BinaryOperator.Divide => left / right,
                _ => throw new InvalidOperationException($"Unknown operator: {binary.Operator}")
            });
        }
        return aggregate;
    }

    private static bool TryGetPlainRegularParameter(AstNode<T> node, out ParameterInfo parameter)
    {
        if (node is ParameterNode<T> parameterNode &&
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
        IFormulaContext<T> context,
        DateTime timestamp,
        DateTime startDate,
        DateTime endDate,
        out Aggregate<T> result)
    {
        result = default;

        if (Expression is not BinaryOperationNode<T> { Operator: BinaryOperator.Multiply } binary)
            return false;

        if (binary.Left.IsTimestampIndependent == binary.Right.IsTimestampIndependent)
            return false;

        var scaleExpression = binary.Left.IsTimestampIndependent ? binary.Left : binary.Right;
        var seriesExpression = binary.Left.IsTimestampIndependent ? binary.Right : binary.Left;
        var scale = scaleExpression.EvaluateScalar(context, timestamp);
        if (!scale.HasValue)
        {
            result = new Aggregate<T>();
            return true;
        }

        var aggregate = EvaluateExpressionSumAndCount(
            context,
            timestamp,
            seriesExpression,
            startDate,
            endDate);

        result = aggregate.Scale(scale.Value);
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

    internal override TimestampDomain GetTimestampDomain(IFormulaContext<T> context, DateTime from, DateTime to) =>
        Expression.GetTimestampDomain(context, from, to);
}
