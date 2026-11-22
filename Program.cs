using FormulaParser;
using FormulaParser.Data;

using var dbContext = FormulaDbContext.CreateWithTestData();
var processor = new FormulaProcessor<Dual<decimal>>(new EfDataProvider<Dual<decimal>>(dbContext));
var calculationDate = new DateTime(2024, 11, 21);
var result = processor.Calculate(new FormulaCalculation
{
    ObjectId = 345L,
    Formula = "sum([p.Oil]:month_now)"
}, calculationDate);

Console.WriteLine($"EF InMemory: {result.Formula} на {calculationDate:dd.MM.yyyy}");
Console.WriteLine($"FirstValue = {result.Values[0].Value?.FirstValue}");
Console.WriteLine($"SecondValue = {result.Values[0].Value?.SecondValue}");

var memoryProvider = new DataProvider<Dual<decimal>>();
for (var date = new DateTime(2024, 11, 1); date <= calculationDate; date = date.AddDays(1))
{
    decimal? secondValue = date.Day switch { 1 => 2.5m, 2 => 0m, _ => null };
    memoryProvider.SetParameterValue(345L, "Oil", date, new Dual<decimal>(1m, secondValue ?? 1m));
}
memoryProvider.SetParameterValue(345L, "Oil", new DateTime(2024, 11, 3, 12, 0, 0),
    new Dual<decimal>(null, 3m));

var memoryProcessor = new FormulaProcessor<Dual<decimal>>(memoryProvider);
var memoryResult = memoryProcessor.Calculate(new FormulaCalculation
{
    ObjectId = 345L,
    Formula = "sum([p.Oil]:month_now)"
}, calculationDate);

Console.WriteLine($"DataProvider: {memoryResult.Formula} на {calculationDate:dd.MM.yyyy}");
Console.WriteLine($"FirstValue = {memoryResult.Values[0].Value?.FirstValue}");
Console.WriteLine($"SecondValue = {memoryResult.Values[0].Value?.SecondValue}");

// using FormulaParser;
// using FormulaParser.Data;
// using System.Diagnostics;
// 
// var yearStart = new DateTime(2024, 1, 1);
// var yearEnd = new DateTime(2024, 12, 31);
// var step = TimeSpan.FromDays(1);
// const long object345 = 345;
// const long object346 = 346;
// 
// Console.WriteLine("=== Тест параметров по id и code name ===\n");
// Console.WriteLine($"Период расчета: {yearStart:dd.MM.yyyy} - {yearEnd:dd.MM.yyyy}");
// 
// var testFormulas = new List<FormulaCalculation>
// {
//     new()
//     {
//         ParameterId = 1001,
//         ObjectId = object345,
//         Formula = "sum((([p.6] - [p.Oil]) * [multiplier2]):year_now)"
//     },
//     new()
//     {
//         ParameterId = 1001,
//         ObjectId = object346,
//         Formula = "sum((([p.6] - [p.Oil]) * [multiplier2]):year_now)"
//     },
//     new()
//     {
//         ParameterId = 1002,
//         ObjectId = object346,
//         Formula = "sum((([obj.345.p.6] - [p.Oil]) * [multiplier3]):year_now)"
//     },
//     new()
//     {
//         ParameterId = 1003,
//         ObjectId = object345,
//         Formula = "sum((([p.6] - [obj.345.p.Oil]) * [multiplier4]):year_now)"
//     },
//     new()
//     {
//         ParameterId = 1004,
//         ObjectId = object346,
//         Formula = "sum((([obj.345.p.6] - [obj.345.p.Oil]) * [customparam]):year_now)"
//     }
// };
// const string simpleFormula = "([multiplier2] + [customparam]) * 10";
// 
// Console.WriteLine("Формулы:");
// for (var i = 0; i < testFormulas.Count; i++)
// {
//     var calculation = testFormulas[i];
//     Console.WriteLine(
//         $"  {i + 1}: p.{calculation.ParameterId}, obj.{calculation.ObjectId} = {calculation.Formula}");
// }
// Console.WriteLine($"Простая формула: {simpleFormula}");
// 
// const int iterations = 1;
// Console.WriteLine($"Количество итераций: {iterations:N0}\n");
// 
// using var dbContext = FormulaDbContext.CreateWithTestData();
// var efProvider = new EfDataProvider<double>(dbContext);
// efProvider.SetValue("multiplier2", 2.0);
// efProvider.SetValue("multiplier3", 3.0);
// efProvider.SetValue("multiplier4", 4.0);
// efProvider.SetValue("customparam", 1.0);
// 
// var efProcessor = new FormulaProcessor<double>(efProvider);
// 
// var swEf = Stopwatch.StartNew();
// IReadOnlyList<FormulaCalculationResult<double>> efResults = null!;
// for (var iteration = 0; iteration < iterations; iteration++)
//     efResults = efProcessor.Calculate(testFormulas, yearStart, yearEnd, step, parallel: false);
// swEf.Stop();
// 
// PrintResults("EF InMemory", efResults, swEf.Elapsed);
// 
// var memoryProvider = new DataProvider<double>();
// memoryProvider.SetValue("multiplier2", 2.0);
// memoryProvider.SetValue("multiplier3", 3.0);
// memoryProvider.SetValue("multiplier4", 4.0);
// memoryProvider.SetValue("customparam", 1.0);
// 
// for (var date = yearStart; date <= yearEnd; date = date.AddDays(1))
// {
//     memoryProvider.SetParameterValue(object345, 6, date, 2.0);
//     memoryProvider.SetParameterValue(object345, "Oil", date, 1.0);
//     memoryProvider.SetParameterValue(object346, 6, date, 3.0);
//     memoryProvider.SetParameterValue(object346, "Oil", date, 1.0);
// }
// 
// var memoryProcessor = new FormulaProcessor<double>(memoryProvider);
// 
// var swMemory = Stopwatch.StartNew();
// IReadOnlyList<FormulaCalculationResult<double>> memoryResults = null!;
// FormulaEvaluationResult<double> simpleMemoryResult = null!;
// for (var iteration = 0; iteration < iterations; iteration++)
// {
//     memoryResults = memoryProcessor.Calculate(testFormulas, yearStart, yearEnd, step, parallel: false);
//     simpleMemoryResult = memoryProcessor.Evaluate(simpleFormula);
// }
// swMemory.Stop();
// 
// PrintResults("DataProvider<double>", memoryResults, swMemory.Elapsed);
// PrintSimpleResult("Simple", simpleMemoryResult);
// 
// static void PrintResults(string title, IReadOnlyList<FormulaCalculationResult<double>> results, TimeSpan elapsed)
// { 
//     Console.WriteLine($"{title}: {elapsed.TotalMilliseconds:N0} мс ({elapsed.TotalSeconds:F2} сек)");
//     var lastDate = results[0].Values[^1].Date;
//     Console.WriteLine($"Дата результата: {lastDate:dd.MM.yyyy HH:mm:ss}");
// 
//     foreach (var formula in results)
//     {
//         Console.WriteLine($"p.{formula.ParameterId}, obj.{formula.ObjectId} = {formula.Formula}");
//         Console.WriteLine($"  value = {formula.Values[^1].Value}");
//     }
// 
//     Console.WriteLine();
// }
// 
// static void PrintSimpleResult(string title, FormulaEvaluationResult<double> result)
// {
//     Console.WriteLine($"{title}: {result.Formula}");
//     Console.WriteLine($"  value = {result.Value}");
//     Console.WriteLine();
// }
// 
