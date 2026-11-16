using FormuLang.NET.Core.Validation;

namespace WebPreview.Model
{
    // Indicators, patterns and strategies edited in the preview: mutable copies of the example configurations

    public sealed class IndicatorParameterDraft
    {
        /// <summary>Key in <c>PARAMETERS</c>.</summary>
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        /// <summary>Default value of the definition in the registry (used by <c>INDICATOR(id)</c> in other formulas).</summary>
        public double DefaultValue { get; set; }
        /// <summary>Value set in the preview, used for the chart of the indicator.</summary>
        public double Value { get; set; }
    }

    public sealed class SeriesDraft
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public string Description { get; set; } = "";
        public string LineColor { get; set; } = "";
        public string FillColor { get; set; } = "";
        public FillMode FillMode { get; set; }
    }

    public sealed class IndicatorDraft
    {
        /// <summary>Key of the indicator in the preview (not editable).</summary>
        public required string Id { get; init; }
        /// <summary>Identifier in the registry: <c>INDICATOR("SMA")</c>.</summary>
        public string IndicatorId { get; set; } = "";
        public string Name { get; set; } = "";
        public string Description { get; set; } = "";
        public List<IndicatorParameterDraft> Parameters { get; set; } = [];
        public List<SeriesDraft> Series { get; set; } = [];
        public SeriesMode SeriesMode { get; set; }
        public IndicatorType Type { get; set; }

        public static IndicatorDraft Of(IndicatorConfig config) => new()
        {
            Id = config.Id,
            IndicatorId = config.IndicatorId,
            Name = config.Name,
            Description = config.Description,
            Parameters = config.Parameters.Select(p => new IndicatorParameterDraft
            {
                Id = p.Id, Name = p.Name, DefaultValue = p.DefaultValue, Value = p.DefaultValue,
            }).ToList(),
            Series = config.Series.Select(s => new SeriesDraft
            {
                Id = s.Id, Name = s.Name, Description = s.Description,
                LineColor = s.LineColor, FillColor = s.FillColor, FillMode = s.FillMode,
            }).ToList(),
            SeriesMode = config.SeriesMode,
            Type = config.Type,
        };

        private static readonly string[] SeriesColors = ["#e9c46a", "#4be39a", "#7aa2f7", "#ff8f70", "#c9a7ff", "#8ecae6"];

        /// <summary>A new series with a free id and the next color.</summary>
        public SeriesDraft NewSeries()
        {
            string id = FreeId("series", Series.Select(s => s.Id).ToList());
            string color = SeriesColors[Series.Count % SeriesColors.Length];
            return new SeriesDraft { Id = id, Name = id, Description = "", LineColor = color, FillColor = "transparent", FillMode = FillMode.Line };
        }

        /// <summary>A new parameter with a free id.</summary>
        public IndicatorParameterDraft NewParameter()
        {
            string id = FreeId("param", Parameters.Select(p => p.Id).ToList());
            return new IndicatorParameterDraft { Id = id, Name = id, DefaultValue = 1, Value = 1 };
        }

        private static string FreeId(string prefix, IReadOnlyList<string> used)
        {
            for (int i = used.Count + 1; ; i++)
            {
                if (!used.Contains($"{prefix}{i}"))
                    return $"{prefix}{i}";
            }
        }

        private static IEnumerable<string> Duplicates(IEnumerable<string> values)
            => values.GroupBy(v => v, StringComparer.Ordinal).Where(g => g.Count() > 1).Select(g => g.Key);

        /// <summary>
        /// Problems of the definition of an indicator. An indicator with problems is not in the registry, so
        /// <c>INDICATOR(id)</c> returns <c>INVALID_KEY_ERROR</c> for it.
        /// </summary>
        public List<string> Problems(IEnumerable<IndicatorDraft> all)
        {
            var problems = new List<string>();
            void Identifier(string value, string what)
            {
                if (!IdentifierValidator.Validate(value))
                    problems.Add($"{what} \"{value}\" is not a valid identifier (^[a-zA-Z_]\\w*$).");
            }

            Identifier(IndicatorId, "Registry id");
            if (all.Any(other => other != this && other.IndicatorId == IndicatorId))
                problems.Add($"Registry id \"{IndicatorId}\" is used by another indicator.");

            foreach (IndicatorParameterDraft p in Parameters)
            {
                Identifier(p.Id, "Parameter id");
                if (!double.IsFinite(p.DefaultValue))
                    problems.Add($"Parameter \"{p.Id}\" has no valid default value.");
            }

            foreach (string id in Duplicates(Parameters.Select(p => p.Id)))
                problems.Add($"Parameter id \"{id}\" is defined more than once.");

            if (Series.Count == 0)
                problems.Add("The indicator has no series to draw.");

            foreach (SeriesDraft s in Series)
                Identifier(s.Id, "Series id");

            foreach (string id in Duplicates(Series.Select(s => s.Id)))
                problems.Add($"Series id \"{id}\" is defined more than once.");

            return problems;
        }
    }

    public sealed class PatternDraft
    {
        public const int MaxLength = 20;

        /// <summary>Key of the pattern in the preview (not editable).</summary>
        public required string Id { get; init; }
        public string Name { get; set; } = "";
        public string Description { get; set; } = "";
        public PatternType Type { get; set; }
        /// <summary>Number of candles of the pattern, ending with the candle where it is detected.</summary>
        public double Length { get; set; }
        // Appearance
        public PatternColor Color { get; set; }
        /// <summary>Writes the label (the initials of the name) above every rectangle.</summary>
        public bool ShowLabel { get; set; } = true;

        public static PatternDraft Of(PatternConfig config) => new()
        {
            Id = config.Id, Name = config.Name, Description = config.Description,
            Type = config.Type, Length = config.Length, Color = config.Color, ShowLabel = true,
        };

        public List<string> Problems()
        {
            var problems = new List<string>();
            if (Name.Trim().Length == 0)
                problems.Add("The pattern has no name.");
            if (Length != Math.Floor(Length) || Length < 1 || Length > MaxLength)
                problems.Add($"The number of candles must be a whole number from 1 to {MaxLength}.");
            return problems;
        }
    }

    public sealed class StrategyRuntime
    {
        /// <summary>The strategy takes part in the final proposal.</summary>
        public bool Active { get; set; }
        /// <summary>Confidence used by the proposal; the default is the one of the definition.</summary>
        public double Confidence { get; set; }
        /// <summary>Lifetime used by the proposal; the default is the one of the definition.</summary>
        public double Lifetime { get; set; }
    }

    public sealed class StrategyDraft
    {
        /// <summary>Key of the strategy in the preview (not editable).</summary>
        public required string Id { get; init; }
        public string Name { get; set; } = "";
        public string Description { get; set; } = "";
        /// <summary>Default confidence of the definition.</summary>
        public double Confidence { get; set; }
        /// <summary>Default lifetime of the definition (whole number of candles).</summary>
        public double Lifetime { get; set; }
        // Appearance
        /// <summary>Text of the markers of the strategy on the chart.</summary>
        public string Label { get; set; } = "";
        // Values of the preview
        public StrategyRuntime Runtime { get; set; } = new();

        public static StrategyDraft Of(StrategyConfig config, bool active = true) => new()
        {
            Id = config.Id,
            Name = config.Name,
            Description = config.Description,
            Confidence = config.Confidence,
            Lifetime = config.Lifetime,
            Label = Initials(config.Name),
            Runtime = new StrategyRuntime { Active = active, Confidence = config.Confidence, Lifetime = config.Lifetime },
        };

        public List<string> Problems()
        {
            var problems = new List<string>();
            if (Name.Trim().Length == 0)
                problems.Add("The strategy has no name.");
            if (!double.IsFinite(Confidence) || Confidence < 0)
                problems.Add("The confidence must be a number ≥ 0.");
            if (Lifetime != Math.Floor(Lifetime) || Lifetime < 1)
                problems.Add("The lifetime must be a whole number of candles ≥ 1.");
            return problems;
        }

        public static string Initials(string name)
            => string.Concat(name.Split((char[]?)null).Select(word => word.Length > 0 ? char.ToUpperInvariant(word[0]).ToString() : ""));
    }
}
