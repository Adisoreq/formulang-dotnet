using FormuLang.NET.Abstractions.Processing;
using FormuLang.NET.Abstractions.Structures;
using FormuLang.NET.Core.Model;
using FormuLang.NET.Core.Primitives;
using FormuLang.NET.Core.Processing;
using FormuLang.NET.Core.Structures;
using FormuLang.NET.Core.Utils;
using Env = FormuLang.NET.Core.Structures.Environment;

namespace FormuLang.NET.Core.Tests
{
    /// <summary>
    /// Core 1.2: the examples of the documents of the standard that the JSON conformance tests do not cover, and the API
    /// of the library for the memory, the prepared formulas, the resumed iterations, the metadata and the warnings.
    /// </summary>
    public class Core12Tests
    {
        private static string[] Iterate(string formula, int count, bool stopOnError = false)
        {
            IterationContext context = IterationContext.Create(Env.Create(), count);
            return Executor.Create().ExecuteIterations(formula, context, stopOnError).Results.Select(r => r.ToString()).ToArray();
        }

        private static PreparedFormula Prepare(string formula, IContext? context = null)
        {
            PreparedFormula prepared = Executor.Create().Prepare(formula, context ?? Context.Create(Env.Create()));
            Assert.True(prepared.Successful, prepared.Failure?.ToString());
            return prepared;
        }

        private static string Run(string formula) => Executor.Create().Execute(formula, Env.Create()).ToString();

        // Memory (docs/core/context.md, "Memory")

        [Fact]
        public void Memory_KeepsACountBetweenIterations()
        {
            IterationContext context = IterationContext.Create(Env.Create(), 4);
            context.SetConstant("SOURCE", Value.Array(new double[] { 5, 20, 30, 1 }.Select(n => Value.Number(n)), "SOURCE"));

            var run = Executor.Create().ExecuteIterations(
                "$count = RECALL(\"count\", 0) + IF(SOURCE[I] > 10, 1, 0)\nSTORE(\"count\", $count)\n$count", context);

            Assert.Equal(["0", "1", "2", "2"], run.Results.Select(r => r.ToString()));
        }

        [Fact]
        public void Memory_GivesThePreviousValueOfASeries()
            => Assert.Equal(["null", "0", "2"], Iterate("$prev = RECALL(\"close\")\nSTORE(\"close\", I * 2)\n$prev", 3));

        [Fact]
        public void Memory_DiscardsTheWritesOfABranchThatIsNotSelected_AlsoInANestedIf()
            => Assert.Equal(["null", "null", "1", "100"],
                Iterate("IF(I > 0, IF(I == 1, STORE(\"x\", I), STORE(\"x\", 100)), 0)\nRECALL(\"x\")", 4));

        [Fact]
        public void Memory_DiscardsTheWritesOfTheArgumentsOfAndAfterTheDecidingOne()
            => Assert.Equal(["[null, null]", "[null, null]", "[1, 1]"],
                Iterate("$t = AND(I == 1, ISERR(STORE(\"x\", I)), ISERR(STORE(\"y\", I)))\n[RECALL(\"x\"), RECALL(\"y\")]", 3));

        [Fact]
        public void Memory_DiscardsTheWritesOfAnIfWhoseConditionIsNotABoolean()
            => Assert.Equal(["[null, null]", "[null, null]"],
                Iterate("$t = IFERR(IF(1, STORE(\"x\", I), STORE(\"y\", I)), 0)\n[RECALL(\"x\"), RECALL(\"y\")]", 2));

        [Fact]
        public void Memory_KeepsTheWritesOfALambdaOnTheSelectedPath()
            => Assert.Equal(["[null, null]", "[0, null]", "[1, null]"],
                Iterate("$r = MAP([1, 2], (e) => IF(e == 2, STORE(\"x\", I), 0))\n$s = IF(false, MAP([1], (e) => STORE(\"y\", 1)), 0)\n[RECALL(\"x\"), RECALL(\"y\")]", 3));

