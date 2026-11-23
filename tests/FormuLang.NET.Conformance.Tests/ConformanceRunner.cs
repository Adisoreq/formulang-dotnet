using FormuLang.NET.Abstractions.Primitives;
using FormuLang.NET.Core.Primitives;
using FormuLang.NET.Core.Processing;
using FormuLang.NET.Core.Structures;
using FormuLang.NET.Extensions.Stock;
using FormuLang.NET.Extensions.Stock.Contexts;
using FormuLang.NET.Extensions.Stock.Registry;
using System.Text.Json;
using Env = FormuLang.NET.Core.Structures.Environment;
using FlObject = FormuLang.NET.Core.Primitives.Object;

namespace FormuLang.NET.Conformance.Tests
{
    /// <summary>
    /// One test case of a conformance file, with the <c>defaults</c> of its file already applied.
    /// </summary>
    public sealed record ConformanceCase(
        string Id,
        string File,
        IReadOnlyList<string> Standards,
        JsonElement Context,
        JsonElement? Environment,
        JsonElement? Registry,
        string Formula,
        JsonElement Expected);

    /// <summary>
    /// Runner of the language-independent conformance tests of the standard (FormuLang <c>tests/README.md</c>): builds
    /// the environment, the context and the registry of a case, executes its formula and compares the outcome with
    /// <c>expected</c>.
    /// </summary>
    public static class ConformanceRunner
    {
        /// <summary>
        /// Standards implemented by the library. A case that requires another standard is skipped.
        /// </summary>
        public static IReadOnlySet<string> SupportedStandards { get; } = new HashSet<string>
        {
            Core.Standards.CoreStandard.Identifier,
            StockStandard.Identifier,
        };

        public static string Directory { get; } = Path.Combine(AppContext.BaseDirectory, "conformance");

        private static readonly Lazy<IReadOnlyDictionary<string, ConformanceCase>> AllCases = new(Load);

        public static IReadOnlyDictionary<string, ConformanceCase> Cases => AllCases.Value;

        // Loading

        private static IReadOnlyDictionary<string, ConformanceCase> Load()
        {
            var cases = new Dictionary<string, ConformanceCase>(StringComparer.Ordinal);

            foreach (string path in System.IO.Directory.EnumerateFiles(Directory, "*.json", SearchOption.AllDirectories).Order())
            {
                if (Path.GetFileName(path) == "schema.json")
                    continue;

                string file = Path.GetRelativePath(Directory, path).Replace('\\', '/');
                using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path));
                JsonElement root = document.RootElement.Clone();

                string[] standards = root.GetProperty("standards").EnumerateArray().Select(s => s.GetString()!).ToArray();
                JsonElement? defaults = root.TryGetProperty("defaults", out JsonElement d) ? d : null;

                foreach (JsonElement item in root.GetProperty("cases").EnumerateArray())
                {
                    string id = item.GetProperty("id").GetString()!;

                    JsonElement? Pick(string key)
                    {
                        if (item.TryGetProperty(key, out JsonElement own))
                            return own;
                        if (defaults is JsonElement def && def.TryGetProperty(key, out JsonElement inherited))
                            return inherited;
                        return null;
                    }

                    string[] caseStandards = item.TryGetProperty("standards", out JsonElement extra)
                        ? standards.Concat(extra.EnumerateArray().Select(s => s.GetString()!)).ToArray()
                        : standards;

                    var testCase = new ConformanceCase(
                        id, file, caseStandards,
                        Pick("context") ?? JsonDocument.Parse("""{ "type": "Default" }""").RootElement,
                        Pick("environment"),
                        Pick("registry"),
                        ReadFormula(item.GetProperty("formula")),
                        item.GetProperty("expected"));

                    if (!cases.TryAdd(id, testCase))
                        throw new InvalidDataException($"The case identifier \"{id}\" is used more than once.");
                }
            }

