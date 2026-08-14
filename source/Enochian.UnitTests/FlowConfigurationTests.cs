using Enochian.Flow.Steps;
using Enochian.Lexicons;
using Enochian.Text;
using System.Text.Json.Nodes;

namespace Enochian.UnitTests;

[TestClass]
public class FlowConfigurationTests
{
    [TestMethod]
    public void LoadsNestedSystemTextJsonConfiguration()
    {
        var configPath = Path.GetFullPath(
            Path.Combine(AppContext.BaseDirectory, "../../../../../samples/ipatransducer.json"));

        var flow = new Flow.Flow(configPath);

        AssertUtils.NoErrors(flow);
        Assert.AreEqual("Debug Test Flow", flow.Id);
        Assert.AreEqual(1, flow.FeatureSets.Count);
        Assert.AreEqual(2, flow.Encodings.Count);

        var steps = flow.Steps ?? throw new AssertFailedException("Flow steps were not configured.");
        AssertUtils.SequenceEquals(["LoadText", "EncodeIPA"], steps.Children.Select(step => step.Id));
    }

    [TestMethod]
    public void LoadsCommentedConfigurationAndUnionShapedHypothesisInputs()
    {
        var configPath = Path.GetFullPath(
            Path.Combine(AppContext.BaseDirectory, "../../../../../samples/voynich.json"));

        var flow = new Flow.Flow(configPath);

        AssertUtils.NoErrors(flow);
        var matcher = flow.Steps?.Children.OfType<DTWMatcher>().Single()
            ?? throw new AssertFailedException("DTW matcher was not configured.");
        var hypotheses = matcher.Hypotheses
            ?? throw new AssertFailedException("Hypotheses were not configured.");

        Assert.AreEqual(8, hypotheses.Groups.SelectMany(group => group.Entries).Count());
        Assert.IsTrue(hypotheses.Groups.SelectMany(group => group.Entries).Any(entry => entry.Input == "daiiin"));
    }

    [TestMethod]
    public void ConfiguresFiveIndependentNormalizedSanskritSearches()
    {
        var configPath = Path.GetFullPath(
            Path.Combine(AppContext.BaseDirectory, "../../../../../samples/sanskrit-panel.json"));

        var flow = new Flow.Flow(configPath);

        AssertUtils.NoErrors(flow);
        Assert.AreEqual(5, flow.Lexicons.OfType<NormalizedLexicon>().Count());
        Assert.IsTrue(flow.Lexicons.All(lexicon => lexicon.Encoding?.Id == "IPA"));
        var matchers = flow.Steps?.Children.OfType<DTWMatcher>().ToArray()
            ?? throw new AssertFailedException("Flow steps were not configured.");
        Assert.AreEqual(5, matchers.Length);
        Assert.IsTrue(matchers.All(matcher => matcher.Lexicons.Count == 1));
        Assert.AreEqual(5, matchers.Select(matcher => matcher.Lexicons.Single().Id).Distinct(StringComparer.Ordinal).Count());
    }

    [TestMethod]
    public void ConfiguresLatinControlFromFrozenArtifact()
    {
        var configPath = Path.GetFullPath(
            Path.Combine(AppContext.BaseDirectory, "../../../../../samples/latin-panel.json"));

        var flow = new Flow.Flow(configPath);

        AssertUtils.NoErrors(flow);
        var lexicon = flow.Lexicons.OfType<NormalizedLexicon>().Single();
        Assert.AreEqual("Perseus-Lewis-Short", lexicon.Id);
        Assert.AreEqual("IPA", lexicon.Encoding?.Id);
        var matcher = flow.Steps?.Children.OfType<DTWMatcher>().Single()
            ?? throw new AssertFailedException("Latin matcher was not configured.");
        Assert.AreEqual(lexicon, matcher.Lexicons.Single());
    }

    [TestMethod]
    public void ReadsTypedJsonNodeConfigurationValues()
    {
        var config = JsonNode.Parse(
            """
            {
              "name": "example",
              "count": 3,
              "ratio": 0.25,
              "enabled": true,
              "optional": null,
              "names": ["alpha", "beta"],
              "children": [{"id": "first"}, {"id": "second"}]
            }
            """)?.AsObject() ?? throw new AssertFailedException("Test configuration did not parse.");
        var errorHandler = new FeatureSet(null);

        Assert.AreEqual("example", config.Get<string>("name", errorHandler));
        Assert.AreEqual(3, config.Get<int>("count", errorHandler));
        Assert.AreEqual(0.25, config.Get<double>("ratio", errorHandler));
        Assert.IsTrue(config.Get<bool>("enabled", errorHandler));
        Assert.IsNull(config.Get<string>("optional", errorHandler));
        AssertUtils.SequenceEquals(["alpha", "beta"], config.GetList<string>("names", errorHandler));
        AssertUtils.SequenceEquals(["first", "second"],
            config.GetChildren("children", errorHandler).Select(child => child.Get<string>("id", errorHandler)));
        AssertUtils.NoErrors(errorHandler);
    }

    [TestMethod]
    public void ReportsInvalidJsonNodeConfigurationType()
    {
        var config = JsonNode.Parse("""{"count":"not a number"}""")?.AsObject()
            ?? throw new AssertFailedException("Test configuration did not parse.");
        var errorHandler = new FeatureSet(null);

        Assert.AreEqual(0, config.Get<int>("count", errorHandler));
        Assert.IsTrue(errorHandler.Errors.Any(error => error.Message?.Contains("count", StringComparison.Ordinal) == true));
    }
}
