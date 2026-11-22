using System.Numerics;
using System.Collections.Concurrent;

namespace FormulaParser;

/// <summary>
/// Обработчик формул с оптимизацией запросов к БД.
/// Умеет обрабатывать как массив формул, так и одну формулу.
/// </summary>
public sealed class FormulaProcessor<T>
    where T : struct, IFloatingPoint<T>
{
    private const long DefaultObjectId = 0;
    private static readonly Dictionary<string, T> NoCustomValues = new(StringComparer.Ordinal);

    private readonly record struct CalculationWorkItem(
        int? ParameterId,
        long? ObjectId,
        string Formula,
        long EffectiveObjectId);

    private readonly record struct PreparedEvaluation(
        string Formula,
        long ObjectId,
        CompiledFormula<T> CompiledFormula,
        FormulaContext<T> Context);

    private readonly record struct DataRequestTemplate(
        long ObjectId,
        string Identifier,
        string? Modifier);

    private sealed class CalculationPlan
    {
        public required Dictionary<ParameterDataKey, string?> Definitions { get; init; }
        public required bool HasSelections { get; init; }
        public required CalculationWorkItem[] WorkItems { get; init; }
        public required (string Formula, long ObjectId)[] EvaluationItems { get; init; }
        public required CompiledFormula<T>[] EvaluationCompiledFormulas { get; init; }
        public required int[] EvaluationObjectIndexes { get; init; }
        public required int[] WorkItemEvaluationIndexes { get; init; }
        public required long[] ObjectIds { get; init; }
        public required DataRequestTemplate[] DataRequestTemplates { get; init; }
        public required string[] CustomNames { get; init; }
    }

    private readonly FormulaCompiler<T> _compiler;
    private readonly IDataProvider<T> _dataProvider;
    private readonly object _calculationPlanLock = new();
    private CalculationPlan? _lastCalculationPlan;

    public FormulaProcessor(IDataProvider<T> dataProvider, FormulaSyntax? syntax = null)
    {
        ArgumentNullException.ThrowIfNull(dataProvider);

        _dataProvider = dataProvider;
        _compiler = new FormulaCompiler<T>(syntax);
    }

    /// <summary>
    /// Вычислить простую формулу без внешних параметров данных.
    /// </summary>
    public FormulaEvaluationResult<T> Evaluate(string formula)
    {
        ArgumentException.ThrowIfNullOrEmpty(formula);
        return Evaluate([formula], parallel: false)[0];
    }

    /// <summary>
    /// Вычислить простые формулы без внешних параметров данных.
    /// </summary>
    public IReadOnlyList<FormulaEvaluationResult<T>> Evaluate(
        IEnumerable<string> formulas,
        bool parallel = true) =>
        ProcessSimple(CreateSimpleWorkItems(formulas), parallel);

    /// <summary>
    /// Посчитать одну расчетную строку на текущую дату.
    /// </summary>
    public FormulaCalculationResult<T> Calculate(FormulaCalculation calculation) =>
        Calculate(calculation, DateTime.Today);

    /// <summary>
    /// Посчитать одну расчетную строку на указанную дату.
    /// </summary>
    public FormulaCalculationResult<T> Calculate(FormulaCalculation calculation, DateTime calculationDate)
    {
        ArgumentNullException.ThrowIfNull(calculation);
        return Calculate([calculation], calculationDate, parallel: false)[0];
    }

    /// <summary>
    /// Посчитать одну расчетную строку на диапазоне дат.
    /// </summary>
    public FormulaCalculationResult<T> Calculate(
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
    public IReadOnlyList<FormulaCalculationResult<T>> Calculate(
        IEnumerable<FormulaCalculation> calculations,
        bool parallel = true) =>
        Calculate(calculations, DateTime.Today, parallel);

    /// <summary>
    /// Посчитать набор расчетных строк на указанную дату.
    /// </summary>
    public IReadOnlyList<FormulaCalculationResult<T>> Calculate(
        IEnumerable<FormulaCalculation> calculations,
        DateTime calculationDate,
        bool parallel = true) =>
        Process(GetOrCreateCalculationPlan(calculations, parallel), calculationDate, parallel);

    /// <summary>
    /// Посчитать набор расчетных строк на диапазоне дат.
    /// </summary>
    public IReadOnlyList<FormulaCalculationResult<T>> Calculate(
        IEnumerable<FormulaCalculation> calculations,
        DateTime startDate,
        DateTime endDate,
        TimeSpan step,
        bool parallel = true) =>
        Process(GetOrCreateCalculationPlan(calculations, parallel), startDate, endDate, step, parallel);

    private IReadOnlyList<FormulaEvaluationResult<T>> ProcessSimple(
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

        var context = new FormulaContext<T>(new Dictionary<ParameterDataKey, ParameterDataSeries<T>>(), customValues);
        var objectContexts = CreateObjectContexts(context, workItems);
        var preparedEvaluations = PrepareEvaluations(evaluationItems, compiledFormulas, objectContexts);
        var values = EvaluateAll(
            preparedEvaluations,
            DateTime.MinValue,
            parallel);

        return CreateSimpleResults(workItems, evaluationItems, values);
    }

    private IReadOnlyList<FormulaCalculationResult<T>> Process(
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
        var context = new FormulaContext<T>(data, customValues);

        // 4. Вычисляем все формулы по каждому объекту.
        var preparedEvaluations = PrepareEvaluations(plan, context);
        var values = EvaluateAll(preparedEvaluations, calculationDate, parallel);
        return CreateSingleDateResults(calculationDate, plan.WorkItems, plan.WorkItemEvaluationIndexes, values);
    }

    private IReadOnlyList<FormulaCalculationResult<T>> Process(
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
        var context = new FormulaContext<T>(data, customValues);
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
        var seen = new HybridSet<(int? ParameterId, long ObjectId, string Formula)>();

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
        lock (_calculationPlanLock)
        {
            var cached = _lastCalculationPlan;
            Dictionary<ParameterDataKey, string?>? refreshed = null;
            if (cached != null && !cached.HasSelections && MatchesPlan(cached, calculationList))
            {
                refreshed = LoadDefinitions(cached.Definitions.Keys.ToArray());
                if (cached.Definitions.All(entry => refreshed[entry.Key] == entry.Value))
                    return cached;
            }

            cached = CreateCalculationPlan(calculationList, parallel, refreshed);
            _lastCalculationPlan = cached;
            return cached;
        }
    }

    private CalculationPlan CreateCalculationPlan(
        IReadOnlyList<FormulaCalculation> calculations,
        bool parallel,
        Dictionary<ParameterDataKey, string?>? knownDefinitions = null)
    {
        var workItems = CreateWorkItems(calculations).ToArray();
        var formulaList = GetFormulaList(workItems);
        var parsedFormulas = CompileAll(formulaList, parallel);
        var evaluationItems = GetEvaluationItems(workItems).ToArray();
        var definitions = new Dictionary<ParameterDataKey, string?>();
        var localDefinitions = new Dictionary<ParameterDataKey, string>();
        foreach (var row in calculations.Where(c => c.ParameterId.HasValue))
        {
            var key = new ParameterDataKey(row.ObjectId, row.ParameterId!.Value.ToString(System.Globalization.CultureInfo.InvariantCulture));
            if (!localDefinitions.TryAdd(key, row.Formula))
                throw new ArgumentException($"Multiple formula definitions for {key}.", nameof(calculations));
        }
        var frontier = new HashSet<ParameterDataKey>();
        var pendingSelections = new HashSet<(ParameterInfo Parameter, long ObjectId)>();
        var selectedObjects = new Dictionary<(ParameterInfo Parameter, long ObjectId), long[]>();
        var selectorCache = new Dictionary<ObjectSelector, long[]>();
        foreach (var item in evaluationItems)
            CollectReferences(parsedFormulas[item.Formula], item.ObjectId);
        var visited = new HashSet<ParameterDataKey>();
        while (frontier.Count != 0 || pendingSelections.Count != 0)
        {
            // Expand every currently known plain formula before resolving newly discovered selectors.
            if (frontier.Count != 0)
            {
                var current = frontier.Where(visited.Add).ToArray();
                frontier.Clear();
                var unknown = current.Where(key => !localDefinitions.ContainsKey(key) &&
                    (knownDefinitions == null || !knownDefinitions.ContainsKey(key))).ToArray();
                var loaded = LoadDefinitions(unknown);
                foreach (var key in current)
                {
                    if (!localDefinitions.TryGetValue(key, out var definition))
                    {
                        definition = knownDefinitions != null && knownDefinitions.TryGetValue(key, out var known)
                            ? known : loaded[key];
                        definitions[key] = definition;
                    }
                    if (definition != null)
                        CollectReferences(_compiler.Compile(definition), key.ObjectId);
                }
                continue;
            }

            var missing = new HashSet<ObjectSelector>();
            foreach (var reference in pendingSelections.ToArray())
            {
                if (!TryResolveSelection(reference.Parameter, reference.ObjectId, missing, out var objects)) continue;
                selectedObjects[reference] = objects;
                pendingSelections.Remove(reference);
                foreach (var id in objects)
                    frontier.Add(new ParameterDataKey(id, reference.Parameter.ParameterId));
            }
            if (frontier.Count != 0) continue;
            if (missing.Count != 0)
            {
                var requests = missing.ToArray();
                var resolved = _dataProvider.ResolveObjects(requests);
                foreach (var request in requests)
                    selectorCache[request] = resolved.TryGetValue(request, out var ids)
                        ? ids.Where(id => id != request.ObjectId).Distinct().Order().ToArray() : [];
            }
        }

        void CollectReferences(CompiledFormula<T> formula, long objectId)
        {
            foreach (var parameter in formula.Parameters)
            {
                if (parameter.Kind != ParameterKind.Parameter) continue;
                if (parameter.HasSelection)
                {
                    var reference = (parameter, objectId);
                    if (!selectedObjects.ContainsKey(reference)) pendingSelections.Add(reference);
                }
                else frontier.Add(new ParameterDataKey(parameter.ObjectId ?? objectId, parameter.ParameterId));
            }
        }

        bool TryResolveSelection(ParameterInfo parameter, long objectId, HashSet<ObjectSelector> missing, out long[] objects)
        {
            objects = [];
            var origin = parameter.ObjectId ?? objectId;
            var first = parameter.Ancestor ?? parameter.Descendant!.Value;
            var request = new ObjectSelector(origin, first.Direction, first.ObjectType, first.RelationType);
            if (!selectorCache.TryGetValue(request, out var roots)) { missing.Add(request); return false; }
            if (!parameter.Ancestor.HasValue || !parameter.Descendant.HasValue) { objects = roots; return true; }
            var descendant = parameter.Descendant.Value;
            var found = new HashSet<long>();
            var complete = true;
            foreach (var root in roots)
            {
                var next = new ObjectSelector(root, descendant.Direction, descendant.ObjectType, descendant.RelationType);
                if (!selectorCache.TryGetValue(next, out var children)) { missing.Add(next); complete = false; }
                else found.UnionWith(children);
            }
            objects = found.Order().ToArray();
            return complete;
        }
        var compiledFormulas = new Dictionary<(string Formula, long ObjectId), CompiledFormula<T>>();
        var expanded = new Dictionary<ParameterDataKey, AstNode<T>>();
        var path = new List<ParameterDataKey>();
        foreach (var item in evaluationItems)
            compiledFormulas[item] = new CompiledFormula<T>(Expand(parsedFormulas[item.Formula].Root, item.ObjectId));

        AstNode<T> Expand(AstNode<T> node, long objectId)
        {
            switch (node)
            {
                case ParameterNode<T> p when p.Parameter.Kind == ParameterKind.Parameter:
                    var parameter = p.Parameter;
                    if (parameter.HasSelection)
                    {
                        var objects = selectedObjects[(parameter, objectId)];
                        return new ObjectSetNode<T>(objects.Select(id => Expand(new ParameterNode<T>(new ParameterInfo
                        {
                            Kind = ParameterKind.Parameter, ObjectId = id, ParameterId = parameter.ParameterId,
                            Modifier = parameter.Modifier
                        }), id)).ToArray());
                    }
                    var key = new ParameterDataKey(parameter.ObjectId ?? objectId, parameter.ParameterId);
                    if (!localDefinitions.TryGetValue(key, out var definition))
                        definition = definitions[key];
                    if (definition == null)
                        return new ParameterNode<T>(new ParameterInfo { Kind = parameter.Kind, ObjectId = key.ObjectId,
                            ParameterId = key.Identifier, Modifier = parameter.Modifier });
                    if (path.Contains(key))
                        throw new InvalidOperationException("Circular formula reference: " + string.Join(" -> ", path.Append(key)));
                    if (!expanded.TryGetValue(key, out var expression))
                    {
                        path.Add(key);
                        try { expression = Expand(_compiler.Compile(definition).Root, key.ObjectId); }
                        finally { path.RemoveAt(path.Count - 1); }
                        expanded[key] = expression;
                    }
                    return parameter.Modifier == null ? expression : new ModifiedExpressionNode<T>(expression, parameter.Modifier);
                case BinaryOperationNode<T> b:
                    var left = Expand(b.Left, objectId);
                    var right = Expand(b.Right, objectId);
                    if (left is ObjectSetNode<T> && right is ObjectSetNode<T>)
                        throw new InvalidOperationException("Aggregate each object set before combining two sets.");
                    if (left is ObjectSetNode<T> leftSet)
                        return new ObjectSetNode<T>(leftSet.Expressions.Select(value => (AstNode<T>)new BinaryOperationNode<T>(value, right, b.Operator)).ToArray());
                    if (right is ObjectSetNode<T> rightSet)
                        return new ObjectSetNode<T>(rightSet.Expressions.Select(value => (AstNode<T>)new BinaryOperationNode<T>(left, value, b.Operator)).ToArray());
                    return new BinaryOperationNode<T>(left, right, b.Operator);
                case FunctionCallNode<T> f:
                    return new FunctionCallNode<T>(f.FunctionName, f.Arguments.Select(a => Expand(a, objectId)).ToList());
                case ModifiedExpressionNode<T> m:
                    var modifiedExpression = Expand(m.Expression, objectId);
                    return modifiedExpression is ObjectSetNode<T> set
                        ? new ObjectSetNode<T>(set.Expressions.Select(value => (AstNode<T>)new ModifiedExpressionNode<T>(value, m.Modifier)).ToArray())
                        : new ModifiedExpressionNode<T>(modifiedExpression, m.Modifier);
                default:
                    return node;
            }
        }
        var objectIds = GetObjectIds(evaluationItems);
        var (dataRequestTemplates, customNames) = CreateDataRequestPlan(evaluationItems, compiledFormulas);

        return new CalculationPlan
        {
            Definitions = definitions,
            HasSelections = selectorCache.Count != 0,
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

    private Dictionary<ParameterDataKey, string?> LoadDefinitions(IReadOnlyCollection<ParameterDataKey> keys)
    {
        var result = new Dictionary<ParameterDataKey, string?>();
        if (keys.Count == 0)
            return result;
        var formulas = _dataProvider.GetFormulas(keys);
        foreach (var key in keys)
            result[key] = formulas.GetValueOrDefault(key);
        return result;
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
        IReadOnlyList<(string Formula, long ObjectId)> evaluationItems,
        IReadOnlyDictionary<(string Formula, long ObjectId), CompiledFormula<T>> compiledFormulas)
    {
        var dataRequestTemplates = new List<DataRequestTemplate>();
        var seenDataRequestTemplates = new HybridSet<DataRequestTemplate>();
        var customNames = new List<string>();
        var seenCustomNames = new HybridSet<string>(StringComparer.Ordinal);

        foreach (var (formula, objectId) in evaluationItems)
        {
            var compiled = compiledFormulas[(formula, objectId)];
            foreach (var dependency in compiled.DataDependencies)
            {
                var param = dependency.Parameter;
                if (param.Kind == ParameterKind.Custom)
                {
                    if (seenCustomNames.Add(param.ParameterId))
                        customNames.Add(param.ParameterId);
                    continue;
                }

                var template = new DataRequestTemplate(
                    param.ObjectId ?? objectId,
                    param.ParameterId,
                    dependency.ModifierPath);

                if (seenDataRequestTemplates.Add(template))
                    dataRequestTemplates.Add(template);
            }
        }

        return (dataRequestTemplates.ToArray(), customNames.ToArray());
    }

    private static CompiledFormula<T>[] GetEvaluationCompiledFormulas(
        IReadOnlyList<(string Formula, long ObjectId)> evaluationItems,
        IReadOnlyDictionary<(string Formula, long ObjectId), CompiledFormula<T>> compiledFormulas)
    {
        var result = new CompiledFormula<T>[evaluationItems.Count];
        for (var i = 0; i < evaluationItems.Count; i++)
            result[i] = compiledFormulas[evaluationItems[i]];

        return result;
    }

    private static int[] GetEvaluationObjectIndexes(
        IReadOnlyList<(string Formula, long ObjectId)> evaluationItems,
        IReadOnlyList<long> objectIds)
    {
        var result = new int[evaluationItems.Count];
        for (var i = 0; i < evaluationItems.Count; i++)
            result[i] = GetObjectIndex(objectIds, evaluationItems[i].ObjectId);

        return result;
    }

    private static int[] GetWorkItemEvaluationIndexes(
        IReadOnlyList<CalculationWorkItem> workItems,
        IReadOnlyList<(string Formula, long ObjectId)> evaluationItems)
    {
        var result = new int[workItems.Count];
        for (var i = 0; i < workItems.Count; i++)
        {
            var workItem = workItems[i];
            result[i] = GetEvaluationIndex(evaluationItems, workItem.Formula, workItem.EffectiveObjectId);
        }

        return result;
    }

    private static long[] GetObjectIds(IReadOnlyList<(string Formula, long ObjectId)> evaluationItems)
    {
        var result = new List<long>(evaluationItems.Count);
        foreach (var (_, objectId) in evaluationItems)
        {
            if (!ContainsObjectId(result, objectId))
                result.Add(objectId);
        }

        return result.ToArray();
    }

    private static bool ContainsObjectId(IReadOnlyList<long> objectIds, long objectId) =>
        GetObjectIndexOrDefault(objectIds, objectId) >= 0;

    private static int GetObjectIndex(IReadOnlyList<long> objectIds, long objectId)
    {
        var index = GetObjectIndexOrDefault(objectIds, objectId);
        if (index >= 0)
            return index;

        throw new InvalidOperationException($"Object '{objectId}' not found in calculation plan.");
    }

    private static int GetObjectIndexOrDefault(IReadOnlyList<long> objectIds, long objectId)
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
        Dictionary<string, CompiledFormula<T>> compiledFormulas)
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

    private Dictionary<string, CompiledFormula<T>> CompileAll(IReadOnlyList<string> formulaList, bool parallel)
    {
        var compilationErrors = new ConcurrentBag<(string Formula, Exception Error)>();

        if (parallel)
        {
            var compiled = new ConcurrentDictionary<string, CompiledFormula<T>>();
            Parallel.ForEach(formulaList, formula => CompileOne(formula, compiled, compilationErrors));
            ThrowIfCompilationErrors(compilationErrors);
            return new Dictionary<string, CompiledFormula<T>>(compiled);
        }

        var results = new Dictionary<string, CompiledFormula<T>>(formulaList.Count);
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
        where TDict : IDictionary<string, CompiledFormula<T>>
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

    private static T?[] EvaluateAll(
        IReadOnlyList<PreparedEvaluation> preparedEvaluations,
        DateTime calculationDate,
        bool parallel)
    {
        var values = new T?[preparedEvaluations.Count];

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

    private static List<FormulaEvaluationResult<T>> CreateSimpleResults(
        IReadOnlyList<CalculationWorkItem> workItems,
        IReadOnlyList<(string Formula, long ObjectId)> evaluationItems,
        IReadOnlyList<T?> values)
    {
        var result = new List<FormulaEvaluationResult<T>>(workItems.Count);
        foreach (var workItem in workItems)
        {
            var value = values[GetEvaluationIndex(evaluationItems, workItem.Formula, workItem.EffectiveObjectId)];
            result.Add(new FormulaEvaluationResult<T>(workItem.Formula, value));
        }

        return result;
    }

    private static List<FormulaCalculationResult<T>> CreateSingleDateResults(
        DateTime date,
        IReadOnlyList<CalculationWorkItem> workItems,
        IReadOnlyList<int> workItemEvaluationIndexes,
        IReadOnlyList<T?> values)
    {
        var result = new List<FormulaCalculationResult<T>>(workItems.Count);
        for (var workItemIndex = 0; workItemIndex < workItems.Count; workItemIndex++)
        {
            var workItem = workItems[workItemIndex];
            var value = values[workItemEvaluationIndexes[workItemIndex]];
            result.Add(new FormulaCalculationResult<T>(
                workItem.ParameterId,
                workItem.ObjectId,
                workItem.Formula,
                [new FormulaCalculationValue<T>(date, value)]));
        }

        return result;
    }

    private static List<FormulaCalculationResult<T>> EvaluateRange(
        IReadOnlyList<CalculationWorkItem> workItems,
        IReadOnlyList<int> workItemEvaluationIndexes,
        IReadOnlyList<PreparedEvaluation> preparedEvaluations,
        IReadOnlyList<DateTime> calculationDates,
        bool parallel)
    {
        var valueArrays = new FormulaCalculationValue<T>[preparedEvaluations.Count][];

        for (var evaluationIndex = 0; evaluationIndex < preparedEvaluations.Count; evaluationIndex++)
            valueArrays[evaluationIndex] = new FormulaCalculationValue<T>[calculationDates.Count];

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

        var result = new List<FormulaCalculationResult<T>>(workItems.Count);
        for (var workItemIndex = 0; workItemIndex < workItems.Count; workItemIndex++)
        {
            var workItem = workItems[workItemIndex];
            var evaluationIndex = workItemEvaluationIndexes[workItemIndex];
            result.Add(new FormulaCalculationResult<T>(
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
        FormulaCalculationValue<T>[][] valueArrays,
        int dateIndex)
    {
        var calculationDate = calculationDates[dateIndex];

        for (var evaluationIndex = 0; evaluationIndex < preparedEvaluations.Count; evaluationIndex++)
        {
            var evaluation = preparedEvaluations[evaluationIndex];
            var value = evaluation.CompiledFormula.Evaluate(evaluation.Context, calculationDate);
            valueArrays[evaluationIndex][dateIndex] =
                new FormulaCalculationValue<T>(calculationDate, value);
        }
    }

    private static Dictionary<long, FormulaContext<T>> CreateObjectContexts(
        FormulaContext<T> context,
        IReadOnlyList<CalculationWorkItem> workItems)
    {
        var result = new Dictionary<long, FormulaContext<T>>();
        foreach (var workItem in workItems)
        {
            var objectId = workItem.EffectiveObjectId;
            if (!result.ContainsKey(objectId))
                result[objectId] = context.ForObject(objectId);
        }

        return result;
    }

    private static List<(string Formula, long ObjectId)> GetEvaluationItems(
        IReadOnlyList<CalculationWorkItem> workItems)
    {
        var result = new List<(string Formula, long ObjectId)>(workItems.Count);
        var seen = new HybridSet<(string Formula, long ObjectId)>();

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
        IReadOnlyList<(string Formula, long ObjectId)> evaluationItems,
        Dictionary<string, CompiledFormula<T>> compiledFormulas,
        IReadOnlyDictionary<long, FormulaContext<T>> objectContexts)
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

    private static PreparedEvaluation[] PrepareEvaluations(CalculationPlan plan, FormulaContext<T> context)
    {
        var objectContexts = new FormulaContext<T>[plan.ObjectIds.Length];
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
        IReadOnlyList<(string Formula, long ObjectId)> evaluationItems,
        string formula,
        long objectId)
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
        var from = calculationDate;
        var to = calculationDate;
        if (modifier == null)
            return (from, to);

        // Apply outer expression modifiers before inner parameter modifiers.
        foreach (var part in modifier.Split('/'))
        {
            var (start, _) = GetSingleModifierRange(part, from);
            var (_, end) = GetSingleModifierRange(part, to);
            from = start;
            to = end;
        }

        return (from, to);
    }

    private static (DateTime DateFrom, DateTime DateTo) GetSingleModifierRange(
        string modifier,
        DateTime calculationDate)
    {
        var monthBegin = new DateTime(calculationDate.Year, calculationDate.Month, 1);
        var yearBegin = new DateTime(calculationDate.Year, 1, 1);

        return modifier switch
        {
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
