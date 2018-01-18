using System;
using System.Collections.Generic;
using System.Dynamic;
using System.IO;
using System.Linq;
using System.Reflection;
using Enochian.Lexicons;
using Enochian.Text;

namespace Enochian.Flow
{
    public interface IFlowResources
    {
        IList<FeatureSet> FeatureSets { get; }
        IList<Encoding> Encodings { get; }
        IList<Lexicon> Lexicons { get; }
    }

    public class Flow : Configurable, IFlowResources
    {
        static readonly NLog.Logger logger = NLog.LogManager.GetCurrentClassLogger();
        IEnumerable<IConfigurable> children;

        public Flow(string fname)
            : base(null)
        {
            Load(fname, this);
        }

        public override NLog.Logger Log => logger;

        public override IEnumerable<IConfigurable> Children
        {
            get => children ?? (children = FeatureSets
                .Concat<IConfigurable>(Encodings)
                .Concat(Steps != null ? new IConfigurable[] { Steps } : Enumerable.Empty<IConfigurable>()));
        }

        public IList<FeatureSet> FeatureSets { get; } = new List<FeatureSet>();
        public IList<Encoding> Encodings { get; } = new List<Encoding>();
        public IList<Lexicon> Lexicons { get; } = new List<Lexicon>();

        public FlowContainer Steps { get; private set; }

        public override IConfigurable Configure(IDictionary<string, object> config)
        {
            base.Configure(config);

            ConfigureFeatures(config);
            ConfigureEncodings(config);
            ConfigureLexicons(config);

            Steps = new FlowContainer(this, this, config);
            return this;
        }

        void ConfigureFeatures(IDictionary<string, object> config)
        {
            var features = config.GetChildren("features", this);
            if (features != null)
            {
                foreach (var fset in features)
                {
                    var featureSet = new FeatureSet(this)
                    {
                        Id = fset.Get<string>("id", this),
                        RelativePath = fset.Get<string>("path", this),
                    };

                    FeatureSets.Add(Load(this, featureSet, featureSet.RelativePath));
                }
            }
        }

        void ConfigureEncodings(IDictionary<string, object> config)
        {
            var encodings = config.GetChildren("encodings", this);
            if (encodings != null)
            {
                foreach (var enc in encodings)
                {
                    var featuresName = enc.Get<string>("features", this);
                    var encoding = new Encoding(this)
                    {
                        Id = enc.Get<string>("id", this),
                        RelativePath = enc.Get<string>("path", this),
                        Features = FeatureSets.FirstOrDefault(fs => fs.Id == featuresName),
                    };

                    if (encoding.Features == null)
                        AddError("unknown feature set '{0}' encoding '{1}'", featuresName, encoding.Id);

                    Encodings.Add(Load(this, encoding, encoding.RelativePath));
                }
            }

            if (!Encodings.Any(e => e.Id == Encoding.Default.Id))
                Encodings.Add(Encoding.Default);
        }

        void ConfigureLexicons(IDictionary<string, object> config)
        {
            var lexicons = config.GetChildren("lexicons", this);
            if (lexicons != null)
            {
                foreach (var lex in lexicons)
                {
                    var id = lex.Get<string>("id", this);

                    var typeName = lex.Get<string>("type", this);
                    if (string.IsNullOrWhiteSpace(typeName))
                    {
                        AddError("no type name for lexicon '{0}'", id ?? "?");
                        continue;
                    }

                    var lexType = Type.GetType(typeName, false);
                    if (lexType == null) lexType = Type.GetType("Enochian.Lexicons." + typeName, false);
                    if (lexType == null)
                    {
                        AddError("unable to find lexicon type '{0}'", typeName);
                        continue;
                    }

                    if (!typeof(Lexicon).IsAssignableFrom(lexType))
                    {
                        AddError("type '{0}' is not a subtype of Enochian.Lexicons.Lexicon", lexType.FullName);
                        continue;
                    }

                    var ctor = lexType.GetConstructor(new[] { typeof(IConfigurable), typeof(IFlowResources) });
                    if (ctor == null)
                    {
                        AddError("type '{0}' does not have a constructor with parameters of type IConfigurable and IFlowResources");
                        continue;
                    }

                    var child = ctor.Invoke(new object[] { this, this }) as Lexicon;
                    child.Parent = this;
                    child.Configure(lex);

                    Lexicons.Add(child);
                }
            }
        }

        public override void PostConfigure()
        {
            PostConfigureChildren(this);
        }

        void PostConfigureChildren(IConfigurable obj)
        {
            foreach (var child in obj.Children)
            {
                PostConfigureChildren(child);
            }
            obj.PostConfigure();
        }

        public IEnumerable<object> GetOutputs()
        {
            var lastStep = Steps.Children.LastOrDefault();
            if (lastStep == null)
                yield break;

            var getOutputs = lastStep.GetType().GetMethod(nameof(FlowStep<int, int>.GetOutputs));
            if (getOutputs == null)
                yield break;

            var enumerable = getOutputs.Invoke(lastStep, null) as System.Collections.IEnumerable;
            if (enumerable == null)
                yield break;

            foreach (var output in enumerable)
                yield return output;
        }

        public void ProcessAll()
        {
            object lastOutput;
            foreach (var output in GetOutputs())
                lastOutput = output;
        }
    }
}
