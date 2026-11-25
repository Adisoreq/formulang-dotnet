using FormuLang.NET.Abstractions.Primitives;
using FormuLang.NET.Core.Functions;
using FormuLang.NET.Core.Primitives;
using FormuLang.NET.Core.Processing;
using FormuLang.NET.Core.Structures;
using static FormuLang.NET.Core.Tests.FormulaRunner;
using Env = FormuLang.NET.Core.Structures.Environment;

namespace FormuLang.NET.Core.Tests
{
    /// <summary>
    /// The optimizations of the evaluator and the Iteration Context give the same results as the plain algorithm:
    /// skipping the branch of IF that is not selected, and reusing the containers between iterations.
    /// </summary>
    public class PerformanceSemanticsTests
    {
        private static (Env Env, Func<int> Calls) EnvironmentWithCounter(bool sideEffectFree)
        {
            var env = Env.Create();
            int calls = 0;
            env.RegisterFunction(new Function("COUNT",
                [new FunctionOverload([], (_, _) => { calls++; return Value.Number(calls, "COUNT()"); })])
            {
                SideEffectFree = sideEffectFree,
            });
            return (env, () => calls);
        }

        private static string RunIn(Env env, string formula) => Executor.Create().Execute(formula, env).Value.ToString()!;

        [Fact]
        public void SideEffectFreeBranch_ThatIsNotSelected_IsNotComputed()
        {
            var (env, calls) = EnvironmentWithCounter(sideEffectFree: true);

            Assert.Equal("1", RunIn(env, "IF(TRUE, 1, COUNT())"));
            Assert.Equal(0, calls());

            Assert.Equal("1", RunIn(env, "IF(FALSE, 0, COUNT())"));
            Assert.Equal(1, calls());
        }

        [Fact]
        public void FunctionWithSideEffects_InTheBranchThatIsNotSelected_IsStillCalled()
        {
            var (env, calls) = EnvironmentWithCounter(sideEffectFree: false);

            Assert.Equal("1", RunIn(env, "IF(TRUE, 1, COUNT())"));
            Assert.Equal(1, calls());
        }

        [Fact]
        public void DelegateFromVariable_InTheBranchThatIsNotSelected_IsStillInvoked()
        {
            var (env, calls) = EnvironmentWithCounter(sideEffectFree: false);

            Assert.Equal("0", RunIn(env, "$f = (x) => COUNT()\nIF(TRUE, 0, MAP([1, 2], $f))"));
            Assert.Equal(2, calls());
        }

        [Fact]
        public void AssignmentInTheBranchThatIsNotSelected_IsStillExecuted()
        {
            Assert.Equal("5", Run("$a = 1\n$b = IF(TRUE, 0, $a = 5)\n$a"));
        }

        [Theory]
        [InlineData("IF(TRUE, 1, DIV(1, 0))", "1")]
        [InlineData("IF(FALSE, ERROR(\"a\"), 2)", "2")]
        [InlineData("IF(1, ERROR(\"a\"), 2)", "[CUSTOM_ERROR (1)] a")]
        [InlineData("IF(ERROR(\"c\"), ERROR(\"a\"), 2)", "[CUSTOM_ERROR (1)] c")]
        [InlineData("$x = IF(2 > 1, IF(FALSE, 1, 2), 3)\n$x * 10", "20")]
        [InlineData("MAP([1, 2, 3], (v) => IF(v > 1, v * 10, ERROR(\"small\")))", "[CUSTOM_ERROR (1)] small")]
        [InlineData("MAP([2, 3], (v) => IF(v > 1, v * 10, ERROR(\"small\")))", "[20, 30]")]
        public void Results_AreTheSameAsWithBothBranchesComputed(string formula, string expected)
        {
            Assert.Equal(expected, Run(formula));
        }

        [Fact]
        public void EveryIteration_StartsWithTheEnvironmentVariablesOfTheEnvironment()
        {
            var env = Env.Create();
            var context = IterationContext.Create(env, 3);

            var run = Executor.Create().ExecuteIterations("$i = I\n@array_indexing = 1\n$i", context);

            Assert.Equal(["0", "1", "2"], run.Values.Select(v => v.ToString()));
            Assert.Equal("0", env.GetProperty("array_indexing").GetValue().ToString());
        }

        [Fact]
        public void EveryIteration_StartsWithoutContextVariables_AndWithThePreviousResult()
        {
            var context = IterationContext.Create(Env.Create(), 4);

            var run = Executor.Create().ExecuteIterations("$seen := 0\n$seen = $seen + 1\nIF(ISNULL(PREV), $seen, PREV + $seen)", context);

            Assert.Equal(["1", "2", "3", "4"], run.Values.Select(v => v.ToString()));
        }

        [Fact]
        public void PreviousResult_IsReadThroughTheContext()
        {
            var context = IterationContext.Create(Env.Create(), 3);
            var run = Executor.Create().ExecuteIterations("IF(ISNULL(PREV), 10, PREV * 2)", context);

            Assert.Equal(["10", "20", "40"], run.Values.Select(v => v.ToString()));
            Assert.Equal("20", context.Previous.ToString());
            Assert.Equal("20", context.GetProperty(IterationContext.PreviousConstant).GetValue().ToString());
        }
    }
}
