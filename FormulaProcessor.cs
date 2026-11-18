using System.Collections.Concurrent;

namespace FormulaParser;

/// <summary>
/// Обработчик формул с оптимизацией запросов к БД.
/// Умеет обрабатывать как массив формул, так и одну формулу.
/// </summary>
public sealed class FormulaProcessor
{
    private const int DefaultObjectId = 0;
    private static readonly Dictionary<string, double> NoCustomValues = new(StringComparer.Ordinal);

    private readonly record struct CalculationWorkItem(
        int? ParameterId,
        int? ObjectId,
        string Formula,
        int EffectiveObjectId);

    private readonly record struct PreparedEvaluation(
        string Formula,
        int ObjectId,
        CompiledFormula CompiledFormula,
        FormulaContext Context);

    private readonly record struct DataRequestTemplate(
        int ObjectId,
        string Identifier,
        string? Modifier);

    private sealed class CalculationPlan
    {
        public required CalculationWorkItem[] WorkItems { get; init; }
        public required (string Formula, int ObjectId)[] EvaluationItems { get; init; }
        public required CompiledFormula[] EvaluationCompiledFormulas { get; init; }
        public required int[] EvaluationObjectIndexes { get; init; }
        public required int[] WorkItemEvaluationIndexes { get; init; }
        public required int[] ObjectIds { get; init; }
        public required DataRequestTemplate[] DataRequestTemplates { get; init; }
        public required string[] CustomNames { get; init; }
    }

    private readonly FormulaCompiler _compiler = new();
    private readonly IDataProvider _dataProvider;
    private readonly object _calculationPlanLock = new();
    private CalculationPlan? _lastCalculationPlan;

    public FormulaProcessor(IDataProvider dataProvider)
    {
        ArgumentNullException.ThrowIfNull(dataProvider);

        _dataProvider = dataProvider;
    }

    /// <summary>
    /// Вычислить простую формулу без внешних параметров данных.
    /// </summary>
    public FormulaEvaluationResult Evaluate(string formula)
    {
        ArgumentException.ThrowIfNullOrEmpty(formula);
        return Evaluate([formula], parallel: false)[0];
    }

    /// <summary>
    /// Вычислить простые формулы без внешних параметров данных.
    /// </summary>
    public IReadOnlyList<FormulaEvaluationResult> Evaluate(
        IEnumerable<string> formulas,
        bool parallel = true) =>
        ProcessSimple(CreateSimpleWorkItems(formulas), parallel);

    /// <summary>
    /// Посчитать одну расчетную строку на текущую дату.
    /// </summary>
    public FormulaCalculationResult Calculate(FormulaCalculation calculation) =>
        Calculate(calculation, DateTime.Today);

    /// <summary>
    /// Посчитать одну расчетную строку на указанную дату.
    /// </summary>
    public FormulaCalculationResult Calculate(FormulaCalculation calculation, DateTime calculationDate)
    {
        ArgumentNullException.ThrowIfNull(calculation);
        return Calculate([calculation], calculationDate, parallel: false)[0];
    }

    /// <summary>
    /// Посчитать одну расчетную строку на диапазоне дат.
    /// </summary>
    public FormulaCalculationResult Calculate(
        FormulaCalculation calculation,
        DateTime startDate,
        DateTime endDate,
        TimeSpan step,
        bool parallel = true)
    {
        ArgumentNullException.ThrowIfNull(calculation);
        return Calculate([calculation], startDate, endDate, step, parallel)[0];
    }

    /// <summary>
    /// Посчитать набор расчетных строк на текущую дату.
    /// </summary>
    public IReadOnlyList<FormulaCalculationResult> Calculate(
        IEnumerable<FormulaCalculation> calculations,
        bool parallel = true) =>
        Calculate(calculations, DateTime.Today, parallel);

