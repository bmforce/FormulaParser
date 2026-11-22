namespace FormulaParser;

/// <summary>Immutable keyword replacements. Replaced spellings are not accepted as aliases.</summary>
public sealed record FormulaSyntax
{
    public static FormulaSyntax Default { get; } = new();
    public string Sum { get; init; } = FormulaKeywords.SUM;
    public string Average { get; init; } = FormulaKeywords.AVG;
    public string MonthNow { get; init; } = FormulaKeywords.MONTH_NOW;
    public string YearNow { get; init; } = FormulaKeywords.YEAR_NOW;
    public string MonthBegin { get; init; } = FormulaKeywords.MONTH_BEGIN;
    public string MonthDays { get; init; } = FormulaKeywords.MONTH_DAYS;
    public string Ancestors { get; init; } = FormulaKeywords.ANCESTORS;
    public string Descendants { get; init; } = FormulaKeywords.DESCENDANTS;
    public string Object { get; init; } = FormulaKeywords.OBJ;
    public string Parameter { get; init; } = FormulaKeywords.PARAMETER;

    internal static bool Matches(string value, string keyword) => string.Equals(value, keyword, StringComparison.OrdinalIgnoreCase);
    internal static bool IsIdentifier(string? value) => !string.IsNullOrWhiteSpace(value) &&
        (char.IsLetter(value[0]) || value[0] == '_') && value.All(ch => char.IsLetterOrDigit(ch) || ch == '_');

    internal void Validate()
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in new[] { Sum, Average, MonthNow, YearNow, MonthBegin, MonthDays, Ancestors, Descendants, Object, Parameter })
        {
            if (!IsIdentifier(name))
                throw new ArgumentException($"Invalid formula keyword '{name}'.");
            if (!names.Add(name)) throw new ArgumentException($"Duplicate formula keyword '{name}'.");
        }
    }

    internal string Function(string name) => Matches(name, Sum) ? FormulaKeywords.SUM :
        Matches(name, Average) ? FormulaKeywords.AVG : Matches(name, MonthDays) ? FormulaKeywords.MONTH_DAYS :
        throw new FormatException($"Unknown function '{name}'.");

    internal string Modifier(string name) => Matches(name, MonthNow) ? FormulaKeywords.MONTH_NOW :
        Matches(name, YearNow) ? FormulaKeywords.YEAR_NOW : Matches(name, MonthBegin) ? FormulaKeywords.MONTH_BEGIN :
        throw new FormatException($"Unknown time modifier '{name}'.");

    internal bool IsRetiredPrefix(string name)
    {
        if (Matches(name, Object) || Matches(name, Parameter) || Matches(name, Ancestors) || Matches(name, Descendants)) return false;
        return Matches(name, FormulaKeywords.OBJ) || Matches(name, FormulaKeywords.PARAMETER) ||
            Matches(name, FormulaKeywords.ANCESTORS) || Matches(name, FormulaKeywords.DESCENDANTS);
    }
}
