using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Enochian.Lexicons;
using Enochian.Text;

namespace Enochian.Flow.Steps
{
    public class DTWMatcher : TextFlowStep
    {
        public DTWMatcher(IConfigurable parent, IFlowResources resources)
            : base(parent, resources)
        {
        }

        public Lexicon Lexicon { get; protected set; }

        public override IConfigurable Configure(IDictionary<string, object> config)
        {
            base.Configure(config);

            var lexicon = config.Get<string>("lexicon", this);
            if (!string.IsNullOrWhiteSpace(lexicon))
            {
                Lexicon = Resources.Lexicons.FirstOrDefault(lex => lex.Id == lexicon);
                if (Lexicon == null)
                    AddError("unable to find lexicon '{0}' for DTWMatcher '{1}'", lexicon, Id);
            }
            else
            {
                AddError("no lexicon specified for DTWMatcher '{0}'", Id);
            }

            return this;
        }

        protected override TextChunk ProcessTyped(TextChunk input)
        {
            return base.ProcessTyped(input);
        }
    }
}
