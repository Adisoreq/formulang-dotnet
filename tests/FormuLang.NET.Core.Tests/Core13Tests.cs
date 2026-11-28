using FormuLang.NET.Core.Functions;
using FormuLang.NET.Core.Primitives;
using FormuLang.NET.Core.Standards;
using FormuLang.NET.Core.Utils;
using static FormuLang.NET.Core.Tests.FormulaRunner;
using Env = FormuLang.NET.Core.Structures.Environment;

namespace FormuLang.NET.Core.Tests
{
    /// <summary>
    /// Core 1.3: the examples of the documents of the standard that the JSON conformance tests
    /// (<c>conformance/core/functions/core13.json</c>) do not cover. Errors are compared by name only.
    /// </summary>
    public class Core13Tests
    {
        private static void AssertResult(string formula, string expected)
        {
            string actual = Run(formula);

            if (expected.EndsWith("_ERROR"))
                Assert.StartsWith($"[{expected} (", actual);
            else
                Assert.Equal(expected, actual);
        }

        // Missing values (docs/standards/core/README.md, "Missing values")

        [Theory]
        [InlineData("null < null", "false")] [InlineData("null <= null", "false")] [InlineData("1 > null", "false")] [InlineData("null >= 1", "false")]
        [InlineData("null == 1", "false")]
        [InlineData("null - null", "null")] [InlineData("null * null", "null")] [InlineData("1 / null", "null")] [InlineData("null / null", "null")]
        [InlineData("ABS(-null)", "null")]
        [InlineData("null + \"a\"", "ARGUMENT_ERROR")] [InlineData("null < \"a\"", "ARGUMENT_ERROR")] [InlineData("true + null", "ARGUMENT_ERROR")]
        [InlineData("MOD(null, 2)", "ARGUMENT_ERROR")] [InlineData("POW(null, 2)", "ARGUMENT_ERROR")]
        [InlineData("null || false", "ARGUMENT_ERROR")] [InlineData("!null", "ARGUMENT_ERROR")]
        // The short circuit of core-1.2 does not change: the deciding argument after null decides
        [InlineData("null || true", "true")] [InlineData("null && false", "false")]
        [InlineData("IFERR(1 + null, 0)", "null")] [InlineData("ISERR(null * 2 > 0)", "false")]
        public void MissingValues(string formula, string expected) => AssertResult(formula, expected);

        // MIN.md, MAX.md, SUM.md, AVG.md

        [Theory]
        [InlineData("MIN([4, null, 9])", "null")] [InlineData("MAX([4, null, 9])", "null")] [InlineData("MAX([4, 5, 7, 9])", "9")] [InlineData("MIN([null])", "null")]
        [InlineData("SUM([])", "0")] [InlineData("SUM([null])", "null")] [InlineData("SUM([1, 2, 3])", "6")]
        [InlineData("AVG([null])", "null")] [InlineData("AVG([1, 2, 3])", "2")]
        [InlineData("MAX([4, \"5\"])", "ARGUMENT_ERROR")] [InlineData("SUM([1, \"a\", null])", "ARGUMENT_ERROR")] [InlineData("SUM([1, ERROR(\"x\")])", "[CUSTOM_ERROR (1)] x")]
        // Two numbers or many numbers do not accept null
        [InlineData("MIN(1, null)", "ARGUMENT_ERROR")] [InlineData("MAX(1, 2, null)", "ARGUMENT_ERROR")]
        // PROD, MEDIAN and the other aggregates do not change
        [InlineData("PROD([1, null])", "ARGUMENT_ERROR")] [InlineData("MEDIAN([1, null])", "ARGUMENT_ERROR")] [InlineData("MEDIAN([])", "OPERATION_ERROR")]
        // With a handler
        [InlineData("MIN([], (e) => e)", "null")] [InlineData("MAX([], (e) => e)", "null")]
        [InlineData("MAX([1, 2], (e) => IF(e == 1, null, e))", "null")] [InlineData("MIN([3, 1, 2], (e) => e * 2)", "2")]
        // Every element is computed: an Error or a wrong result after a null result is still returned (I33)
        [InlineData("MIN([1, 2], (e) => IF(e == 1, null, ERROR(\"x\")))", "[CUSTOM_ERROR (1)] x")]
        [InlineData("MAX([1, 2], (e) => IF(e == 1, null, \"a\"))", "OPERATION_ERROR")]
        [InlineData("MIN([1, 2], (a, b) => a)", "ARGUMENT_ERROR")]
        public void Aggregates(string formula, string expected) => AssertResult(formula, expected);

