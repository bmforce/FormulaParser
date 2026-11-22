using System.Numerics;
using System.Collections.Concurrent;

namespace FormulaParser;

public sealed class FormulaCompiler<T>
    where T : struct, IFloatingPoint<T>
{
    private readonly ConcurrentDictionary<string, CompiledFormula<T>> _cache = new();
    private readonly FormulaSyntax _syntax;

    public FormulaCompiler(FormulaSyntax? syntax = null)
    {
        _syntax = syntax ?? FormulaSyntax.Default;
        _syntax.Validate();
    }
    // НЕ используем shared Parser<T> - создаем новый для каждой компиляции для потокобезопасности

    public CompiledFormula<T> Compile(string formula)
    {
        return _cache.GetOrAdd(formula, CompileInternal);
    }

    private CompiledFormula<T> CompileInternal(string formula)
    {
        try
        {
            // Создаем новый Parser<T> для каждой компиляции - потокобезопасно!
            var parser = new Parser<T>(_syntax);
            var ast = parser.Parse(formula);
            return new CompiledFormula<T>(ast);
        }
        catch (FormatException ex)
        {
            throw new FormatException($"Error parsing formula: '{formula}'. {ex.Message}", ex);
        }
    }

    public void ClearCache()
    {
        _cache.Clear();
    }

    public int CacheSize => _cache.Count;
}
