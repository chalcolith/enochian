using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Enochian.UnitTests
{
    static class AssertUtils
    {
        public static void WithErrors(Action<IList<string>> act, Action assert, string expectedError = null)
        {
            WithErrors(null, act, assert, expectedError);
        }

        public static void WithErrors(Action arrange, Action<IList<string>> act, Action assert, string expectedError = null)
        {
            arrange?.Invoke();
            var errors = new List<string>();
            act?.Invoke(errors);

            if (string.IsNullOrWhiteSpace(expectedError))
            {
                if (errors.Any())
                    throw new AssertFailedException(string.Format("errors: {0}", string.Join(", ", errors)));
                assert?.Invoke();
            }
            else
            {
                var found = errors.Any(e => e.Contains(expectedError));
                Assert.IsTrue(found,
                    string.Format("did not find expected error {0}: {1}",
                        expectedError, string.Join(", ", errors)));
            }
        }

        public static void SequenceEquals<T>(IEnumerable<T> expected, IEnumerable<T> actual)
        {
            if (expected == null) throw new ArgumentNullException(nameof(expected));
            if (actual == null) throw new ArgumentNullException(nameof(actual));

            if (!expected.SequenceEqual(actual))
                throw new AssertFailedException("sequences are not equal");
        }
    }
}