        // GET.md

        [Theory]
        [InlineData("[10, 20, 30][5]", "INVALID_INDEX_ERROR")] [InlineData("GET([10, 20, 30], 5, 0)", "0")] [InlineData("GET([10, 20, 30], -1, null)", "null")]
        [InlineData("GET({name: \"Ada\"}, \"age\", 0)", "0")] [InlineData("GET({name: \"Ada\"}, \"name\", 0)", "Ada")]
        [InlineData("null.close", "null")] [InlineData("null[\"close\"]", "null")] [InlineData("GET(null, \"close\")", "null")]
        [InlineData("$x = null\n$x.close", "null")] [InlineData("$x = null\n$x.a.b", "null")]
        [InlineData("GET(null, \"close\", 1)", "ARGUMENT_ERROR")] [InlineData("GET({ a: 1 }, \"a\", ERROR(\"x\"))", "1")] [InlineData("GET([1, 2], 0, 9)", "1")]
        [InlineData("{name: \"Ada\"}[\"first name\"]", "INVALID_KEY_ERROR")]
        [InlineData("@array_indexing = 1\nGET([1, 2], 0, 9)", "9")] [InlineData("@array_indexing = 1\nGET([1, 2], 2, 9)", "2")]
        public void Get(string formula, string expected) => AssertResult(formula, expected);

        // APPEND.md, INSERT.md, PUT.md, REMOVE.md, CONCAT.md

        [Theory]
        [InlineData("APPEND([], \"a\")", "[\"a\"]")] [InlineData("APPEND([1], null)", "[1, null]")] [InlineData("APPEND(1, 2)", "ARGUMENT_ERROR")]
        [InlineData("INSERT([2, 3], 0, 1)", "[1, 2, 3]")] [InlineData("INSERT([1, 2], -1, 0)", "INVALID_INDEX_ERROR")] [InlineData("INSERT([1, 2], 0.5, 0)", "INVALID_INDEX_ERROR")]
        [InlineData("INSERT([], 0, 1)", "[1]")]
        [InlineData("PUT([1], 0, null)", "[null]")] [InlineData("PUT([], 0, 1)", "INVALID_INDEX_ERROR")]
        [InlineData("REMOVE([1, 2, 3], 2)", "[1, 2]")] [InlineData("$a = [1, 2, 3]\nREMOVE($a, LEN($a) - 1)", "[1, 2]")]
        [InlineData("$a = [1, 2, 3]\n$b = REMOVE($a, 0)\n$c = PUT($a, 0, 9)\n$d = INSERT($a, 0, 0)\n[$a, $b, $c, $d]", "[[1, 2, 3], [2, 3], [9, 2, 3], [0, 1, 2, 3]]")]
        [InlineData("CONCAT([], [1])", "[1]")] [InlineData("CONCAT([[1]], [[2]])", "[[1], [2]]")] [InlineData("CONCAT([1], \"a\")", "ARGUMENT_ERROR")]
        [InlineData("CONCAT(\"foo\", \"bar\")", "foobar")]
        [InlineData("@array_indexing = 1\nPUT([1, 2], 1, 9)", "[9, 2]")] [InlineData("@array_indexing = 1\nREMOVE([1, 2], 2)", "[1]")]
        [InlineData("@array_indexing = 1\nINSERT([1, 2], 3, 3)", "[1, 2, 3]")] [InlineData("@array_indexing = 1\nINSERT([1, 2], 0, 0)", "INVALID_INDEX_ERROR")]
        public void ArrayFunctions(string formula, string expected) => AssertResult(formula, expected);

        // VALUES.md, ENTRIES.md, FROM_ENTRIES.md, PUT.md, REMOVE.md, MERGE.md

