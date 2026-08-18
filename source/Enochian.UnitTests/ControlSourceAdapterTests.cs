using Enochian.Controls;

namespace Enochian.UnitTests;

[TestClass]
public sealed class ControlSourceAdapterTests
{
    private static readonly string FixtureRoot = Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "../../../../../source/Enochian.UnitTests/Fixtures/Controls"));

    [TestMethod]
    public void ParsesTurkishOrthographyAndAttributesWithFrozenExclusions()
    {
        var result = ZemberekDictionaryAdapter.Parse(Path.Combine(FixtureRoot, "zemberek.fixture.dict"));

        AssertUtils.SequenceEquals(["ışık", "iğne", "kâğıt"], result.Lemmas.Select(lemma => lemma.NormalizedForm));
        Assert.IsTrue(result.Lemmas[0].Morphology.Contains("A:NoVoicing"));
        Assert.IsFalse(result.Lemmas.Any(lemma => lemma.OriginalForm.Contains('\'')));
        Assert.AreEqual(2, result.Rejections.Count(rejection => rejection.Category == "proper_name"));
        Assert.AreEqual(1, result.Rejections.Count(rejection => rejection.Category == "abbreviation"));
        Assert.AreEqual(2, result.Rejections.Count(rejection => rejection.Category == "malformed"));
    }

    [TestMethod]
    public void ParsesHungarianStemsWithoutExpandingMorphology()
    {
        var result = MagyarIspellAdapter.Parse(Path.Combine(FixtureRoot, "magyar"));

        AssertUtils.SequenceEquals(
            ["csizma", "dzsungel", "kenyér", "asszony", "kulcscsomó", "hosszú"],
            result.Lemmas.Select(lemma => lemma.NormalizedForm));
        Assert.AreEqual(6, result.Lemmas.Count);
        Assert.IsTrue(result.Lemmas.Single(lemma => lemma.NormalizedForm == "asszony")
            .Morphology.Contains("[compound:boundary]"));
        Assert.IsTrue(result.Lemmas.Single(lemma => lemma.NormalizedForm == "csizma")
            .Morphology.Contains("[flag:N]"));
        Assert.IsTrue(result.Lemmas.Single(lemma => lemma.NormalizedForm == "hosszú")
            .Morphology.Contains("[flag:A]"));
        Assert.AreEqual(1, result.Rejections.Count(rejection => rejection.Category == "proper_name"));
        Assert.AreEqual(1, result.Rejections.Count(rejection => rejection.Category == "abbreviation"));
        Assert.AreEqual(1, result.Rejections.Count(rejection => rejection.Category == "obsolete"));
        Assert.AreEqual(1, result.Rejections.Count(rejection => rejection.Category == "correction"));
    }
}
