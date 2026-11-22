using System.Numerics;
namespace FormulaParser;

public sealed class CompiledFormula<T>
    where T : struct, IFloatingPoint<T>
{
    private readonly AstNode<T> _root;
    internal AstNode<T> Root => _root;
    private readonly ParameterInfo[] _parameters;
    internal readonly record struct DataDependency(ParameterInfo Parameter, string? ModifierPath);
    internal IReadOnlyList<DataDependency> DataDependencies { get; }

    public CompiledFormula(AstNode<T> root)
    {
        _root = root;
        var parameters = new HybridSet<ParameterInfo>();
        _root.CollectParameters(parameters);
        _parameters = parameters.ToArray();
        var dependencies = new List<DataDependency>();
        CollectDataDependencies(root, null, dependencies);
        DataDependencies = dependencies.ToArray();
    }

    // Keep loading requirements separate from parameters used during evaluation.
    private static void CollectDataDependencies(
        AstNode<T> node, string? modifierPath, List<DataDependency> dependencies)
    {
        switch (node)
        {
            case ParameterNode<T> parameter:
                dependencies.Add(new DataDependency(parameter.Parameter,
                    AppendModifier(modifierPath, parameter.Parameter.Modifier)));
                break;
            case BinaryOperationNode<T> binary:
                CollectDataDependencies(binary.Left, modifierPath, dependencies);
                CollectDataDependencies(binary.Right, modifierPath, dependencies);
                break;
            case FunctionCallNode<T> function:
                foreach (var argument in function.Arguments)
                    CollectDataDependencies(argument, modifierPath, dependencies);
                break;
            case ModifiedExpressionNode<T> modified:
                CollectDataDependencies(modified.Expression,
                    AppendModifier(modifierPath, modified.Modifier), dependencies);
                break;
            case NumberNode<T>:
                break;
            case ObjectSetNode<T> selection:
                foreach (var expression in selection.Expressions)
                    CollectDataDependencies(expression, modifierPath, dependencies);
                break;
            default:
                // Preserve support for caller-defined AST nodes.
                var parameters = new HybridSet<ParameterInfo>();
                node.CollectParameters(parameters);
                foreach (var parameter in parameters.ToArray())
                    dependencies.Add(new DataDependency(parameter,
                        AppendModifier(modifierPath, parameter.Modifier)));
                break;
        }
    }

    private static string? AppendModifier(string? path, string? modifier)
    {
        if (modifier?.Contains('/') == true)
            throw new FormatException($"Invalid time modifier: {modifier}");
        return modifier == null ? path : path == null
            ? modifier.ToLowerInvariant()
            : path + "/" + modifier.ToLowerInvariant();
    }

    public IReadOnlyCollection<ParameterInfo> Parameters => _parameters;

    public T? Evaluate(IFormulaContext<T> context, DateTime timestamp)
    {
        return _root.Evaluate(context, timestamp).AsScalar();
    }

    public override string ToString() => _root.ToString() ?? string.Empty;
}
