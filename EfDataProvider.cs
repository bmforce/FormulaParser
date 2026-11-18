using FormulaParser.Data;
using Microsoft.EntityFrameworkCore;

namespace FormulaParser;

/// <summary>
/// Провайдер данных на основе EF Core.
/// В формуле обычно несколько параметров — их выбираем скопом одним запросом.
/// Загружает все запрошенные параметры из базы.
/// </summary>
public sealed class EfDataProvider : DataProviderBase
{
    private readonly FormulaDbContext _context;

    public EfDataProvider(FormulaDbContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        _context = context;
    }

    public override Dictionary<ParameterDataKey, ParameterDataSeries> GetData(
        IReadOnlyCollection<DataRequest> requests)
    {
        var result = new Dictionary<ParameterDataKey, ParameterDataSeries>(requests.Count);
        if (requests.Count == 0)
            return result;

        var requestedNumericIds = new List<int>();
        var requestedCodeNames = new List<string>();

        foreach (var request in requests)
        {
            if (int.TryParse(request.Identifier, out var numericId))
                requestedNumericIds.Add(numericId);
            else
                requestedCodeNames.Add(request.Identifier);
        }

        var resolvedParameterIds = ResolveParameterIds(requestedNumericIds, requestedCodeNames);
        var numericParameterIds = resolvedParameterIds.Values.Distinct().ToList();
        var requestedObjectIds = requests.Select(r => r.ObjectId).Distinct().ToList();
        var minDate = requests.Min(r => r.DateFrom);
        var maxDate = requests.Max(r => r.DateTo);

        // Один запрос к БД: загружаем все значения для всех параметров в нужном диапазоне.
        // AsNoTracking — данные только читаются, отслеживание изменений не нужно.
        // Порядок не важен: значения складываются в Dictionary<DateTime, Value>, поэтому OrderBy не нужен.
        var allValues = _context.ParameterValues
            .AsNoTracking()
            .Where(p =>
                requestedObjectIds.Contains(p.ObjectId) &&
                numericParameterIds.Contains(p.ParameterId) &&
                p.Date >= minDate &&
                p.Date <= maxDate)
            .ToList()
            .GroupBy(p => new { p.ObjectId, p.ParameterId })
            .ToDictionary(
                g => (g.Key.ObjectId, g.Key.ParameterId),
                g => g.ToDictionary(v => v.Date, v => v.Value));

        foreach (var request in requests)
        {
            var dataKey = request.DataKey;

            if (!resolvedParameterIds.TryGetValue(request.Identifier, out var parameterId) ||
                !allValues.TryGetValue((request.ObjectId, parameterId), out var paramValues))
            {
                result[dataKey] = ParameterDataSeries.Empty();
                continue;
            }

            result[dataKey] = ParameterDataSeries.FromValues(
                request.DateFrom,
                request.DateTo,
                paramValues);
        }

        return result;
    }

    private Dictionary<string, int> ResolveParameterIds(
        List<int> requestedNumericIds,
        List<string> requestedCodeNames)
    {
        var result = new Dictionary<string, int>(StringComparer.Ordinal);

        if (requestedNumericIds.Count == 0 && requestedCodeNames.Count == 0)
            return result;

        var parameters = _context.Parameters
            .AsNoTracking()
            .Where(p => requestedNumericIds.Contains(p.Id) || requestedCodeNames.Contains(p.CodeName))
            .Select(p => new { p.Id, p.CodeName })
            .ToList();

        foreach (var parameter in parameters)
        {
            var idKey = parameter.Id.ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (requestedNumericIds.Contains(parameter.Id))
                result[idKey] = parameter.Id;

            if (requestedCodeNames.Contains(parameter.CodeName))
                result[parameter.CodeName] = parameter.Id;
        }

        return result;
    }
}
