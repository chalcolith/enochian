using System;
using System.Collections.Generic;
using System.Dynamic;
using System.Linq;
using System.Text;

namespace Enochian
{
    public abstract class Configurable
    {
        IList<ErrorRecord> errors;

        public Configurable()
        {
        }

        public Configurable(dynamic config)
        {
            Configure(config);
        }

        public string AbsoluteFilePath { get; set; }
        public string Id { get; set; }
        public string Description { get; set; }
        public string Changes { get; set; }

        public IEnumerable<ErrorRecord> Errors
        {
            get { return errors ?? Enumerable.Empty<ErrorRecord>(); }
        }

        protected void AddError(string format, params object[] args)
        {
            AddError(0, 0, format, args);
        }

        protected void AddError(int line, int column, string format, params object[] args)
        {
            if (errors == null) errors = new List<ErrorRecord>();
            errors.Add(new ErrorRecord
            {
                AbsoluteFilePath = AbsoluteFilePath,
                ErrorLine = line,
                ErrorColumn = column,
                Message = string.Format(format, args),
            });
        }

        protected virtual void Configure(dynamic config)
        {
            Id = config.Id;
            Description = config.Description;
            Changes = config.Changes;
        }
    }

    public class ErrorRecord
    {
        public string AbsoluteFilePath { get; set; }
        public int ErrorLine { get; set; }
        public int ErrorColumn { get; set; }
        public string Message { get; set; }
    }
}
