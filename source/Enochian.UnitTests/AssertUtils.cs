using System.Globalization;

namespace Enochian.UnitTests;

public static class AssertUtils
{
    public static void NoErrors(IConfigurable obj)
    {
        if (obj.Errors != null)
        {
            var message = string.Join(", ", obj.Errors.Select(er => er.Message));
            if (!string.IsNullOrWhiteSpace(message))
            {
                Assert.Fail(message);
            }
        }
    }

    public static void WithErrors(Action<IList<string>> act, Action assert, string? expectedError = null)
    {
        WithErrors(null, act, assert, expectedError);
    }

    public static void WithErrors(Action? arrange, Action<IList<string>> act, Action assert, string? expectedError = null)
    {
        arrange?.Invoke();
        var errors = new List<string>();
        act?.Invoke(errors);

        if (string.IsNullOrWhiteSpace(expectedError))
        {
            if (errors.Count != 0)
            {
                throw new AssertFailedException(string.Format(CultureInfo.InvariantCulture, "errors: {0}", string.Join(", ", errors)));
            }

            assert?.Invoke();
        }
        else
        {
            var found = errors.Any(e => e.Contains(expectedError));
            Assert.IsTrue(found,
                string.Format(CultureInfo.InvariantCulture, "did not find expected error {0}: {1}",
                    expectedError, string.Join(", ", errors)));
        }
    }

    public static void SequenceEquals<T>(IEnumerable<T> expected, IEnumerable<T> actual)
    {
        ArgumentNullException.ThrowIfNull(expected);

        ArgumentNullException.ThrowIfNull(actual);

        if (!expected.SequenceEqual(actual))
        {
            throw new AssertFailedException("sequences are not equal");
        }
    }
}