    /// <summary>
    /// Посчитать набор расчетных строк на указанную дату.
    /// </summary>
    public IReadOnlyList<FormulaCalculationResult> Calculate(
        IEnumerable<FormulaCalculation> calculations,
        DateTime calculationDate,
        bool parallel = true) =>
        Process(GetOrCreateCalculationPlan(calculations, parallel), calculationDate, parallel);

    /// <summary>
    /// Посчитать набор расчетных строк на диапазоне дат.
    /// </summary>
    public IReadOnlyList<FormulaCalculationResult> Calculate(
        IEnumerable<FormulaCalculation> calculations,
        DateTime startDate,
        DateTime endDate,
        TimeSpan step,
        bool parallel = true) =>
        Process(GetOrCreateCalculationPlan(calculations, parallel), startDate, endDate, step, parallel);

    private IReadOnlyList<FormulaEvaluationResult> ProcessSimple(
        IReadOnlyList<CalculationWorkItem> workItems,
        bool parallel)
    {
        var evaluationItems = GetEvaluationItems(workItems);
        var formulaList = GetFormulaList(workItems);
        var compiledFormulas = CompileAll(formulaList, parallel);
        var customNames = GetSimpleCustomNames(compiledFormulas);
        var customValues = customNames.Count == 0
            ? NoCustomValues
            : _dataProvider.GetCustomValues(customNames);

        var context = new FormulaContext(new Dictionary<ParameterDataKey, ParameterDataSeries>(), customValues);
        var objectContexts = CreateObjectContexts(context, workItems);
        var preparedEvaluations = PrepareEvaluations(evaluationItems, compiledFormulas, objectContexts);
        var values = EvaluateAll(
            preparedEvaluations,
            DateTime.MinValue,
            parallel);

        return CreateSimpleResults(workItems, evaluationItems, values);
    }

    private IReadOnlyList<FormulaCalculationResult> Process(
        CalculationPlan plan,
        DateTime calculationDate,
        bool parallel)
    {
        // 1. Строим запросы внешних данных по заранее подготовленному плану.
        var dataRequests = BuildDataRequests(plan.DataRequestTemplates, calculationDate);

        // 2. Один запрос ко всем внешним данным.
        var data = _dataProvider.GetData(dataRequests);
        var customValues = plan.CustomNames.Length == 0
            ? NoCustomValues
            : _dataProvider.GetCustomValues(plan.CustomNames);

        // 3. Создаем контекст с предзагруженными данными и custom-константами.
        var context = new FormulaContext(data, customValues);

        // 4. Вычисляем все формулы по каждому объекту.
        var preparedEvaluations = PrepareEvaluations(plan, context);
        var values = EvaluateAll(preparedEvaluations, calculationDate, parallel);
        return CreateSingleDateResults(calculationDate, plan.WorkItems, plan.WorkItemEvaluationIndexes, values);
    }

    private IReadOnlyList<FormulaCalculationResult> Process(
        CalculationPlan plan,
        DateTime startDate,
        DateTime endDate,
        TimeSpan step,
        bool parallel)
    {
        if (endDate < startDate)
            throw new ArgumentException("End date must be greater than or equal to start date.", nameof(endDate));
        if (step <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(step), "Step must be greater than zero.");

        var calculationDates = BuildCalculationDates(startDate, endDate, step);

        // 1. Строим общий набор запросов на весь диапазон дат.
        var dataRequests = BuildDataRequests(plan.DataRequestTemplates, calculationDates);

        // 2. Загружаем внешние данные и custom-константы один раз.
        var data = _dataProvider.GetData(dataRequests);
        var customValues = plan.CustomNames.Length == 0
            ? NoCustomValues
            : _dataProvider.GetCustomValues(plan.CustomNames);

        // 3. Один общий контекст используется для всех расчетных дат.
        var context = new FormulaContext(data, customValues);
        var preparedEvaluations = PrepareEvaluations(plan, context);

        return EvaluateRange(plan.WorkItems, plan.WorkItemEvaluationIndexes, preparedEvaluations, calculationDates, parallel);
    }

