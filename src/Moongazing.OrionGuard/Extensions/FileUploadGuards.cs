using System.Collections.Frozen;

namespace Moongazing.OrionGuard.Extensions;

/// <summary>
/// Validation guards for file upload security.
/// Detects fake MIME types via magic byte inspection, enforces size limits,
/// and checks for malicious content patterns.
/// </summary>
public static class FileUploadGuards
{
    // Magic bytes for common file types (first N bytes of the file)
    private static readonly FrozenDictionary<string, byte[][]> MagicBytes = new Dictionary<string, byte[][]>
    {
        [".jpg"] = new[] { new byte[] { 0xFF, 0xD8, 0xFF } },
        [".jpeg"] = new[] { new byte[] { 0xFF, 0xD8, 0xFF } },
        [".png"] = new[] { new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A } },
        [".gif"] = new[] { new byte[] { 0x47, 0x49, 0x46, 0x38 } },
        [".pdf"] = new[] { new byte[] { 0x25, 0x50, 0x44, 0x46 } },
        [".zip"] = new[] { new byte[] { 0x50, 0x4B, 0x03, 0x04 }, new byte[] { 0x50, 0x4B, 0x05, 0x06 } },
        [".docx"] = new[] { new byte[] { 0x50, 0x4B, 0x03, 0x04 } },
        [".xlsx"] = new[] { new byte[] { 0x50, 0x4B, 0x03, 0x04 } },
        [".exe"] = new[] { new byte[] { 0x4D, 0x5A } },
        [".dll"] = new[] { new byte[] { 0x4D, 0x5A } },
        [".bmp"] = new[] { new byte[] { 0x42, 0x4D } },
        [".webp"] = new[] { new byte[] { 0x52, 0x49, 0x46, 0x46 } },
        [".mp3"] = new[] { new byte[] { 0x49, 0x44, 0x33 }, new byte[] { 0xFF, 0xFB } },
        [".mp4"] = new[] { new byte[] { 0x00, 0x00, 0x00, 0x18, 0x66, 0x74, 0x79, 0x70 }, new byte[] { 0x00, 0x00, 0x00, 0x20, 0x66, 0x74, 0x79, 0x70 } },
        [".svg"] = new[] { System.Text.Encoding.UTF8.GetBytes("<?xml"), System.Text.Encoding.UTF8.GetBytes("<svg") },
    }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    // Dangerous file extensions that should never be uploaded
    private static readonly FrozenSet<string> DangerousExtensions = new HashSet<string>
    {
        ".exe", ".dll", ".bat", ".cmd", ".com", ".msi", ".scr", ".pif",
        ".vbs", ".vbe", ".js", ".jse", ".wsf", ".wsh", ".ps1", ".psm1",
        ".sh", ".bash", ".csh", ".ksh", ".reg", ".inf", ".hta", ".cpl",
        ".msp", ".mst", ".sct", ".ws"
    }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Validates that the file extension matches the actual file content (magic bytes).
    /// Prevents attackers from uploading executables disguised as images.
    /// </summary>
    /// <param name="fileBytes">The file content as byte array or first 16+ bytes.</param>
    /// <param name="claimedExtension">The file extension claimed (e.g., ".jpg").</param>
    /// <param name="parameterName">Parameter name for error messages.</param>
    public static void AgainstFakeMimeType(this byte[] fileBytes, string claimedExtension, string parameterName)
    {
        ArgumentNullException.ThrowIfNull(fileBytes);
        if (string.IsNullOrWhiteSpace(claimedExtension))
            throw new ArgumentException($"{parameterName} extension cannot be empty.", parameterName);

        if (!claimedExtension.StartsWith('.'))
            claimedExtension = "." + claimedExtension;

        if (!MagicBytes.TryGetValue(claimedExtension, out var expectedSignatures))
            return; // Unknown extension - can't verify

        bool matchesAny = false;
        foreach (var signature in expectedSignatures)
        {
            if (fileBytes.Length >= signature.Length)
            {
                bool matches = true;
                for (int i = 0; i < signature.Length; i++)
                {
                    if (fileBytes[i] != signature[i])
                    {
                        matches = false;
                        break;
                    }
                }
                if (matches) { matchesAny = true; break; }
            }
        }

        if (!matchesAny)
            throw new ArgumentException($"{parameterName} content does not match the claimed file type '{claimedExtension}'.", parameterName);
    }

    /// <summary>
    /// Validates that a Stream's content matches the claimed extension.
    /// </summary>
    /// <param name="fileStream">The file stream to validate.</param>
    /// <param name="claimedExtension">The file extension claimed (e.g., ".jpg").</param>
    /// <param name="parameterName">Parameter name for error messages.</param>
    public static void AgainstFakeMimeType(this Stream fileStream, string claimedExtension, string parameterName)
    {
        ArgumentNullException.ThrowIfNull(fileStream);
        var buffer = new byte[16];
        var originalPosition = fileStream.CanSeek ? fileStream.Position : -1;
        var bytesRead = fileStream.Read(buffer, 0, buffer.Length);
        if (fileStream.CanSeek) fileStream.Position = originalPosition;

        var header = new byte[bytesRead];
        Array.Copy(buffer, header, bytesRead);
        header.AgainstFakeMimeType(claimedExtension, parameterName);
    }

    /// <summary>
    /// Validates that file size does not exceed the maximum allowed.
    /// </summary>
    /// <param name="fileSizeInBytes">The file size in bytes.</param>
    /// <param name="maxSizeInBytes">The maximum allowed file size in bytes.</param>
    /// <param name="parameterName">Parameter name for error messages.</param>
    public static void AgainstOversizedUpload(this long fileSizeInBytes, long maxSizeInBytes, string parameterName)
    {
        if (fileSizeInBytes <= 0)
            throw new ArgumentException($"{parameterName} file is empty.", parameterName);
        if (fileSizeInBytes > maxSizeInBytes)
            throw new ArgumentException($"{parameterName} file size ({fileSizeInBytes / 1024}KB) exceeds the maximum allowed ({maxSizeInBytes / 1024}KB).", parameterName);
    }

    /// <summary>
    /// Validates that file size does not exceed the maximum allowed.
    /// </summary>
    /// <param name="fileBytes">The file content as byte array.</param>
    /// <param name="maxSizeInBytes">The maximum allowed file size in bytes.</param>
    /// <param name="parameterName">Parameter name for error messages.</param>
    public static void AgainstOversizedUpload(this byte[] fileBytes, long maxSizeInBytes, string parameterName)
        => ((long)fileBytes.Length).AgainstOversizedUpload(maxSizeInBytes, parameterName);

    /// <summary>
    /// Validates that the file extension is not in the dangerous list.
    /// </summary>
    /// <param name="fileName">The file name to validate.</param>
    /// <param name="parameterName">Parameter name for error messages.</param>
    public static void AgainstDangerousFileExtension(this string fileName, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            throw new ArgumentException($"{parameterName} file name cannot be empty.", parameterName);

        var extension = Path.GetExtension(fileName);
        if (DangerousExtensions.Contains(extension))
            throw new ArgumentException($"{parameterName} has a dangerous file extension '{extension}'.", parameterName);
    }

    /// <summary>
    /// Validates that the file extension is in the allowed list.
    /// </summary>
    /// <param name="fileName">The file name to validate.</param>
    /// <param name="allowedExtensions">Array of allowed extensions (e.g., ".jpg", ".png").</param>
    /// <param name="parameterName">Parameter name for error messages.</param>
    public static void AgainstDisallowedExtension(this string fileName, string[] allowedExtensions, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            throw new ArgumentException($"{parameterName} file name cannot be empty.", parameterName);

        var extension = Path.GetExtension(fileName);
        bool allowed = false;
        for (int i = 0; i < allowedExtensions.Length; i++)
        {
            var ext = allowedExtensions[i].StartsWith('.') ? allowedExtensions[i] : "." + allowedExtensions[i];
            if (string.Equals(extension, ext, StringComparison.OrdinalIgnoreCase))
            {
                allowed = true;
                break;
            }
        }
        if (!allowed)
            throw new ArgumentException($"{parameterName} extension '{extension}' is not allowed. Allowed: {string.Join(", ", allowedExtensions)}.", parameterName);
    }

    /// <summary>
    /// Validates that a file does not contain potentially malicious content patterns.
    /// Checks for embedded scripts, macros, and executable signatures in non-executable files.
    /// </summary>
    /// <param name="fileBytes">The file content as byte array.</param>
    /// <param name="claimedExtension">The file extension claimed (e.g., ".jpg").</param>
    /// <param name="parameterName">Parameter name for error messages.</param>
    public static void AgainstMaliciousContent(this byte[] fileBytes, string claimedExtension, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(claimedExtension)) return;
        if (!claimedExtension.StartsWith('.')) claimedExtension = "." + claimedExtension;

        // Only check non-executable file types for embedded malicious content
        var imageExtensions = new[] { ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".webp", ".tiff", ".ico" };
        bool isImage = false;
        for (int i = 0; i < imageExtensions.Length; i++)
        {
            if (string.Equals(claimedExtension, imageExtensions[i], StringComparison.OrdinalIgnoreCase))
            { isImage = true; break; }
        }
        if (!isImage) return;

        // Check for MZ header (exe/dll) embedded in image
        if (fileBytes.Length > 2 && fileBytes[0] == 0x4D && fileBytes[1] == 0x5A)
            throw new ArgumentException($"{parameterName} contains an embedded executable.", parameterName);

        // Check for script content in image files
        var content = System.Text.Encoding.UTF8.GetString(fileBytes, 0, Math.Min(fileBytes.Length, 8192));
        if (content.Contains("<script", StringComparison.OrdinalIgnoreCase) ||
            content.Contains("javascript:", StringComparison.OrdinalIgnoreCase) ||
            content.Contains("<?php", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException($"{parameterName} contains embedded script content.", parameterName);
        }
    }

    /// <summary>
    /// Constants for common file size limits.
    /// </summary>
    public static class FileSizeLimits
    {
        public const long OneKB = 1024;
        public const long OneMB = 1024 * 1024;
        public const long FiveMB = 5 * OneMB;
        public const long TenMB = 10 * OneMB;
        public const long TwentyFiveMB = 25 * OneMB;
        public const long FiftyMB = 50 * OneMB;
        public const long OneHundredMB = 100 * OneMB;
        public const long OneGB = 1024 * OneMB;
    }
}
