using System;
using System.Collections.Generic;
using System.Linq;
using Enochian.Text;

namespace Enochian.Flow.Steps
{
    public class HypothesisFile : RelativeConfigurable
    {
        static readonly NLog.Logger logger = NLog.LogManager.GetCurrentClassLogger();

        public HypothesisFile(IConfigurable parent, IFlowResources resources)
            : base(parent)
        {
            Resources = resources;
        }

        public override NLog.Logger Log => logger;

        public IFlowResources Resources { get; }
        public Encoding Encoding { get; protected set; }

        public IList<HypothesisGroup> Groups { get; protected set; }

        public override IConfigurable Configure(IDictionary<string, object> config)
        {
            base.Configure(config);

            var encoding = config.Get<string>("encoding", this);
            if (!string.IsNullOrWhiteSpace(encoding))
            {
                Encoding = Resources.Encodings.FirstOrDefault(enc => enc.Id == encoding);
                if (Encoding == null)
                    AddError("invalid encoding id '{0}'", encoding);
            }
            else
            {
                AddError("no encoding specified");
            }

            Groups = new List<HypothesisGroup>();
            var groups = config.GetChildren("hypotheses", this);
            if (groups != null)
            {
                try
                {
                    foreach (var group in groups)
                    {
                        Groups.Add(new HypothesisGroup(this, group));
                    }
                }
                catch (Exception e)
                {
                    AddError("hypotheses needs to be a list of hypothesis groups: {0}", e.Message);
                }
            }

            return this;
        }
    }

    public class HypothesisGroup : Configurable
    {
        static readonly NLog.Logger logger = NLog.LogManager.GetCurrentClassLogger();

        public HypothesisGroup(HypothesisFile parent, IDictionary<string, object> group)
            : base(parent)
        {
            SourceFile = parent;
            Configure(group);
        }

        public HypothesisFile SourceFile { get; }
        public IList<HypothesisEntry> Entries { get; protected set; }

        public override NLog.Logger Log => logger;

        public override IConfigurable Configure(IDictionary<string, object> config)
        {
            base.Configure(config);

            Entries = new List<HypothesisEntry>();
            var entries = config.GetChildren("entries", this);
            if (entries != null)
            {
                foreach (var entry in entries)
                {
                    if (!entry.TryGetValue("input", out object inputs))
                        continue;
                    entry.TryGetValue("lemma", out object lemma);
                    entry.TryGetValue("definition", out object definition);

                    IEnumerable<object> inputEnum = inputs is string ? new object[] { inputs } : inputs as IEnumerable<object>;
                    foreach (var input in inputEnum ?? Enumerable.Empty<object>())
                    {
                        Entries.Add(new HypothesisEntry
                        {
                            SourceGroup = this,
                            Input = input.ToString(),
                            Lemma = lemma.ToString(),
                            Definition = definition.ToString(),
                        });
                    }
                }
            }

            return this;
        }
    }

    public class HypothesisEntry
    {
        public HypothesisGroup SourceGroup { get; set; }
        public string Input { get; set; }
        public string Lemma { get; set; }
        public string Definition { get; set; }

        public Encoding Encoding => SourceGroup?.SourceFile?.Encoding;
    }
}