        [Fact]
        public void Memory_OfATimeContext_IsVisibleFromTheNextFrame()
        {
            TimeContext context = TimeContext.Create(Env.Create());
            var run = Executor.Create().ExecuteFrames("STORE(\"t\", TIME_S)\nRECALL(\"t\", -1)", context, [(0, 0, null), (1, 1, null)]);

            Assert.Equal(["-1", "0"], run.Results.Select(r => r.ToString()));
            Assert.Equal("1", context.Memory.Recall("t")?.ToString());
        }

        [Fact]
        public void Memory_IsEmptyAgainWhenTheIterationsStartAgain()
        {
            IterationContext context = IterationContext.Create(Env.Create(), 2);
            var executor = Executor.Create();
            const string formula = "$c = RECALL(\"c\", 0) + 1\nSTORE(\"c\", $c)\n$c";

            executor.ExecuteIterations(formula, context);
            Assert.Equal(["1", "2"], executor.ExecuteIterations(formula, context).Results.Select(r => r.ToString()));
        }

        [Fact]
        public void Memory_CanBeReadAndRestoredByTheHostApplication()
        {
            IterationContext context = IterationContext.Create(Env.Create(), 1);
            context.Memory.Load([new("x", Value.Number(7))]);
            context.BeginIteration(0, null);
            Assert.Equal(0, context.Memory.Count); // the first iteration starts with an empty memory

            context.Memory.Load([new("x", Value.Number(7))]);
            Assert.Equal("7", Executor.Create().Execute("RECALL(\"x\")", context).ToString());
            Assert.Equal(["x=7"], context.Memory.Entries().Select(e => $"{e.Key}={StringConverter.ValueToString(e.Value)}"));
        }

        [Fact]
        public void Memory_DoesNotChangeTheResultOfIfWithASkippedBranch()
            => Assert.Equal(["null", "null", "1"], Iterate("$v = IF(I == 0, 1, STORE(\"x\", I))\nRECALL(\"x\")", 3));

        [Fact]
        public void Memory_WritesAreNotAppliedInTheDefaultContext()
            => Assert.StartsWith("[OPERATION_ERROR (", Run("STORE(\"x\", 1)"));

        // AND and OR (docs/standards/core/functions/AND.md, "Short circuit")

        [Theory]
        [InlineData("AND(true, 1, false)", "false")]
        [InlineData("OR(false, 1, true)", "true")]
        [InlineData("AND(true, ERROR(\"x\"), false)", "[CUSTOM_ERROR (1)] x")]
        [InlineData("AND(false)", "ARGUMENT_ERROR")]
        [InlineData("OR(true)", "ARGUMENT_ERROR")]
        [InlineData("true && true && true", "true")]
        [InlineData("false || false || true", "true")]
        [InlineData("OR(false, false)", "false")]
        [InlineData("AND(true, true, true, true, true)", "true")]
        [InlineData("false && ERROR(\"x\")", "false")]
        [InlineData("true || 1 / 0", "true")]
        public void AndOr_ShortCircuit(string formula, string expected)
        {
            string actual = Run(formula);

            if (expected.EndsWith("_ERROR"))
                Assert.StartsWith($"[{expected} (", actual);
            else
                Assert.Equal(expected, actual);
        }

        // HISTORY and resumed iterations (docs/core/context.md)

        [Fact]
        public void History_IsNullForAnIterationThatResultedInAnError()
            => Assert.Equal(["[]", "[CUSTOM_ERROR (1)] e", "[[], null]"], Iterate("IF(I == 1, ERROR(\"e\"), HISTORY)", 3));

        [Fact]
        public void History_GivesAtMostThreeBuySignals()
        {
            IterationContext context = IterationContext.Create(Env.Create(), 6);
            context.SetConstant("BUY", Value.Number(1));
            context.SetConstant("NEUTRAL", Value.Number(0));

            var run = Executor.Create().ExecuteIterations("$buys = LEN(FILTER(HISTORY, (r) => r == BUY))\nIF($buys < 3, BUY, NEUTRAL)", context);
            Assert.Equal(["1", "1", "1", "0", "0", "0"], run.Results.Select(r => r.ToString()));
        }