            return cases;
        }

        /// <summary>
        /// A formula is a text, or an array of lines joined with a new line.
        /// </summary>
        private static string ReadFormula(JsonElement formula) => formula.ValueKind == JsonValueKind.Array
            ? string.Join('\n', formula.EnumerateArray().Select(line => line.GetString()))
            : formula.GetString()!;

        // Running

        /// <summary>
        /// Executes the case and returns the problems found; an empty list means that the case passed.
        /// </summary>
        public static IReadOnlyList<string> Run(ConformanceCase testCase)
        {
            Env environment = CreateEnvironment(testCase);
            JsonProperty expected = testCase.Expected.EnumerateObject().Single();
            var problems = new List<string>();

            switch (expected.Name)
            {
                case "errors":
                    CompareValidation(Validator.Validate(testCase.Formula, environment).Errors, expected.Value, problems);
                    break;

                case "results":
                    IReadOnlyList<ExecutionResult> results = ExecuteAll(testCase, environment);
                    JsonElement[] expectedResults = expected.Value.EnumerateArray().ToArray();

                    if (results.Count != expectedResults.Length)
                        problems.Add($"{results.Count} iterations were executed, {expectedResults.Length} expected.");

                    for (int i = 0; i < Math.Min(results.Count, expectedResults.Length); i++)
                        CompareOutcome(results[i], expectedResults[i], $"iteration {i}: ", problems);
                    break;

                case "result":
                    CompareOutcome(ExecuteSingle(testCase, environment), expected.Value, "", problems);
                    break;

                case "error":
                    ExecutionResult result = ExecuteSingle(testCase, environment);
                    string code = expected.Value.GetProperty("code").GetString()!;

                    if (result.Successful)
                        problems.Add($"Expected {code}, but the execution succeeded with {result}.");
                    else if (result.Error.Name != code)
                        problems.Add($"Expected {code}, got {result.Error.Name}: {result.Error.Message}");
                    else if (expected.Value.TryGetProperty("message", out JsonElement message) && message.GetString() != result.Error.Message)
                        problems.Add($"Expected the message \"{message.GetString()}\", got \"{result.Error.Message}\".");
                    break;

                default:
                    problems.Add($"Unknown expected outcome \"{expected.Name}\".");
                    break;
            }

            return problems;
        }

        private static string ContextType(ConformanceCase testCase) => testCase.Context.GetProperty("type").GetString()!;

        private static ExecutionResult ExecuteSingle(ConformanceCase testCase, Env environment)
        {
            if (ContextType(testCase) == "Default")
                return Executor.Create().Execute(testCase.Formula, Context.Create(environment));

            // A single outcome of an iterated context: the first failed iteration, or the last one
            IReadOnlyList<ExecutionResult> results = ExecuteAll(testCase, environment);
            return results.FirstOrDefault(r => !r.Successful) ?? results[^1];
        }

        private static IReadOnlyList<ExecutionResult> ExecuteAll(ConformanceCase testCase, Env environment)
        {
            var executor = Executor.Create();
            JsonElement context = testCase.Context;

            switch (ContextType(testCase))
            {
                case "Default":
                    return [executor.Execute(testCase.Formula, Context.Create(environment))];

                case "Iteration":
                    int n = context.GetProperty("n").GetInt32();
                    return executor.ExecuteIterations(testCase.Formula, IterationContext.Create(environment, n), stopOnError: false).Results;

                case "Time":
                    var frames = context.GetProperty("frames").EnumerateArray().Select(frame => (
                        frame.GetProperty("time_s").GetDouble(),
                        frame.GetProperty("dtime_s").GetDouble(),
                        frame.TryGetProperty("now", out JsonElement now) ? now.GetDouble() : (double?)null)).ToArray();
                    return executor.ExecuteFrames(testCase.Formula, TimeContext.Create(environment), frames, stopOnError: false).Results;

                default:
                    return executor.ExecuteIterations(testCase.Formula, CreateStockContext(context, environment, executor), stopOnError: false).Results;
            }
        }

        private static StockContext CreateStockContext(JsonElement context, Env environment, Executor executor)
        {
            Candle[] candles = context.GetProperty("candles").EnumerateArray().Select(ReadCandle).ToArray();
            IObject? parameters = context.TryGetProperty("parameters", out JsonElement p) ? (IObject)ToValue(p).Raw! : null;

            return context.GetProperty("type").GetString() switch
            {
                "Stock" => StockContext.Create(environment, candles, executor),
                "Indicator" => IndicatorContext.Create(environment, candles, parameters,
                    context.TryGetProperty("source", out JsonElement source)
                        ? source.EnumerateArray().Select(e => e.ValueKind == JsonValueKind.Null ? (double?)null : e.GetDouble()).ToArray()
                        : null,
                    executor),
                "Pattern" => PatternContext.Create(environment, candles, executor, parameters),
                "Strategy" => StrategyContext.Create(environment, candles, executor),
                var type => throw new InvalidDataException($"Unknown context type \"{type}\"."),
            };
        }

        private static Candle ReadCandle(JsonElement candle)
        {
            double? Optional(string key) => candle.TryGetProperty(key, out JsonElement v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : null;

            return new Candle(
                candle.GetProperty("open").GetDouble(), candle.GetProperty("high").GetDouble(),
                candle.GetProperty("low").GetDouble(), candle.GetProperty("close").GetDouble(),
                Optional("volume"), Optional("time"));
        }

        // Environment

        private static Env CreateEnvironment(ConformanceCase testCase)
        {
            var environment = Env.Create();

            if (testCase.Standards.Any(s => s.StartsWith("stock-", StringComparison.Ordinal)))
            {
                FormulaRegistry registry = environment.RegisterStock().Registry;

                if (testCase.Registry is JsonElement definitions)
                {
                    AddDefinitions(definitions, "indicators", registry.Indicators);
                    AddDefinitions(definitions, "patterns", registry.Patterns);
                }
            }

            // Deterministic tests: NOW is 0 unless the case gives it
            environment.SetNow(0);

            if (testCase.Environment is not JsonElement changes)
                return environment;

            if (changes.TryGetProperty("constants", out JsonElement constants))
            {
                foreach (JsonProperty constant in constants.EnumerateObject())
                    environment.SetConstant(constant.Name, ToValue(constant.Value));
            }

            if (changes.TryGetProperty("variables", out JsonElement variables))
            {
                foreach (JsonProperty variable in variables.EnumerateObject())
                {
                    // The value may have a wrong type on purpose, to test the validation
                    environment.RemoveProperty(variable.Name);
                    environment.InitVariable(variable.Name, ToValue(variable.Value));
                }
            }

            if (changes.TryGetProperty("omit", out JsonElement omit))
            {
                foreach (JsonElement name in omit.EnumerateArray())
                    environment.RemoveProperty(name.GetString()!);
            }

            return environment;
        }

        private static void AddDefinitions(JsonElement registry, string part, FormulaCollection collection)
        {
            if (!registry.TryGetProperty(part, out JsonElement definitions))
                return;

            foreach (JsonElement definition in definitions.EnumerateArray())
            {
                FormulaParameter[] parameters = definition.TryGetProperty("parameters", out JsonElement p)
                    ? p.EnumerateObject().Select(e => new FormulaParameter(e.Name, ToValue(e.Value))).ToArray()
                    : [];

                collection.Add(definition.GetProperty("id").GetString()!, ReadFormula(definition.GetProperty("formula")), parameters);
            }
        }

        // Values

        /// <summary>
        /// JSON value as a FormuLang value; an object with the <c>$type</c> key has that extended type.
        /// </summary>
        public static IValue ToValue(JsonElement json)
        {
            switch (json.ValueKind)
            {
                case JsonValueKind.Null:
                    return Value.Null("null");
                case JsonValueKind.True:
                case JsonValueKind.False:
                    return Value.Boolean(json.GetBoolean(), "");
                case JsonValueKind.Number:
                    return Value.Number(json.GetDouble(), "");
                case JsonValueKind.String:
                    return Value.Text(json.GetString()!, "");
                case JsonValueKind.Array:
                    return Value.Array(json.EnumerateArray().Select(ToValue).ToArray(), "");
                case JsonValueKind.Object:
                    var obj = new FlObject();
                    string? type = null;

                    foreach (JsonProperty property in json.EnumerateObject())
                    {
                        if (property.Name == "$type")
                            type = property.Value.GetString();
                        else
                            obj.InitVariable(property.Name, ToValue(property.Value));
                    }

                    return type == null ? Value.Object(obj, "") : Value.Object(obj, type, "");
                default:
                    throw new InvalidDataException($"Unsupported JSON value {json}.");
            }
        }

        private static void CompareOutcome(ExecutionResult result, JsonElement expected, string prefix, List<string> problems)
        {
            if (expected.ValueKind == JsonValueKind.Object && expected.TryGetProperty("$error", out JsonElement code))
            {
                if (result.Successful)
                    problems.Add($"{prefix}expected {code.GetString()}, got {result}.");
                else if (result.Error.Name != code.GetString())
                    problems.Add($"{prefix}expected {code.GetString()}, got {result.Error.Name}: {result.Error.Message}");
                return;
            }

            if (!result.Successful)
            {
                problems.Add($"{prefix}expected {expected}, got {result.Error.Name}: {result.Error.Message}");
                return;
            }

            if (!ValueEquals(result.Value, expected))
                problems.Add($"{prefix}expected {expected}, got {result}.");
        }

        /// <summary>
        /// Numbers are equal if they differ by at most <c>1e-9</c> times the larger absolute value (or by <c>1e-12</c>
        /// near 0). Object keys are compared in order. Delegates are not compared.
        /// </summary>
        public static bool ValueEquals(IValue actual, JsonElement expected)
        {
            switch (expected.ValueKind)
            {
                case JsonValueKind.Null:
                    return actual.Type.Base == Types.NullName;

                case JsonValueKind.True:
                case JsonValueKind.False:
                    return actual.Raw is bool b && b == expected.GetBoolean();

                case JsonValueKind.Number:
                    if (actual.Raw is not double number || actual.Type.Base != Types.NumberName)
                        return false;

                    double e = expected.GetDouble();
                    double scale = Math.Max(Math.Abs(number), Math.Abs(e));
                    return Math.Abs(number - e) <= (scale < 1e-3 ? 1e-12 : 1e-9 * scale);

                case JsonValueKind.String:
                    return actual.Raw is string text && actual.Type.Base == Types.TextName && text == expected.GetString();

                case JsonValueKind.Array:
                    if (actual.Raw is not IValue[] elements || actual.Type.Base != Types.ArrayName)
                        return false;

                    JsonElement[] items = expected.EnumerateArray().ToArray();
                    return elements.Length == items.Length && elements.Zip(items).All(pair => ValueEquals(pair.First, pair.Second));

                case JsonValueKind.Object:
                    if (actual.Type.Base == Types.DelegateName)
                        return true;

                    if (actual.Raw is not IObject obj || actual.Type.Base != Types.ObjectName)
                        return false;

                    JsonProperty[] properties = expected.EnumerateObject().Where(p => p.Name != "$type").ToArray();
                    string? type = expected.TryGetProperty("$type", out JsonElement t) ? t.GetString() : null;

                    return actual.Type.Extended == type
                        && obj.Keys.SequenceEqual(properties.Select(p => p.Name))
                        && properties.All(p => ValueEquals(obj.GetProperty(p.Name).GetValue(), p.Value));

                default:
                    return false;
            }
        }

        private static void CompareValidation(IReadOnlyList<IError> errors, JsonElement expected, List<string> problems)
        {
            JsonElement[] items = expected.EnumerateArray().ToArray();

            if (errors.Count != items.Length)
                problems.Add($"Expected {items.Length} validation errors, got {errors.Count}: {string.Join("; ", errors.Select(e => $"{e.Name}: {e.Message}"))}");

            for (int i = 0; i < Math.Min(errors.Count, items.Length); i++)
            {
                string code = items[i].GetProperty("code").GetString()!;

                if (errors[i].Name != code)
                    problems.Add($"Error {i}: expected {code}, got {errors[i].Name}.");

                if (items[i].TryGetProperty("message", out JsonElement message) && message.GetString() != errors[i].Message)
                    problems.Add($"Error {i}: expected \"{message.GetString()}\", got \"{errors[i].Message}\".");
            }
        }
    }
}
