using Enochian.Cdsl;
using Enochian.Flow.Steps;
using Enochian.Lexicons;
using Enochian.Text;
using Enochian.UnitTests;
using System.Text;
using System.Text.Json.Nodes;

namespace Enochian.IntegrationTests;

[TestClass]
public class FlowTests
{
    private const string IpaTransducerPath = @"samples/ipatransducer.json";

    private static string GetConfigPath(string relativePath)
    {
        var assemblyDir = Path.GetDirectoryName(typeof(FlowTests).Assembly.Location)
            ?? throw new InvalidOperationException("Unable to determine the integration-test assembly directory.");
        var configPath = Path.Combine(assemblyDir, "../../../../..", relativePath);
        return Path.GetFullPath(configPath);
    }

    [TestMethod]
    [DataRow(IpaTransducerPath, "py",
        @"+Cons,-Son,-Syll,+Labial,-Round,-Cor,-Dorsal,-Phar,-Voice,-SG,-CG,-Cont,-Strident,-Lateral,-DelRel,-Nasal;
              -Cons,+Son,+Syll,+Labial,+Round,-Cor,+Dorsal,+High,-Low,-Back,+Tense,+Phar,+ATR,+Voice,-SG,-CG,+Cont,-Strident,-Lateral,-DelRel,-Nasal")]
    [DataRow(IpaTransducerPath, @"pБy",
        @"+Cons,-Son,-Syll,+Labial,-Round,-Cor,-Dorsal,-Phar,-Voice,-SG,-CG,-Cont,-Strident,-Lateral,-DelRel,-Nasal;;
              -Cons,+Son,+Syll,+Labial,+Round,-Cor,+Dorsal,+High,-Low,-Back,+Tense,+Phar,+ATR,+Voice,-SG,-CG,+Cont,-Strident,-Lateral,-DelRel,-Nasal")]
    [DataRow(IpaTransducerPath, @"pʰy",
        @"+Cons,-Son,-Syll,+Labial,-Round,-Cor,-Dorsal,-Phar,-Voice,+SG,-CG,-Cont,-Strident,-Lateral,-DelRel,-Nasal;
              -Cons,+Son,+Syll,+Labial,+Round,-Cor,+Dorsal,+High,-Low,-Back,+Tense,+Phar,+ATR,+Voice,-SG,-CG,+Cont,-Strident,-Lateral,-DelRel,-Nasal")]
    public void TestIPATransducer(string fname, string given, string expected)
    {
        var configPath = GetConfigPath(fname);
        var flow = new Flow.Flow(configPath);
        AssertUtils.NoErrors(flow);

        var features = flow.FeatureSets.FirstOrDefault(fs => fs.Id == "Default");
        Assert.IsNotNull(features, "no Default feature set");

        var steps = flow.Steps;
        Assert.IsNotNull(steps, "flow has no steps");

        var sampleText = steps.Children.FirstOrDefault() as SampleText;
        Assert.IsNotNull(sampleText, "first step is not SampleText");

        var transducer = steps.Children.LastOrDefault() as Transducer;
        Assert.IsNotNull(transducer, "last step is not Transducer");

        var encoding = transducer.Encoding;
        Assert.IsNotNull(encoding, "transducer has no output encoding");

        var tokens = given.Split([' ', '\t', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries);
        sampleText.Chunks =
        [
            new TextChunk
            {
                Lines =
                [
                    new TextLine
                    {
                        SourceStep = sampleText,
                        Text = given,
                        Segments = [.. tokens
                            .Select(t => new TextSegment
                            {
                                Options =
                                [
                                    new SegmentOption { Text = t }
                                ]
                            })],
                    }
                ]
            }
        ];

        var errors = new List<string>();
        var expectedPhones = expected.Split(';')
            .Select(fs => features.GetFeatureVector(fs.Split(','), errors))
            .ToList();
        Assert.IsFalse(errors.Count != 0, string.Join(", ", errors));

        var outputs = flow.GetOutputs().OfType<TextChunk>();
        var chunkIter = outputs.GetEnumerator();
        var expectedIter = expectedPhones.GetEnumerator();
        foreach (var token in tokens)
        {
            if (!chunkIter.MoveNext())
            {
                Assert.Fail($"no output for token '{token}'");
            }

            var chunk = chunkIter.Current;
            var iline = chunk.Lines
                .FirstOrDefault(line => ReferenceEquals(line.SourceStep, transducer));
            Assert.IsNotNull(iline, "unable to find line from transducer");

            Assert.IsNotNull(iline.Segments);
            Assert.AreEqual(1, iline.Segments.Count, "expected 1 segments");
            foreach (var option in iline.Segments.Select(seg => seg.Options.First()))
            {
                Assert.IsNotNull(option.Phones);
                var actualPhones = option.Phones.Where(p => p.Length == features.NumDimensions).ToArray();
                foreach (var phone in actualPhones)
                {
                    if (!expectedIter.MoveNext())
                    {
                        Assert.Fail($"no expected phone for token '{token}', seg '{option.Text}'");
                    }

                    var expectedPhone = expectedIter.Current;
                    double distance = Math.DynamicTimeWarp.EuclideanDistance(expectedPhone, phone);
                    var expSpec = string.Join(",", features.GetFeatureSpec(expectedPhone));
                    var actSpec = string.Join(",", features.GetFeatureSpec(phone));
                    Assert.IsTrue(distance < 0.001,
                        $"distance for token '{token}', seg '{option.Text}' is {distance}; expected {expSpec}; actual {actSpec}");
                }
            }
        }
    }


    private const string EnglishTestPath = @"samples/english_test.json";

    [TestMethod]
    public void EnglishTestSimple()
    {
        var configPath = GetConfigPath(EnglishTestPath);
        var flow = new Flow.Flow(configPath);
        AssertUtils.NoErrors(flow);

        var steps = flow.Steps;
        Assert.IsNotNull(steps, "flow has no steps");

        foreach (var lexicon in flow.Lexicons)
        {
            lexicon.MaxEntriesToLoad = 1000;
        }

        var given = "aardvark absolved abelard";
        var tokens = given.Split([' ', '\t', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries);
        foreach (var sampleText in steps.Children.OfType<SampleText>())
        {
            sampleText.Chunks =
            [
                new TextChunk
                {
                    Lines =
                    [
                        new TextLine
                        {
                            Text = given,
                            SourceStep = sampleText,
                            Segments = [.. tokens
                                .Select(t => new TextSegment
                                {
                                    Options =
                                    [
                                        new SegmentOption { Text = t}
                                    ]
                                })],
                        }
                    ]
                }
            ];
        }

        var reportPath = flow.GetOutputs().Single() as string;
        Assert.IsNotNull(reportPath);

        var dtwMatcher = steps.Children.OfType<DTWMatcher>().LastOrDefault();
        Assert.IsNotNull(dtwMatcher);
        Assert.IsNotEmpty(dtwMatcher.Lexicons, "DTW matcher has no configured lexicons");

        var matchReport = steps.Children.OfType<MatchReport>().LastOrDefault();
        Assert.IsNotNull(matchReport);

        var results = matchReport.Results;
        Assert.IsNotNull(results);

        var dtwLine = results
            .SelectMany(chunk => chunk.Lines)
            .FirstOrDefault(line => ReferenceEquals(line.SourceStep, dtwMatcher));
        var resultStepIds = string.Join(", ", results.SelectMany(chunk => chunk.Lines).Select(line => line.SourceStep?.Id));
        Assert.IsNotNull(dtwLine, $"No DTW line found. Result step IDs: {resultStepIds}");

        Assert.AreEqual(tokens.Length, dtwLine.Segments.Count);

        for (int i = 0; i < tokens.Length; i++)
        {
            var expected = tokens[i].ToUpperInvariant();
            var options = dtwLine.Segments[i].Options;
            var found = options.Any(opt => opt.Text?.StartsWith(expected, StringComparison.OrdinalIgnoreCase) == true);
            Assert.IsTrue(found, "did not find a CMU entry for " + expected);
            Assert.IsTrue(options
                .Where(option => (option.Tags & TextTag.Match) != TextTag.None)
                .All(option => option.MatchResult != null
                    && option.RawDistance == option.MatchResult.Cost
                    && option.DtwPathLength == option.MatchResult.PathLength
                    && option.MeanPathDistance == option.MatchResult.MeanPathCost
                    && option.MeanInputLengthDistance == option.MatchResult.MeanInputLengthCost));
        }
    }

    [TestMethod]
    public void SanskritPanelSearchesEachFixtureLexiconAndPreservesSource()
    {
        using var fixture = new SanskritPanelFixture();
        var flow = fixture.CreateFlow();
        AssertUtils.NoErrors(flow);

        var result = flow.GetOutputs().Single() as TextChunk;

        Assert.IsNotNull(result);
        foreach (var matcher in flow.Steps!.Children.OfType<DTWMatcher>())
        {
            var expectedSource = matcher.Lexicons.Single().Entries
                .Select(entry => entry.SourceId)
                .Distinct(StringComparer.Ordinal)
                .Single();
            var line = result.Lines.Single(candidate => ReferenceEquals(candidate.SourceStep, matcher));
            Assert.IsTrue(line.Segments
                .SelectMany(segment => segment.Options)
                .Any(option => string.Equals(option.Entry?.SourceId, expectedSource, StringComparison.Ordinal)));
        }

        var legacyFlow = fixture.CreateLegacyShsFlow();
        var comparison = SanskritCorpusBuilder.CompareShs(
            legacyFlow.Lexicons.Single().Entries,
            flow.Lexicons.Single(lexicon => lexicon.Id == "cdsl-shs").Entries,
            0,
            new Dictionary<string, string>(StringComparer.Ordinal));
        Assert.AreEqual(0, comparison.Discrepancies.Count);
        Assert.AreEqual(0, comparison.UnexplainedAboveTolerance);
    }

    private sealed class SanskritPanelFixture : IDisposable
    {
        private static readonly string[] DictionaryCodes = ["mw", "ap", "pwg", "pw", "shs"];
        private readonly string root = Path.Combine(Path.GetTempPath(), $"enochian-sanskrit-panel-{Guid.NewGuid():N}");

        public SanskritPanelFixture()
        {
            _ = Directory.CreateDirectory(root);
        }

        public Enochian.Flow.Flow CreateFlow()
        {
            var resources = new Enochian.Flow.Flow(GetConfigPath("resources/lexicons/cdsl-normalization.flow.json"));
            AssertUtils.NoErrors(resources);
            var adapter = new CdslOrigAdapter(
                resources.FeatureSets.Single(featureSet => featureSet.Id == "Default"),
                resources.Encodings.Single(encoding => encoding.Id == "SLP1"));
            var manifestPath = Path.Combine(root, "manifest.json");
            File.WriteAllText(manifestPath, "{}", new UTF8Encoding(false));
            var lexicons = new JsonArray();
            var steps = new JsonArray
            {
                new JsonObject
                {
                    ["id"] = "Sanskrit Sample",
                    ["type"] = "SampleText",
                    ["features"] = "Default",
                    ["text"] = "aɡni",
                },
                new JsonObject
                {
                    ["id"] = "Encode Sanskrit Sample",
                    ["type"] = "Transducer",
                    ["features"] = "Default",
                    ["encoding"] = "IPA",
                },
            };

            foreach (var dictionaryCode in DictionaryCodes)
            {
                var sourceId = $"cdsl-{dictionaryCode}";
                var outputPath = Path.Combine(root, sourceId + ".jsonl");
                _ = adapter.Normalize(
                    new CdslManifest(
                        sourceId,
                        dictionaryCode,
                        "https://github.com/sanskrit-lexicon/csl-orig",
                        "fixture-revision",
                        new string('0', 64),
                        "CC-BY-SA-4.0",
                        dictionaryCode + ".txt",
                        outputPath),
                    GetConfigPath($"source/Enochian.UnitTests/Fixtures/Cdsl/{dictionaryCode}.txt"),
                    outputPath,
                    Path.Combine(root, sourceId + ".adapter-quality.json"),
                    "fixture command");
                lexicons.Add(new JsonObject
                {
                    ["id"] = sourceId,
                    ["type"] = "NormalizedLexicon",
                    ["features"] = "Default",
                    ["encoding"] = "IPA",
                    ["path"] = outputPath,
                    ["manifest"] = manifestPath,
                    ["qualityReport"] = Path.Combine(root, sourceId + ".loader-quality.json"),
                });
                steps.Add(new JsonObject
                {
                    ["id"] = "Search " + dictionaryCode,
                    ["type"] = "DTWMatcher",
                    ["lexicon"] = sourceId,
                    ["numOptions"] = 1,
                    ["tolerance"] = 0.0,
                });
            }

            var config = new JsonObject
            {
                ["id"] = "Sanskrit Fixture Panel",
                ["features"] = new JsonArray
                {
                    new JsonObject { ["path"] = GetConfigPath("resources/encodings/features.json") },
                },
                ["encodings"] = new JsonArray
                {
                    new JsonObject
                    {
                        ["id"] = "IPA",
                        ["features"] = "Default",
                        ["path"] = GetConfigPath("resources/encodings/ipa.json"),
                    },
                },
                ["lexicons"] = lexicons,
                ["steps"] = steps,
            };
            var configPath = Path.Combine(root, "flow.json");
            File.WriteAllText(configPath, config.ToJsonString(), new UTF8Encoding(false));
            return new Enochian.Flow.Flow(configPath);
        }

        public Enochian.Flow.Flow CreateLegacyShsFlow()
        {
            var config = new JsonObject
            {
                ["id"] = "Legacy SHS Fixture",
                ["features"] = new JsonArray
                {
                    new JsonObject { ["path"] = GetConfigPath("resources/encodings/features.json") },
                },
                ["encodings"] = new JsonArray
                {
                    new JsonObject
                    {
                        ["id"] = "IPA",
                        ["features"] = "Default",
                        ["path"] = GetConfigPath("resources/encodings/ipa.json"),
                    },
                    new JsonObject
                    {
                        ["id"] = "SLP1",
                        ["features"] = "Default",
                        ["path"] = GetConfigPath("resources/encodings/slp1.json"),
                    },
                },
                ["lexicons"] = new JsonArray
                {
                    new JsonObject
                    {
                        ["id"] = "SHS-Legacy",
                        ["type"] = "ShabdaSagara",
                        ["features"] = "Default",
                        ["encoding"] = "SLP1",
                        ["path"] = GetConfigPath("source/Enochian.UnitTests/Fixtures/Cdsl/shs.txt"),
                    },
                },
                ["steps"] = new JsonArray(),
            };
            var configPath = Path.Combine(root, "legacy-shs-flow.json");
            File.WriteAllText(configPath, config.ToJsonString(), new UTF8Encoding(false));
            var flow = new Enochian.Flow.Flow(configPath);
            AssertUtils.NoErrors(flow);
            return flow;
        }

        public void Dispose()
        {
            Directory.Delete(root, true);
        }
    }
}