        [Fact]
        public void ResumedIterations_GiveTheSameResultsAsAllTheIterations()
        {
            const string formula = "$sum = RECALL(\"sum\", 0) + I\nSTORE(\"sum\", $sum)\n[$sum, LEN(HISTORY), IFNULL(PREV, -1)]";
            var executor = Executor.Create();
            var all = executor.ExecuteIterations(formula, IterationContext.Create(Env.Create(), 5));

            IterationContext context = IterationContext.Create(Env.Create(), 3);
            PreparedFormula prepared = Prepare(formula, context);
            var first = executor.ExecuteIterations(prepared, context);
            Assert.Equal(3, context.CompletedIterations);

            context.Extend(5);
            var rest = executor.ResumeIterations(prepared, context);

            Assert.Equal(all.Results.Select(r => r.ToString()), first.Results.Concat(rest.Results).Select(r => r.ToString()));
            Assert.Empty(executor.ResumeIterations(prepared, context).Results);
        }

        [Fact]
        public void ResumedIterations_ContinueFromASavedState()
        {
            const string formula = "$sum = RECALL(\"sum\", 0) + I\nSTORE(\"sum\", $sum)\n[$sum, LEN(HISTORY), IFNULL(PREV, -1)]";
            var executor = Executor.Create();
            var all = executor.ExecuteIterations(formula, IterationContext.Create(Env.Create(), 5));

            IterationContext first = IterationContext.Create(Env.Create(), 3);
            executor.ExecuteIterations(formula, first);
            IterationState state = first.SaveState();

            IterationContext second = IterationContext.Create(Env.Create(), 5);
            second.RestoreState(state);
            var rest = executor.ResumeIterations(formula, second);

            Assert.Equal(all.Results.Skip(3).Select(r => r.ToString()), rest.Results.Select(r => r.ToString()));
        }

        [Fact]
        public void IterationContext_DoesNotShrink()
            => Assert.Throws<ArgumentOutOfRangeException>(() => IterationContext.Create(Env.Create(), 3).Extend(2));

        // Prepared formulas (docs/semantics/execution.md, "Execution modes")

        [Fact]
        public void PreparedFormula_GivesTheSameResultInAHotExecution()
        {
            Env environment = Env.Create();
            var executor = Executor.Create();
            PreparedFormula prepared = Prepare("$a = 2\n$a * PI", Context.Create(environment));
            string cold = executor.Execute("$a = 2\n$a * PI", environment).ToString();

            Assert.Equal(cold, executor.Evaluate(prepared, Context.Create(environment)).ToString());
            Assert.Equal(cold, executor.Evaluate(prepared, Context.Create(environment)).ToString());
        }

        [Fact]
        public void PreparedFormula_IsPreparedAgainForAnotherEnvironment()
            => Assert.Equal("2", Executor.Create().Evaluate(Prepare("1 + 1"), Context.Create(Env.Create())).ToString());

        [Fact]
        public void PreparedFormula_ChecksTheEnvironmentBeforeTheFirstHotExecution()
        {
            PreparedFormula prepared = Prepare("1 + 1");
            Env broken = Env.Create();
            broken.RemoveProperty("PI");

            ExecutionResult result = Executor.Create().Evaluate(prepared, Context.Create(broken));
            Assert.False(result.Successful);
            Assert.Equal("ENVIRONMENT_ERROR", result.Error.Name);
        }

        [Fact]
        public void PreparedFormula_OfAFormulaThatDoesNotParse_GivesItsError()
        {
            PreparedFormula prepared = Executor.Create().Prepare("1 +", Context.Create(Env.Create()));

            Assert.False(prepared.Successful);
            Assert.Equal("SYNTAX_ERROR", Executor.Create().Evaluate(prepared, Context.Create(Env.Create())).Error.Name);
        }

        // Metadata (docs/core/metadata.md)

        private const string Block = "<@ {\n  id: \"sma-cross\",\n  parameters: { fast: { type: \"Number\", default: 10, min: 1 } }\n} @>\n";
        private const string BlockObject = "{ id: \"sma-cross\", parameters: { fast: { type: \"Number\", default: 10, min: 1 } } }";

