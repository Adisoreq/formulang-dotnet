using FormuLang.NET.Abstractions.Functions;
using FormuLang.NET.Abstractions.Structures;
using FormuLang.NET.Core.Primitives;
using FormuLang.NET.Core.Processing;
using FormuLang.NET.Core.Standards;
using FormuLang.NET.Core.Structures;
using FormuLang.NET.Extensions.Stock.Registry;

namespace FormuLang.NET.Extensions.Stock
{
    /// <summary>
    /// The Stock standard (docs/standards/stock/README.md). Every instance has its own formula registry; register it
    /// with <see cref="StockEnvironmentExtensions.RegisterStock"/>.
    /// </summary>
    public sealed class StockStandard : IStandard
    {
        public const string Identifier = "stock-1.2";

        /// <summary>
        /// Earlier versions that this version is backward compatible with; they are in <c>STANDARDS</c> too.
        /// </summary>
        public static IReadOnlyList<string> CompatibleIdentifiers { get; } = ["stock-1.0", "stock-1.1"];

        /// <summary>
        /// Required Core standard, in the <c>REQUIRE</c> format.
        /// </summary>
        public const string RequiredCore = "core-1.2+";

        public string ID => "stock";

        public IStandardVersion Version { get; } = new StandardVersion { Major = 1, Minor = 2 };

        public string Name => "Stock";

        public IEnumerable<string> ExtendedTypes => [Candle.TypeName];

        public IAbstractTree<string> ExtendedTypeTree { get; }

        public IEnumerable<IFunction> NamedFunctions { get; }

        /// <summary>
        /// Indicator and pattern definitions provided by the host application.
        /// </summary>
        public FormulaRegistry Registry { get; }

        public StockStandard(FormulaRegistry? registry = null)
        {
            Registry = registry ?? new FormulaRegistry();

            var tree = new AbstractTree<string>(Types.ObjectName, Types.ObjectName);
            tree.AddNode(Candle.TypeName, Candle.TypeName, Types.ObjectName);
            ExtendedTypeTree = tree;

            NamedFunctions =
            [
                StockFunctions.Candle,
                StockFunctions.IsBullish,
                StockFunctions.IsBearish,
                StockFunctions.LoadIndicator(Registry),
                StockFunctions.LoadPattern(Registry),
                StockFunctions.LoadCandles,
                StockFunctions.Lag,
                StockFunctions.Window,
                .. SeriesFunctions.All,
                SeriesFunctions.Score,
            ];
        }

        /// <summary>
        /// Checks that the environment supports the required Core standard (<c>core-1.2+</c>).
        /// </summary>
        public static bool IsCoreSupported(IEnvironment environment)
            => environment.Standards.Any(s => CoreProcedures.Satisfies(s, "core", ["1", "2+"]));

        /// <summary>
        /// The check of the <c>stock</c> key of the metadata (docs/standards/stock/README.md, "Metadata"):
        /// <c>category</c> (Text), <c>outputs</c> (an Object of <c>{ name, description }</c>) and <c>checks</c> (an Object
        /// of Texts).
        /// </summary>
        public static MetadataChecker MetadataChecker { get; } = MetadataChecks.Object(new Dictionary<string, MetadataChecker>
        {
            ["category"] = MetadataChecks.Text,
            ["outputs"] = MetadataChecks.ObjectOf(MetadataChecks.Object(new Dictionary<string, MetadataChecker>
            {
                ["name"] = MetadataChecks.Text,
                ["description"] = MetadataChecks.Text,
            })),
            ["checks"] = MetadataChecks.ObjectOf(MetadataChecks.Text),
        });
    }

    public static class StockEnvironmentExtensions
    {
        /// <summary>
        /// Adds the Stock standard: <c>stock-1.0</c>, <c>stock-1.1</c> and <c>stock-1.2</c> in <c>STANDARDS</c>, the
        /// <c>Stock.Candle</c> type, the functions of the standard, the formula registry, and the check of the
        /// <c>stock</c> key of the metadata.
        /// </summary>
        /// <exception cref="InvalidOperationException">
        /// The environment does not support <c>core-1.2+</c>, or the standard is already registered.
        /// </exception>
        public static StockStandard RegisterStock(this Core.Structures.Environment environment, FormulaRegistry? registry = null)
        {
            if (!StockStandard.IsCoreSupported(environment))
                throw new InvalidOperationException($"The Stock standard requires {StockStandard.RequiredCore}.");

            var standard = new StockStandard(registry);
            foreach (string compatible in StockStandard.CompatibleIdentifiers)
                environment.RegisterStandard(compatible);
            environment.RegisterStandard(standard);
            environment.RegisterMetadataChecker(standard.ID, StockStandard.MetadataChecker);
            return standard;
        }

        /// <summary>
        /// Formula registry of the environment, or <c>null</c> if it does not support the Stock standard.
        /// </summary>
        public static FormulaRegistry? GetFormulaRegistry(this IEnvironment environment)
            => (environment.GetFunction(StockFunctions.LoadIndicatorName) as LoadIndicatorFunction)?.Registry;
    }
}
