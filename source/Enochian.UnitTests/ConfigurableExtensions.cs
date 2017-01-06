using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;

namespace Enochian.UnitTests
{
    static class ConfigurableExtensions
    {
        public static void ConfigWith<T>(this T value, dynamic config)
            where T : Configurable
        {
            var configure = typeof(T).GetTypeInfo().DeclaredMethods
                .FirstOrDefault(mi =>
                {
                    var parms = mi.GetParameters();
                    return mi.Name == "Configure" && parms.Length == 1
                        && typeof(object).GetTypeInfo().IsAssignableFrom(parms.Single().ParameterType);
                });
            if (configure == null)
                throw new Exception(string.Format("Unable to find Configure method for type {0}", typeof(T).FullName));
            configure.Invoke(value, new object[] { config });
        }
    }
}
