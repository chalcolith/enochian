using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Enochian.Flow.Steps;
using Enochian.Text;
using Enochian.UnitTests;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Enochian.IntegrationTests
{
    [TestClass]
    public class FlowTests
    {
        const string IpaTransducerPath = @"samples/ipatransducer.json";

        [DataTestMethod]
        [DataRow(IpaTransducerPath, "py",
            @"+Cons,-Son,-Syll,+Labial,-Round,-Cor,-Dorsal,-Phar,-Voice,-SG,-CG,-Cont,-Strident,-Lateral,-DelRel,-Nasal;
              -Cons,+Son,+Syll,+Labial,+Round,-Cor,+Dorsal,+High,-Low,-Back,+Tense,+Phar,+ATR,+Voice,-SG,-CG,+Cont,-Strident,-Lateral,-DelRel,-Nasal")]
        [DataRow(IpaTransducerPath, @"pʰy",
            @"+Cons,-Son,-Syll,+Labial,-Round,-Cor,-Dorsal,-Phar,-Voice,+SG,-CG,-Cont,-Strident,-Lateral,-DelRel,-Nasal;
              -Cons,+Son,+Syll,+Labial,+Round,-Cor,+Dorsal,+High,-Low,-Back,+Tense,+Phar,+ATR,+Voice,-SG,-CG,+Cont,-Strident,-Lateral,-DelRel,-Nasal")]
        [DataRow(IpaTransducerPath, @"pБy",
            @"+Cons,-Son,-Syll,+Labial,-Round,-Cor,-Dorsal,-Phar,-Voice,-SG,-CG,-Cont,-Strident,-Lateral,-DelRel,-Nasal;;
              -Cons,+Son,+Syll,+Labial,+Round,-Cor,+Dorsal,+High,-Low,-Back,+Tense,+Phar,+ATR,+Voice,-SG,-CG,+Cont,-Strident,-Lateral,-DelRel,-Nasal")]
        public void TestIPATransducer(string fname, string given, string expected)
        {
            var assemblyDir = Path.GetDirectoryName(typeof(FlowTests).GetTypeInfo().Assembly.Location);
            var configPath = Path.Combine(assemblyDir, "../../../../..", fname);
            var flow = new Flow.Flow(configPath);
            AssertUtils.NoErrors(flow);

            var features = flow.FeatureSets.FirstOrDefault(fs => fs.Name == "Default");
            Assert.IsNotNull(features, "no Default feature set");

            var sampleText = flow.Steps.Children.FirstOrDefault() as SampleText;
            Assert.IsNotNull(sampleText, "first step is not SampleText");

            var transducer = flow.Steps.Children.LastOrDefault() as Transducer;
            Assert.IsNotNull(transducer, "last step is not Transducer");

            var encoding = transducer.OutputEncoding;
            Assert.IsNotNull(encoding, "transducer has no output encoding");

            var tokens = sampleText.Tokens = given.Split(SampleText.WHITESPACE, StringSplitOptions.RemoveEmptyEntries);

            var errors = new List<string>();
            var expectedVectors = expected.Split(';')
                .Select(fs => features.GetFeatureVector(fs.Split(','), errors))
                .ToList();
            Assert.IsFalse(errors.Any(), string.Join(", ", errors));

            var outputs = flow.GetOutputs().OfType<TextChunk>();
            var chunkIter = outputs.GetEnumerator();
            var expectedIter = expectedVectors.GetEnumerator();
            foreach (var token in tokens)
            {
                if (!chunkIter.MoveNext())
                    Assert.Fail("no output for token '{0}'", token);

                var chunk = chunkIter.Current;
                var iline = chunk.Lines.FirstOrDefault(line => line.Encoding == encoding);
                Assert.IsNotNull(iline, "unable to find segment with encoding '{0}'", encoding.Name);

                Assert.IsNotNull(iline.Segments);
                Assert.AreEqual(1, iline.Segments.Count, "expected 1 segments");
                foreach (var seg in iline.Segments)
                {
                    Assert.IsNotNull(seg.Vectors);
                    var actualVectors = seg.Vectors.Where(v => v.Length == features.NumDimensions).ToArray();
                    foreach (var actualVector in actualVectors)
                    {
                        if (!expectedIter.MoveNext())
                            Assert.Fail("no expected vector for token '{0}', seg '{1}'", token, seg.Text);

                        var expectedVector = expectedIter.Current;

                        double distance = FeatureSet.EuclideanDistance(expectedVector, actualVector);
                        var expSpec = string.Join(",", features.GetFeatureSpec(expectedVector));
                        var actSpec = string.Join(",", features.GetFeatureSpec(actualVector));
                        Assert.IsTrue(distance < 0.001, "distance for token '{0}', seg '{1}' is {2}", token, seg.Text, distance);
                    }
                }
            }
        }
    }
}
