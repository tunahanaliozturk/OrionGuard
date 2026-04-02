namespace Moongazing.OrionGuard.Extensions;

public static class FileGuards
{
    public static void AgainstNonExistentFile(this string filePath, string parameterName)
    {
        if (!System.IO.File.Exists(filePath))
        {
            throw new ArgumentException($"{parameterName} does not exist.", parameterName);
        }
    }

    public static void AgainstInvalidFileExtension(this string filePath, string[] validExtensions, string parameterName)
    {
        var fileExtension = System.IO.Path.GetExtension(filePath);
        if (!validExtensions.Contains(fileExtension, StringComparer.OrdinalIgnoreCase))
        {
            throw new ArgumentException($"{parameterName} must have one of the following extensions: {string.Join(", ", validExtensions)}.", parameterName);
        }
    }

    public static void AgainstEmptyFile(this string filePath, string parameterName)
    {
        var fileInfo = new System.IO.FileInfo(filePath);
        if (fileInfo.Length == 0)
        {
            throw new ArgumentException($"{parameterName} cannot be an empty file.", parameterName);
        }
    }
}
