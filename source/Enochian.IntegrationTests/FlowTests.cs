using Enochian.Flow.Steps;
using Enochian.Text;
using Enochian.UnitTests;

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
        }
    }
}
