using System.Reflection;

namespace Moongazing.OrionGuard.Extensions;

public static class ObjectGuards
{
    private static class PropertyCache<T>
    {
        internal static readonly PropertyInfo[] Properties = typeof(T).GetProperties();
    }

    public static void AgainstNull(this object obj, string parameterName)
    {
        if (obj == null)
        {
            throw new ArgumentNullException(parameterName, $"{parameterName} cannot be null.");
        }
    }

    public static void AgainstUninitializedProperties<T>(this T obj, string parameterName)
    {
        var properties = PropertyCache<T>.Properties;
        foreach (var property in properties)
        {
            if (property.GetValue(obj) == null)
            {
                throw new ArgumentException($"{parameterName} contains uninitialized property: {property.Name}.", parameterName);
            }
        }
    }
}
