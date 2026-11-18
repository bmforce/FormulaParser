using System.Collections.Concurrent;

namespace FormulaParser;

public sealed class FormulaCompiler
{
    private readonly ConcurrentDictionary<string, CompiledFormula> _cache = new();
    // НЕ используем shared Parser - создаем новый для каждой компиляции для потокобезопасности

    public CompiledFormula Compile(string formula)
    {
        return _cache.GetOrAdd(formula, CompileInternal);
    }

    private CompiledFormula CompileInternal(string formula)
    {
        try
        {
            // Создаем новый Parser для каждой компиляции - потокобезопасно!
            var parser = new Parser();
            var ast = parser.Parse(formula);
            return new CompiledFormula(ast);
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