        [Theory]
        [InlineData("ENTRIES({})", "[]")]
        [InlineData("$o = { a: true, b: false }\nMAP(FILTER(ENTRIES($o), (e) => e[1]), (e) => e[0])", "[\"a\"]")]
        [InlineData("ALL(VALUES({ a: true, b: false }), (c) => c)", "false")] [InlineData("SUM(VALUES({ trend: 2, entry: 1 }))", "3")]
        [InlineData("$o = { a: 1, b: 2 }\nMAP(KEYS($o), (k) => $o[k]) == VALUES($o)", "true")]
        [InlineData("FROM_ENTRIES(MAP([20, 50], (p) => [\"period\" + TEXT(p), p * 2]))", "{ period20: 40, period50: 100 }")]
        [InlineData("FROM_ENTRIES([])", "{ }")] [InlineData("FROM_ENTRIES([[1, \"a\"]])", "ARGUMENT_ERROR")] [InlineData("FROM_ENTRIES([\"a\"])", "ARGUMENT_ERROR")]
        [InlineData("FROM_ENTRIES([[\"a\", 1], ERROR(\"x\")])", "[CUSTOM_ERROR (1)] x")]
        [InlineData("$o = { a: 1, b: 2 }\nFROM_ENTRIES(ENTRIES($o)) == $o", "true")]
        [InlineData("PUT({ a: 1, b: 2 }, \"a\", 3)", "{ a: 3, b: 2 }")] [InlineData("REMOVE({ a: 1 }, \"a\")", "{ }")]
        [InlineData("$o = { a: 1 }\n$p = PUT($o, \"b\", 2)\n$r = REMOVE($o, \"a\")\n[$o, $p, $r]", "[{ a: 1 }, { a: 1, b: 2 }, { }]")]
        [InlineData("MERGE({ a: 1 }, { b: 2 })", "{ a: 1, b: 2 }")] [InlineData("MERGE({ a: 1 }, { b: 2 }, { c: 3 }, { a: 4 })", "{ a: 4, b: 2, c: 3 }")]
        [InlineData("MERGE({ a: 1 }, { b: 2 }, 3)", "ARGUMENT_ERROR")]
        // MERGE of one object is not an overload ("more than two arguments", I34)
        [InlineData("MERGE({ a: 1 })", "ARGUMENT_ERROR")]
        public void ObjectFunctions(string formula, string expected) => AssertResult(formula, expected);

        // Definitions of core-1.3

        [Fact]
        public void Standards_ListCore10ToCore13()
        {
            Assert.Equal("core-1.3", CoreStandard.Identifier);
            Assert.Equal("[\"core-1.0\", \"core-1.1\", \"core-1.2\", \"core-1.3\"]", Run("STANDARDS"));
            Assert.StartsWith("[SUCCESS", Run("REQUIRE \"core-1.3+\""));
        }

        [Fact]
        public void TheFunctionsOfCore13AreDefined()
        {
            var defined = CoreStandard.Instance.NamedFunctions.Select(f => f.Name).ToHashSet();
            string[] added = ["APPEND", "INSERT", "PUT", "REMOVE", "VALUES", "ENTRIES", "FROM_ENTRIES"];
            Assert.Empty(added.Where(name => !defined.Contains(name)));
        }

        [Fact]
        public void ArgumentTemplates_MayAcceptNull()
        {
            var env = Env.Create();
            ArgumentTemplate number = ArgumentTemplate.Of("a", Types.Number, nullable: true);
            ArgumentTemplate numbers = ArgumentTemplate.ArrayOf("numbers", Types.Number, nullable: true);

            Assert.Equal("a <Number | Null>", number.ToString());
            Assert.Equal("numbers <Array<Number | Null>>", numbers.ToString());
            Assert.True(ArgumentUtils.MatchTemplateType(Value.Null(), number, env));
            Assert.True(ArgumentUtils.MatchTemplateType(Value.Number(1), number, env));
            Assert.False(ArgumentUtils.MatchTemplateType(Value.Text("1", "\"1\""), number, env));
            Assert.False(ArgumentUtils.MatchTemplateType(Value.Null(), numbers, env));
            Assert.True(ArgumentUtils.MatchTemplateType(Value.Array([Value.Number(1), Value.Null()], ""), numbers, env));
            Assert.False(ArgumentTemplate.Of("a", Types.Number).Nullable);
        }
    }
}
