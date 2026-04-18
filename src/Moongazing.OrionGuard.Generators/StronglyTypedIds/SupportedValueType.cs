#nullable enable

namespace Moongazing.OrionGuard.Generators.StronglyTypedIds
{
    /// <summary>
    /// Enumerates the underlying primitive types supported by the <c>[StronglyTypedId]</c>
    /// generator.
    /// </summary>
    internal enum SupportedValueType
    {
        Guid,
        Int32,
        Int64,
        String,
        Ulid
    }

    internal static class SupportedValueTypeMap
    {
        public static bool TryParse(string fullyQualifiedName, out SupportedValueType result)
        {
            switch (fullyQualifiedName)
            {
                case "System.Guid":
                    result = SupportedValueType.Guid; return true;
                case "System.Int32":
                case "int":
                    result = SupportedValueType.Int32; return true;
                case "System.Int64":
                case "long":
                    result = SupportedValueType.Int64; return true;
                case "System.String":
                case "string":
                    result = SupportedValueType.String; return true;
                case "System.Ulid":
                    result = SupportedValueType.Ulid; return true;
                default:
                    result = default;
                    return false;
            }
        }

        public static string CSharpKeyword(SupportedValueType type) => type switch
        {
            SupportedValueType.Guid => "global::System.Guid",
            SupportedValueType.Int32 => "int",
            SupportedValueType.Int64 => "long",
            SupportedValueType.String => "string",
            SupportedValueType.Ulid => "global::System.Ulid",
            _ => throw new System.ArgumentOutOfRangeException(nameof(type))
        };
    }
}
