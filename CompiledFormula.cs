namespace FormulaParser;

public sealed class CompiledFormula
{
    private readonly AstNode _root;
    private readonly ParameterInfo[] _parameters;

    public CompiledFormula(AstNode root)
    {
        _root = root;
        var parameters = new HybridSet<ParameterInfo>();
        _root.CollectParameters(parameters);
        _parameters = parameters.ToArray();
    }

    public IReadOnlyCollection<ParameterInfo> Parameters => _parameters;

    public double? Evaluate(IFormulaContext context, DateTime timestamp)
    {
        return _root.Evaluate(context, timestamp).AsScalar();
    }

    public override string ToString() => _root.ToString() ?? string.Empty;
}
