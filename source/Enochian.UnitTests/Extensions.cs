using System;
using System.Collections.Generic;
using System.Dynamic;
using System.Linq;
using System.Reflection;
using System.Text;

namespace Enochian.UnitTests
{
    static class Extensions
    {
        public static ExpandoObject Init(this ExpandoObject expando, object obj)
        {
            var dict = expando as IDictionary<string, object>;
            var props = obj.GetType().GetTypeInfo()
                .GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.FlattenHierarchy)
                .Where(prop => prop.CanRead);
            foreach (var prop in props)
            {
                dict[prop.Name] = prop.GetValue(obj, null);
            }
            return expando;
        }

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