        [Fact]
        public void Metadata_IsReadWithoutAnEnvironment()
        {
            MetadataResult reading = MetadataReader.Read($"{Block}PARAMETERS.fast");

            Assert.True(reading.Successful);
            Assert.Equal(BlockObject, StringConverter.ValueToString(reading.Metadata));
            // The block is replaced by white space, so the rest of the formula keeps its positions
            Assert.Equal("PARAMETERS.fast", reading.Formula.Split('\n')[4]);
        }

        [Fact]
        public void Metadata_IsAPartOfThePreparedFormula()
            => Assert.Equal(BlockObject, StringConverter.ValueToString(Prepare($"{Block}1").Metadata));

        [Fact]
        public void Metadata_DoesNotChangeTheResult()
            => Assert.Equal("4", Run($"{Block}$x = 2 # c\n$x ^ 2"));

        [Theory]
        [InlineData("<@ { a: 1 }", "The metadata block is not closed (line 1, column 1).")]
        [InlineData("# c\n  <@ [1] @>\n1", "The metadata block (line 2, column 3) must contain one Object literal.")]
        [InlineData("<@ { a: $x } @>\n1", "The metadata block (line 1, column 1) may contain only constant values: Number, Text and Boolean literals, null, and Arrays and Objects of them.")]
        public void Metadata_ThatIsNotValid_IsASyntaxError(string formula, string message)
        {
            ExecutionResult result = Executor.Create().Execute(formula, Env.Create());

            Assert.Equal("SYNTAX_ERROR", result.Error.Name);
            Assert.Equal(message, result.Error.Message);
        }

        [Fact]
        public void Metadata_ReadByTheExecutor_IsValidatedFirst()
        {
            MetadataResult reading = Executor.Create().ReadMetadata("<@ { id: \"a\" } @>\n$ł = 1", Env.Create());

            Assert.False(reading.Successful);
            Assert.Equal("SYNTAX_ERROR", reading.Error.Name);
        }

        // Warnings (docs/standards/core/semantics/warnings.md)

        [Fact]
        public void Warnings_HaveThePositionOfTheSequenceAndTheMessageOfTheStandard()
        {
            IReadOnlyList<IWarning> warnings = Prepare("$a = 1\n\n# comment\n  $a + 1; $a\n$x = $a\n- $a\n$x").Warnings;

            Assert.Equal(
            [
                (WarningCode.UnusedValue, 4, 3, "The value of the sequence at line 4 is not used."),
                (WarningCode.UnusedValue, 4, 11, "The value of the sequence at line 4 is not used."),
                (WarningCode.UnusedValue, 6, 1, "The value of the sequence at line 6 is not used."),
                (WarningCode.ContinuedLine, 6, 1, "The sequence at line 6 starts with \"-\": a new line ends the previous sequence. Put the operator at the end of the previous line, or the expression in parentheses."),
            ], warnings.Select(w => (w.Code, w.Line, w.Column, w.Message)));
        }

        [Fact]
        public void Warnings_ArePositionedInTheOriginalTextAfterAScopedCommentAndAMetadataBlock()
        {
            IReadOnlyList<IWarning> warnings = Prepare("<@ {\n  id: 1\n} @> <# a\nb #> $a = 1; $a\n$a").Warnings;

            Assert.Equal(["METADATA_TYPE 1:1", "UNUSED_VALUE 4:14"], warnings.Select(w => $"{w.Code} {w.Line}:{w.Column}"));
            Assert.Equal("Metadata key \"id\" must be of type Text.", warnings[0].Message);
        }

        [Fact]
        public void Warnings_CheckTheNestedKeysOfTheMetadata()
        {
            IReadOnlyList<IWarning> warnings = Prepare("<@ { parameters: { p: { min: \"1\" }, q: 2 }, result: [1], x: { anything: 1 } } @>\n1").Warnings;

            Assert.Equal(
            [
                "Metadata key \"parameters.p.min\" must be of type Number.",
                "Metadata key \"parameters.q\" must be of type Object.",
                "Metadata key \"result\" must be of type Text or Array of Text.",
            ], warnings.Select(w => w.Message));
        }