    private static List<CalculationWorkItem> CreateSimpleWorkItems(IEnumerable<string> formulas)
    {
        ArgumentNullException.ThrowIfNull(formulas);

        var formulaList = formulas as IReadOnlyList<string> ?? formulas.ToList();
        var result = new List<CalculationWorkItem>(formulaList.Count);
        foreach (var formula in formulaList)
        {
            ArgumentException.ThrowIfNullOrEmpty(formula);
            result.Add(new CalculationWorkItem(
                ParameterId: null,
                ObjectId: null,
                Formula: formula,
                EffectiveObjectId: DefaultObjectId));
        }

        return result;
    }

    private static List<CalculationWorkItem> CreateWorkItems(IEnumerable<FormulaCalculation> calculations)
    {
        ArgumentNullException.ThrowIfNull(calculations);

        var calculationList = calculations as IReadOnlyList<FormulaCalculation> ?? calculations.ToList();
        var result = new List<CalculationWorkItem>(calculationList.Count);
        var seen = new HybridSet<(int? ParameterId, int ObjectId, string Formula)>();

        foreach (var calculation in calculationList)
        {
            if (calculation is null)
                throw new ArgumentException("Calculation cannot be null.", nameof(calculations));

            ArgumentException.ThrowIfNullOrEmpty(calculation.Formula);

            if (!seen.Add((calculation.ParameterId, calculation.ObjectId, calculation.Formula)))
            {
                throw new ArgumentException(
                    $"Duplicate calculation for ParameterId '{calculation.ParameterId?.ToString() ?? "null"}', " +
                    $"ObjectId '{calculation.ObjectId}' and formula '{calculation.Formula}'.",
                    nameof(calculations));
            }

            result.Add(new CalculationWorkItem(
                calculation.ParameterId,
                calculation.ObjectId,
                calculation.Formula,
                calculation.ObjectId));
        }

        return result;
    }

    private CalculationPlan GetOrCreateCalculationPlan(
        IEnumerable<FormulaCalculation> calculations,
        bool parallel)
    {
        ArgumentNullException.ThrowIfNull(calculations);

        var calculationList = calculations as IReadOnlyList<FormulaCalculation> ?? calculations.ToList();
        var cached = _lastCalculationPlan;
        if (cached != null && MatchesPlan(cached, calculationList))
            return cached;

        lock (_calculationPlanLock)
        {
            cached = _lastCalculationPlan;
            if (cached != null && MatchesPlan(cached, calculationList))
                return cached;

            cached = CreateCalculationPlan(calculationList, parallel);
            _lastCalculationPlan = cached;
            return cached;
        }
    }

    private CalculationPlan CreateCalculationPlan(
        IReadOnlyList<FormulaCalculation> calculations,
        bool parallel)
    {
        var workItems = CreateWorkItems(calculations).ToArray();
        var formulaList = GetFormulaList(workItems);
        var compiledFormulas = CompileAll(formulaList, parallel);
        var evaluationItems = GetEvaluationItems(workItems).ToArray();
        var objectIds = GetObjectIds(evaluationItems);
        var (dataRequestTemplates, customNames) = CreateDataRequestPlan(evaluationItems, compiledFormulas);

        return new CalculationPlan
        {
            WorkItems = workItems,
            EvaluationItems = evaluationItems,
            EvaluationCompiledFormulas = GetEvaluationCompiledFormulas(evaluationItems, compiledFormulas),
            EvaluationObjectIndexes = GetEvaluationObjectIndexes(evaluationItems, objectIds),
            WorkItemEvaluationIndexes = GetWorkItemEvaluationIndexes(workItems, evaluationItems),
            ObjectIds = objectIds,
            DataRequestTemplates = dataRequestTemplates,
            CustomNames = customNames
        };
    }

