using Enochian.Controls;
using System.Text;

namespace Enochian.UnitTests;

[TestClass]
public sealed class ControlSourceAdapterTests
{
    private static readonly string FixtureRoot = Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "../../../../../source/Enochian.UnitTests/Fixtures/Controls"));

    [TestMethod]
    public void ParsesUniMorphWithoutPromotingInflectedFormsToLemmas()
    {
        var result = UniMorphAdapter.Parse(Path.Combine(FixtureRoot, "unimorph-indic.fixture.tsv"));

        Assert.HasCount(4, result.Lemmas);
        Assert.HasCount(5, result.InflectedForms!);
        Assert.IsTrue(result.Lemmas.All(lemma => lemma.RecordId.StartsWith("lemma:", StringComparison.Ordinal)));
        Assert.IsTrue(result.InflectedForms!.All(form => form.RecordId.StartsWith("form:", StringComparison.Ordinal)));
        Assert.IsFalse(result.Lemmas.Any(lemma => lemma.NormalizedForm is "क़लमों" or "हँसता"));
        Assert.IsTrue(result.Lemmas.Any(lemma => lemma.NormalizedForm.Contains('्')));
        Assert.IsTrue(result.Lemmas.Any(lemma => lemma.NormalizedForm.Contains('़')));
        Assert.IsTrue(result.Lemmas.Any(lemma => lemma.NormalizedForm.Contains('ँ')));
        Assert.IsTrue(result.Lemmas.All(lemma => lemma.NormalizedForm.IsNormalized(NormalizationForm.FormC)));
    }

    [TestMethod]
    public void ExcludesUnvocalizedPersoArabicLemmasFromPhonology()
    {
        var result = UniMorphAdapter.Parse(
            Path.Combine(FixtureRoot, "unimorph-persian.fixture.tsv"),
            requireArabicVowelMarks: true);

        Assert.AreEqual("کِتاب", result.Lemmas.Single().NormalizedForm);
        Assert.HasCount(2, result.InflectedForms!);
        Assert.AreEqual("uncertain_unvocalized_orthography", result.Rejections.Single().Category);
    }

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
