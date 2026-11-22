using System.Numerics;

namespace FormulaParser;

/// <summary>A resolved selector: expressions are bound to concrete objects, not rewritten as text.</summary>
public sealed class ObjectSetNode<T>(IReadOnlyList<AstNode<T>> expressions) : AstNode<T>
    where T : struct, IFloatingPoint<T>
{
    public IReadOnlyList<AstNode<T>> Expressions { get; } = expressions.ToArray();

    public override T? EvaluateScalar(IFormulaContext<T> context, DateTime timestamp) =>
        throw new InvalidOperationException("An object selector returns a set. Use sum() or avg().");

    public override ValueResult<T> Evaluate(IFormulaContext<T> context, DateTime timestamp)
    {
        var values = new List<T>();
        var present = new List<bool>();
        foreach (var expression in Expressions)
            foreach (var value in expression.Evaluate(context, timestamp).AsArray())
            {
                values.Add(value.GetValueOrDefault());
                present.Add(value.HasValue);
            }
        return ValueResult<T>.Array(values.ToArray(), present.ToArray());
    }

    public override Aggregate<T> EvaluateAggregate(IFormulaContext<T> context, DateTime timestamp)
    {
        var aggregate = new Aggregate<T>();
        foreach (var expression in Expressions) aggregate.Merge(expression.EvaluateAggregate(context, timestamp));
        return aggregate;
    }

    public override void CollectParameters(HybridSet<ParameterInfo> parameters)
    {
        foreach (var expression in Expressions) expression.CollectParameters(parameters);
    }

    internal override TimestampDomain GetTimestampDomain(IFormulaContext<T> context, DateTime from, DateTime to)
    {
        var domain = TimestampDomain.Empty;
        foreach (var expression in Expressions) domain = domain.Union(expression.GetTimestampDomain(context, from, to));
        return domain;
    }
}
