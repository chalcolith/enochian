using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Enochian.Flow.Steps;
using Enochian.Lexicons;
using Enochian.Text;
using Enochian.UnitTests;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Enochian.IntegrationTests
{
    [TestClass]
    public class FlowTests
    {
        const string IpaTransducerPath = @"samples/ipatransducer.json";

        string GetConfigPath(string relativePath)
        {
            var assemblyDir = Path.GetDirectoryName(typeof(FlowTests).GetTypeInfo().Assembly.Location);
            var configPath = Path.Combine(assemblyDir, "../../../../..", relativePath);
            return Path.GetFullPath(configPath);
        }

        [DataTestMethod]
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

            var sampleText = flow.Steps.Children.FirstOrDefault() as SampleText;
            Assert.IsNotNull(sampleText, "first step is not SampleText");

            var transducer = flow.Steps.Children.LastOrDefault() as Transducer;
            Assert.IsNotNull(transducer, "last step is not Transducer");

            var encoding = transducer.Encoding;
            Assert.IsNotNull(encoding, "transducer has no output encoding");

            var tokens = given.Split(new[] { ' ', '\t', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
            sampleText.Chunks = new List<TextLine>
            {
                new TextLine
                {
                    Text = given,
                    Segments = tokens
                        .Select(t => new TextSegment
                        {
                            Options = new List<SegmentOption>
                            {
                                new SegmentOption { Text = t }
                            }
                        })
                        .ToArray(),
                }
            };

            var errors = new List<string>();
            var expectedPhones = expected.Split(';')
                .Select(fs => features.GetFeatureVector(fs.Split(','), errors))
                .ToList();
            Assert.IsFalse(errors.Any(), string.Join(", ", errors));

            var outputs = flow.GetOutputs().OfType<TextChunk>();
            var chunkIter = outputs.GetEnumerator();
            var expectedIter = expectedPhones.GetEnumerator();
            foreach (var token in tokens)
            {
                if (!chunkIter.MoveNext())
                    Assert.Fail("no output for token '{0}'", token);

                var chunk = chunkIter.Current;
                var iline = chunk.Lines
                    .FirstOrDefault(line => object.ReferenceEquals(line.SourceStep, transducer));
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
                            Assert.Fail("no expected phone for token '{0}', seg '{1}'", token, option.Text);

                        var expectedPhone = expectedIter.Current;
                        double distance = Enochian.Math.DynamicTimeWarp.EuclideanDistance(expectedPhone, phone);
                        var expSpec = string.Join(",", features.GetFeatureSpec(expectedPhone));
                        var actSpec = string.Join(",", features.GetFeatureSpec(phone));
                        Assert.IsTrue(distance < 0.001, "distance for token '{0}', seg '{1}' is {2}", token, option.Text, distance);
                    }
                }
            }
        }


        const string EnglishTestPath = @"samples/english_test.json";

        [TestMethod]
        public void EnglishTestSimple()
        {
            var configPath = GetConfigPath(EnglishTestPath);
            var flow = new Flow.Flow(configPath);
            AssertUtils.NoErrors(flow);

            foreach (var lexicon in flow.Lexicons)
            {
                lexicon.MaxEntriesToLoad = 1000;
            }

            var given = "aardvark absolved abelard";
            var tokens = given.Split(new[] { ' ', '\t', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var sampleText in flow.Steps.Children.OfType<SampleText>())
            {
                sampleText.Chunks = new List<TextLine>
                {
                    new TextLine
                    {
                        Text = given,
                        Segments = tokens
                            .Select(t => new TextSegment
                            {
                                Options = new List<SegmentOption>
                                {
                                    new SegmentOption { Text = t}
                                }
                            })
                            .ToArray(),
                    }
                };
            }

            var reportPath = flow.GetOutputs().Single() as string;
            Assert.IsNotNull(reportPath);

            var dtwMatcher = flow.Steps.Children.OfType<DTWMatcher>().LastOrDefault();
            Assert.IsNotNull(dtwMatcher);

            var matchReport = flow.Steps.Children.OfType<MatchReport>().LastOrDefault();
            Assert.IsNotNull(matchReport);

            var dtwLine = matchReport.Results
                .SelectMany(chunk => chunk.Lines)
                .FirstOrDefault(line => object.ReferenceEquals(line.SourceStep, dtwMatcher));
            Assert.IsNotNull(dtwLine);

            Assert.AreEqual(tokens.Length, dtwLine.Segments.Count);

            for (int i = 0; i < tokens.Length; i++)
            {
                var expected = tokens[i].ToUpperInvariant();
                var options = dtwLine.Segments[i].Options;
                var found = options.Any(opt => opt.Text.ToUpperInvariant().StartsWith(expected));
                Assert.IsTrue(found, "did not find a CMU entry for " + expected);
            }
        }
    }
}
