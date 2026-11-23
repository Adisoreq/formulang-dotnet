using Microsoft.Extensions.DependencyInjection;
using Bunit;
using WebPreview.Components;

namespace WebPreview.Tests
{
    /// <summary>Rendering of a Razor component with bUnit.</summary>
    public class ProblemListTests : TestContext
    {
        [Fact]
        public void RendersNothingWithoutProblems()
        {
            var list = RenderComponent<ProblemList>(p => p.Add(c => c.Items, Array.Empty<string>()));
            Assert.Empty(list.Markup.Trim());
        }

        [Fact]
        public void RendersEveryProblemAndTheNote()
        {
            var list = RenderComponent<ProblemList>(p => p
                .Add(c => c.Items, new[] { "First", "Second" })
                .AddChildContent("<li class=\"note\">Note</li>"));

            var items = list.FindAll("ul.config-problems > li");
            Assert.Equal(["First", "Second", "Note"], items.Select(i => i.TextContent));
            Assert.Equal("alert", list.Find("ul").GetAttribute("role"));
        }
    }

    /// <summary>The parameter table shared by the editors of indicators and patterns.</summary>
    public class ParameterTableTests : TestContext
    {
        [Fact]
        public void AddsAParameterWithAFreeId()
        {
            Services.AddSingleton(new PreviewState());
            var parameters = new List<IndicatorParameterDraft>();

            var table = RenderComponent<ParameterTable>(p => p.Add(c => c.Parameters, parameters).Add(c => c.Kind, "pattern"));
            Assert.Contains("The pattern has no parameters.", table.Markup);

            table.Find("button.ghost").Click();

            IndicatorParameterDraft added = Assert.Single(parameters);
            Assert.Equal("param1", added.Id);
        }
    }
}