        [Fact]
        public void Warnings_CompareTheContextOfTheMetadataWithTheContextTypeAndItsBaseTypes()
        {
            IterationContext iteration = IterationContext.Create(Env.Create(), 1);

            Assert.Empty(Prepare("<@ { context: \"Default\" } @>\n1", iteration).Warnings);
            Assert.Equal(["The formula is written for the context \"Time\", but is executed in \"Iteration\"."],
                Prepare("<@ { context: \"Time\" } @>\n1", iteration).Warnings.Select(w => w.Message));
        }

        [Fact]
        public void Warnings_AreNotReportedForANewLineInsideParenthesesOrAfterASemicolon()
        {
            Assert.Empty(Prepare("$a = 1\n$x = ($a\n- 1)\n$x").Warnings);
            Assert.Empty(Prepare("$a = 1;\n- $a").Warnings);
        }

        [Fact]
        public void Warnings_AreAPartOfTheResultOfAColdExecution()
            => Assert.Equal([WarningCode.UnusedValue], Executor.Create().Execute("1\n2", Env.Create()).Warnings.Select(w => w.Code));

        [Fact]
        public void Warnings_ALineThatStartsWithABinaryOperatorIsASyntaxErrorWithTheMessageOfTheStandard()
        {
            ExecutionResult result = Executor.Create().Execute("$a = true\n\n$b = $a\n  && true", Env.Create());

            Assert.Equal("Line 4 starts with \"&&\", which has no left operand: a new line ends the previous sequence. Put \"&&\" at the end of the previous line, or the expression in parentheses.",
                result.Error.Message);
        }

        // Parameter specifications (docs/implementation/formula_models.md, "Parameter specification")

        [Fact]
        public void Parameters_AreReadFromTheMetadataAndNormalized()
        {
            MetadataResult reading = MetadataReader.Read("<@ { parameters: { period: { type: \"Number\", default: 14, min: 1, name: \"Period\" }, mode: { options: [\"a\", \"b\"] } } } @>\n1");
            IReadOnlyList<ParameterSpec> specs = ParameterSpec.FromMetadata(reading.Metadata);

            Assert.Equal(["period", "mode"], specs.Select(s => s.Key));
            Assert.Equal("Period", specs[0].Name);
            Assert.Equal("mode", specs[1].Name);

            ParameterNormalization ok = Parameters.Normalize(specs, [new("mode", Value.Text("b", "\"b\""))]);
            Assert.True(ok.Successful);
            Assert.Equal("{ period: 14, mode: \"b\" }", Parameters.Key(ok.Parameters!));

            ParameterNormalization bad = Parameters.Normalize(specs, [new("period", Value.Number(0)), new("mode", Value.Text("c", "\"c\"")), new("z", Value.Number(1))]);
            Assert.Equal(["UNKNOWN z", "MIN period", "OPTION mode"], bad.Problems.Select(p => $"{p.CodeName} {p.Key}"));
            Assert.Equal("The parameter \"mode\" must be one of: a, b.", bad.Problems[2].Message);
            Assert.Null(bad.Parameters);
        }

        [Fact]
        public void ValueConversion_ConvertsDotNetAndJsonValues()
        {
            Assert.Equal("[1, \"a\", true, null, { k: 2 }]",
                StringConverter.ValueToString(ValueConversion.FromClr(new object?[] { 1, "a", true, null, new Dictionary<string, object?> { ["k"] = 2.0 } })));

            using var json = System.Text.Json.JsonDocument.Parse("""{ "a": [1, 2], "b": "x" }""");
            Assert.True(ValueConversion.TryFromJson(json.RootElement, out var value));
            Assert.Equal("{ a: [1, 2], b: \"x\" }", StringConverter.ValueToString(value));
            Assert.Equal("{\"a\":[1,2],\"b\":\"x\"}", ValueConversion.ToJson(value).GetRawText());
            Assert.False(ValueConversion.TryFromClr(new Action(() => { }), out _));
        }
    }
}