    private static bool MatchesPlan(
        CalculationPlan plan,
        IReadOnlyList<FormulaCalculation> calculations)
    {
        if (plan.WorkItems.Length != calculations.Count)
            return false;

        for (var i = 0; i < calculations.Count; i++)
        {
            var calculation = calculations[i];
            if (calculation is null)
                return false;

            var workItem = plan.WorkItems[i];
            if (workItem.ParameterId != calculation.ParameterId ||
                workItem.EffectiveObjectId != calculation.ObjectId ||
                !string.Equals(workItem.Formula, calculation.Formula, StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    private static (DataRequestTemplate[] DataRequestTemplates, string[] CustomNames) CreateDataRequestPlan(
        IReadOnlyList<(string Formula, int ObjectId)> evaluationItems,
        IReadOnlyDictionary<string, CompiledFormula> compiledFormulas)
    {
        var dataRequestTemplates = new List<DataRequestTemplate>();
        var seenDataRequestTemplates = new HybridSet<DataRequestTemplate>();
        var customNames = new List<string>();
        var seenCustomNames = new HybridSet<string>(StringComparer.Ordinal);

        foreach (var (formula, objectId) in evaluationItems)
        {
            var compiled = compiledFormulas[formula];
            foreach (var param in compiled.Parameters)
            {
                if (param.Kind == ParameterKind.Custom)
                {
                    if (seenCustomNames.Add(param.ParameterId))
                        customNames.Add(param.ParameterId);
                    continue;
                }

                var template = new DataRequestTemplate(
                    param.ObjectId ?? objectId,
                    param.ParameterId,
                    param.Modifier?.ToLowerInvariant());

                if (seenDataRequestTemplates.Add(template))
                    dataRequestTemplates.Add(template);
            }
        }

        return (dataRequestTemplates.ToArray(), customNames.ToArray());
    }

    private static CompiledFormula[] GetEvaluationCompiledFormulas(
        IReadOnlyList<(string Formula, int ObjectId)> evaluationItems,
        IReadOnlyDictionary<string, CompiledFormula> compiledFormulas)
    {
        var result = new CompiledFormula[evaluationItems.Count];
        for (var i = 0; i < evaluationItems.Count; i++)
            result[i] = compiledFormulas[evaluationItems[i].Formula];

        return result;
    }

    private static int[] GetEvaluationObjectIndexes(
        IReadOnlyList<(string Formula, int ObjectId)> evaluationItems,
        IReadOnlyList<int> objectIds)
    {
        var result = new int[evaluationItems.Count];
        for (var i = 0; i < evaluationItems.Count; i++)
            result[i] = GetObjectIndex(objectIds, evaluationItems[i].ObjectId);

        return result;
    }

    private static int[] GetWorkItemEvaluationIndexes(
        IReadOnlyList<CalculationWorkItem> workItems,
        IReadOnlyList<(string Formula, int ObjectId)> evaluationItems)
    {
        var result = new int[workItems.Count];
        for (var i = 0; i < workItems.Count; i++)
        {
            var workItem = workItems[i];
            result[i] = GetEvaluationIndex(evaluationItems, workItem.Formula, workItem.EffectiveObjectId);
        }

        return result;
    }

    private static int[] GetObjectIds(IReadOnlyList<(string Formula, int ObjectId)> evaluationItems)
    {
        var result = new List<int>(evaluationItems.Count);
        foreach (var (_, objectId) in evaluationItems)
        {
            if (!ContainsObjectId(result, objectId))
                result.Add(objectId);
        }

        return result.ToArray();
    }

    private static bool ContainsObjectId(IReadOnlyList<int> objectIds, int objectId) =>
        GetObjectIndexOrDefault(objectIds, objectId) >= 0;

    private static int GetObjectIndex(IReadOnlyList<int> objectIds, int objectId)
    {
        var index = GetObjectIndexOrDefault(objectIds, objectId);
        if (index >= 0)
            return index;

        throw new InvalidOperationException($"Object '{objectId}' not found in calculation plan.");
    }

    private static int GetObjectIndexOrDefault(IReadOnlyList<int> objectIds, int objectId)
    {
        for (var i = 0; i < objectIds.Count; i++)
        {
            if (objectIds[i] == objectId)
                return i;
        }

        return -1;
    }

    private static List<string> GetFormulaList(IReadOnlyList<CalculationWorkItem> workItems)
    {
        var result = new List<string>(workItems.Count);
        var seen = new HybridSet<string>(StringComparer.Ordinal);

        foreach (var workItem in workItems)
        {
            if (seen.Add(workItem.Formula))
                result.Add(workItem.Formula);
        }

        return result;
    }

    private static IReadOnlyCollection<string> GetSimpleCustomNames(
        Dictionary<string, CompiledFormula> compiledFormulas)
    {
        List<string>? customNames = null;
        HybridSet<string>? seenCustomNames = null;

        foreach (var (formula, compiled) in compiledFormulas)
        {
            foreach (var parameter in compiled.Parameters)
            {
                if (parameter.Kind != ParameterKind.Custom)
                {
                    throw new InvalidOperationException(
                        $"Simple formula cannot use external parameter '{parameter}' in formula '{formula}'.");
                }

                if (parameter.Modifier != null)
                {
                    throw new InvalidOperationException(
                    $"Simple formula cannot use modifier '{parameter.Modifier}' for constant '{parameter.ParameterId}' in formula '{formula}'.");
                }

                customNames ??= new List<string>();
                seenCustomNames ??= new HybridSet<string>(StringComparer.Ordinal);
                if (seenCustomNames.Add(parameter.ParameterId))
                    customNames.Add(parameter.ParameterId);
            }
        }

        return customNames is null ? Array.Empty<string>() : customNames;
    }

    private Dictionary<string, CompiledFormula> CompileAll(IReadOnlyList<string> formulaList, bool parallel)
    {
        var compilationErrors = new ConcurrentBag<(string Formula, Exception Error)>();

        if (parallel)
        {
            var compiled = new ConcurrentDictionary<string, CompiledFormula>();
            Parallel.ForEach(formulaList, formula => CompileOne(formula, compiled, compilationErrors));
            ThrowIfCompilationErrors(compilationErrors);
            return new Dictionary<string, CompiledFormula>(compiled);
        }

        var results = new Dictionary<string, CompiledFormula>(formulaList.Count);
        List<(string Formula, Exception Error)>? sequentialErrors = null;
        foreach (var formula in formulaList)
        {
            try
            {
                results[formula] = _compiler.Compile(formula);
            }
            catch (Exception ex)
            {
                sequentialErrors ??= new List<(string Formula, Exception Error)>();
                sequentialErrors.Add((formula, ex));
            }
        }

        ThrowIfCompilationErrors(sequentialErrors);
        return results;
    }

    private void CompileOne<TDict>(string formula, TDict target, ConcurrentBag<(string Formula, Exception Error)> errors)
        where TDict : IDictionary<string, CompiledFormula>
    {
        try
        {
            target[formula] = _compiler.Compile(formula);
        }
        catch (Exception ex)
        {
            errors.Add((formula, ex));
        }
    }

    private static void ThrowIfCompilationErrors(ConcurrentBag<(string Formula, Exception Error)> errors)
    {
        if (errors.IsEmpty)
            return;

        throw CreateCompilationException(errors);
    }

    private static void ThrowIfCompilationErrors(List<(string Formula, Exception Error)>? errors)
    {
        if (errors is not { Count: > 0 })
            return;

        throw CreateCompilationException(errors);
    }

    private static AggregateException CreateCompilationException(IEnumerable<(string Formula, Exception Error)> errors)
    {
        var aggregate = new AggregateException(
            "One or more formulas failed to compile.",
            errors.Select(e => new InvalidOperationException(
                $"Failed to compile formula: '{e.Formula}'. Error: {e.Error.Message}", e.Error)));

        return aggregate;
    }

    private static double?[] EvaluateAll(
        IReadOnlyList<PreparedEvaluation> preparedEvaluations,
        DateTime calculationDate,
        bool parallel)
    {
        var values = new double?[preparedEvaluations.Count];

        if (parallel)
        {
            Parallel.For(0, preparedEvaluations.Count, index =>
                values[index] = preparedEvaluations[index].CompiledFormula.Evaluate(
                    preparedEvaluations[index].Context,
                    calculationDate));

            return values;
        }

        for (var index = 0; index < preparedEvaluations.Count; index++)
        {
            var evaluation = preparedEvaluations[index];
            values[index] = evaluation.CompiledFormula.Evaluate(evaluation.Context, calculationDate);
        }

        return values;
    }

    private static List<FormulaEvaluationResult> CreateSimpleResults(
        IReadOnlyList<CalculationWorkItem> workItems,
        IReadOnlyList<(string Formula, int ObjectId)> evaluationItems,
        IReadOnlyList<double?> values)
    {
        var result = new List<FormulaEvaluationResult>(workItems.Count);
        foreach (var workItem in workItems)
        {
            var value = values[GetEvaluationIndex(evaluationItems, workItem.Formula, workItem.EffectiveObjectId)];
            result.Add(new FormulaEvaluationResult(workItem.Formula, value));
        }

        return result;
    }

    private static List<FormulaCalculationResult> CreateSingleDateResults(
        DateTime date,
        IReadOnlyList<CalculationWorkItem> workItems,
        IReadOnlyList<int> workItemEvaluationIndexes,
        IReadOnlyList<double?> values)
    {
        var result = new List<FormulaCalculationResult>(workItems.Count);
        for (var workItemIndex = 0; workItemIndex < workItems.Count; workItemIndex++)
        {
            var workItem = workItems[workItemIndex];
            var value = values[workItemEvaluationIndexes[workItemIndex]];
            result.Add(new FormulaCalculationResult(
                workItem.ParameterId,
                workItem.ObjectId,
                workItem.Formula,
                [new FormulaCalculationValue(date, value)]));
        }

        return result;
    }

    private static List<FormulaCalculationResult> EvaluateRange(
        IReadOnlyList<CalculationWorkItem> workItems,
        IReadOnlyList<int> workItemEvaluationIndexes,
        IReadOnlyList<PreparedEvaluation> preparedEvaluations,
        IReadOnlyList<DateTime> calculationDates,
        bool parallel)
    {
        var valueArrays = new FormulaCalculationValue[preparedEvaluations.Count][];

        for (var evaluationIndex = 0; evaluationIndex < preparedEvaluations.Count; evaluationIndex++)
            valueArrays[evaluationIndex] = new FormulaCalculationValue[calculationDates.Count];

        if (parallel)
        {
            Parallel.For(0, calculationDates.Count, dateIndex =>
                FillRangeDate(
                    preparedEvaluations,
                    calculationDates,
                    valueArrays,
                    dateIndex));
        }
        else
        {
            for (var dateIndex = 0; dateIndex < calculationDates.Count; dateIndex++)
            {
                FillRangeDate(
                    preparedEvaluations,
                    calculationDates,
                    valueArrays,
                    dateIndex);
            }
        }

        var result = new List<FormulaCalculationResult>(workItems.Count);
        for (var workItemIndex = 0; workItemIndex < workItems.Count; workItemIndex++)
        {
            var workItem = workItems[workItemIndex];
            var evaluationIndex = workItemEvaluationIndexes[workItemIndex];
            result.Add(new FormulaCalculationResult(
                workItem.ParameterId,
                workItem.ObjectId,
                workItem.Formula,
                valueArrays[evaluationIndex]));
        }

        return result;
    }

    private static void FillRangeDate(
        IReadOnlyList<PreparedEvaluation> preparedEvaluations,
        IReadOnlyList<DateTime> calculationDates,
        FormulaCalculationValue[][] valueArrays,
        int dateIndex)
    {
        var calculationDate = calculationDates[dateIndex];

        for (var evaluationIndex = 0; evaluationIndex < preparedEvaluations.Count; evaluationIndex++)
        {
            var evaluation = preparedEvaluations[evaluationIndex];
            var value = evaluation.CompiledFormula.Evaluate(evaluation.Context, calculationDate);
            valueArrays[evaluationIndex][dateIndex] =
                new FormulaCalculationValue(calculationDate, value);
        }
    }

    private static Dictionary<int, FormulaContext> CreateObjectContexts(
        FormulaContext context,
        IReadOnlyList<CalculationWorkItem> workItems)
    {
        var result = new Dictionary<int, FormulaContext>();
        foreach (var workItem in workItems)
        {
            var objectId = workItem.EffectiveObjectId;
            if (!result.ContainsKey(objectId))
                result[objectId] = context.ForObject(objectId);
        }

        return result;
    }

    private static List<(string Formula, int ObjectId)> GetEvaluationItems(
        IReadOnlyList<CalculationWorkItem> workItems)
    {
        var result = new List<(string Formula, int ObjectId)>(workItems.Count);
        var seen = new HybridSet<(string Formula, int ObjectId)>();

        foreach (var workItem in workItems)
        {
            var item = (workItem.Formula, workItem.EffectiveObjectId);
            if (seen.Add(item))
                result.Add(item);
        }

        return result;
    }

    /// <summary>
    /// Строит запросы внешних данных: объединяет одинаковые p.* ссылки и берет максимальные диапазоны.
    /// </summary>
    private static PreparedEvaluation[] PrepareEvaluations(
        IReadOnlyList<(string Formula, int ObjectId)> evaluationItems,
        Dictionary<string, CompiledFormula> compiledFormulas,
        IReadOnlyDictionary<int, FormulaContext> objectContexts)
    {
        var result = new PreparedEvaluation[evaluationItems.Count];
        for (var index = 0; index < evaluationItems.Count; index++)
        {
            var (formula, objectId) = evaluationItems[index];
            result[index] = new PreparedEvaluation(
                formula,
                objectId,
                compiledFormulas[formula],
                objectContexts[objectId]);
        }

        return result;
    }

    private static PreparedEvaluation[] PrepareEvaluations(CalculationPlan plan, FormulaContext context)
    {
        var objectContexts = new FormulaContext[plan.ObjectIds.Length];
        for (var objectIndex = 0; objectIndex < plan.ObjectIds.Length; objectIndex++)
            objectContexts[objectIndex] = context.ForObject(plan.ObjectIds[objectIndex]);

        var result = new PreparedEvaluation[plan.EvaluationItems.Length];
        for (var evaluationIndex = 0; evaluationIndex < plan.EvaluationItems.Length; evaluationIndex++)
        {
            var (formula, objectId) = plan.EvaluationItems[evaluationIndex];
            result[evaluationIndex] = new PreparedEvaluation(
                formula,
                objectId,
                plan.EvaluationCompiledFormulas[evaluationIndex],
                objectContexts[plan.EvaluationObjectIndexes[evaluationIndex]]);
        }

        return result;
    }

    private static int GetEvaluationIndex(
        IReadOnlyList<(string Formula, int ObjectId)> evaluationItems,
        string formula,
        int objectId)
    {
        for (var index = 0; index < evaluationItems.Count; index++)
        {
            var item = evaluationItems[index];
            if (item.ObjectId == objectId && string.Equals(item.Formula, formula, StringComparison.Ordinal))
                return index;
        }

        throw new InvalidOperationException($"Evaluation item '{formula}' for object '{objectId}' not found.");
    }

    private static List<DataRequest> BuildDataRequests(
        IReadOnlyList<DataRequestTemplate> templates,
        DateTime calculationDate)
    {
        var requests = new List<DataRequest>(templates.Count);
        for (var i = 0; i < templates.Count; i++)
        {
            var template = templates[i];
            var (dateFrom, dateTo) = GetRequiredDataRange(template.Modifier, calculationDate);
            AddOrMergeRequest(requests, new DataRequest
            {
                ObjectId = template.ObjectId,
                Identifier = template.Identifier,
                DateFrom = dateFrom,
                DateTo = dateTo
            });
        }

        return requests;
    }

    private static List<DataRequest> BuildDataRequests(
        IReadOnlyList<DataRequestTemplate> templates,
        IReadOnlyList<DateTime> calculationDates)
    {
        var requests = new List<DataRequest>(templates.Count);
        for (var i = 0; i < templates.Count; i++)
        {
            var template = templates[i];
            var (dateFrom, dateTo) = GetRequiredDataRange(template.Modifier, calculationDates);
            AddOrMergeRequest(requests, new DataRequest
            {
                ObjectId = template.ObjectId,
                Identifier = template.Identifier,
                DateFrom = dateFrom,
                DateTo = dateTo
            });
        }

        return requests;
    }

    private static void AddOrMergeRequest(
        List<DataRequest> requests,
        DataRequest request)
    {
        for (var i = 0; i < requests.Count; i++)
        {
            var existing = requests[i];
            if (existing.ObjectId != request.ObjectId ||
                !string.Equals(existing.Identifier, request.Identifier, StringComparison.Ordinal))
            {
                continue;
            }

            requests[i] = MergeRequests(existing, request);
            return;
        }

        requests.Add(request);
    }

    private static List<DateTime> BuildCalculationDates(DateTime startDate, DateTime endDate, TimeSpan step)
    {
        var result = new List<DateTime>();
        for (var date = startDate; date <= endDate; date = date.Add(step))
            result.Add(date);

        return result;
    }

    private static (DateTime DateFrom, DateTime DateTo) GetRequiredDataRange(
        string? modifier,
        IReadOnlyList<DateTime> calculationDates)
    {
        if (calculationDates.Count == 0)
            throw new ArgumentException("At least one calculation date is required.", nameof(calculationDates));

        var minDate = DateTime.MaxValue;
        var maxDate = DateTime.MinValue;

        foreach (var calculationDate in calculationDates)
        {
            var (dateFrom, dateTo) = GetRequiredDataRange(modifier, calculationDate);
            if (dateFrom < minDate)
                minDate = dateFrom;
            if (dateTo > maxDate)
                maxDate = dateTo;
        }

        return (minDate, maxDate);
    }

    private static (DateTime DateFrom, DateTime DateTo) GetRequiredDataRange(
        string? modifier,
        DateTime calculationDate)
    {
        var monthBegin = new DateTime(calculationDate.Year, calculationDate.Month, 1);
        var yearBegin = new DateTime(calculationDate.Year, 1, 1);

        return modifier switch
        {
            // p.* без модификатора может попасть внутрь выражения с модификатором диапазона.
            null => (yearBegin, calculationDate),
            FormulaKeywords.MONTH_BEGIN => (monthBegin, monthBegin),
            FormulaKeywords.MONTH_NOW => (monthBegin, calculationDate),
            FormulaKeywords.YEAR_NOW => (yearBegin, calculationDate),
            _ => throw new InvalidOperationException($"Unknown modifier: {modifier}")
        };
    }

    /// <summary>
    /// Объединяет два запроса одной ссылки, беря максимальный диапазон.
    /// </summary>
    private static DataRequest MergeRequests(DataRequest req1, DataRequest req2)
    {
        return new DataRequest
        {
            ObjectId = req1.ObjectId,
            Identifier = req1.Identifier,
            DateFrom = req1.DateFrom < req2.DateFrom ? req1.DateFrom : req2.DateFrom,
            DateTo = req1.DateTo > req2.DateTo ? req1.DateTo : req2.DateTo
        };
    }
}
