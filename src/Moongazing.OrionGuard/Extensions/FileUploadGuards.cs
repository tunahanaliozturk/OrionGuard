using System.Buffers;
using System.Collections.Frozen;
using System.Text;

namespace Moongazing.OrionGuard.Extensions;

/// <summary>
/// Validation guards for file upload security.
/// Detects fake MIME types via magic byte inspection, enforces size limits,
/// and checks for malicious content patterns.
/// </summary>
/// <remarks>
/// The control that decides which files are accepted is an allow-list of extensions
/// (<see cref="AgainstDisallowedExtension"/>) combined with a signature check
/// (<see cref="AgainstFakeMimeType(byte[], string, string)"/>). The denylist and content checks here are
/// best-effort extra layers. Store uploads outside the web root under a generated name, and serve them with
/// <c>Content-Disposition: attachment</c> and <c>X-Content-Type-Options: nosniff</c>.
/// </remarks>
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
        ".msp", ".mst", ".sct", ".ws", ".lnk", ".jar",
        // Server-side pages and handlers, run by the server if the upload folder is web-reachable;
        // ".config" covers web.config, which reconfigures IIS for its folder.
        ".aspx", ".ashx", ".asmx", ".php", ".phtml", ".jsp", ".config",
        // Active content: rendered with script access to the origin that serves it.
        ".html", ".htm", ".svg"
    }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    private static readonly string[] ImageExtensions = [".jpg", ".jpeg", ".png", ".gif", ".bmp", ".webp", ".tiff", ".ico"];

    private static readonly byte[][] ScriptMarkers = ["<script"u8.ToArray(), "javascript:"u8.ToArray(), "<?php"u8.ToArray()];

    private static readonly SearchValues<byte> ScriptMarkerFirstBytes = SearchValues.Create("<jJ"u8);

    /// <summary>
    /// Validates that the file extension matches the actual file content (magic bytes).
    /// Prevents attackers from uploading executables disguised as images.
    /// </summary>
    /// <remarks>
    /// Only extensions with a known signature are checked (.jpg, .jpeg, .png, .gif, .pdf, .zip, .docx, .xlsx,
    /// .exe, .dll, .bmp, .webp, .mp3, .mp4, .svg). Any other extension (.txt, .csv, .html, ...) passes
    /// unchecked, because there is no signature to compare against; decide which extensions are accepted with
    /// <see cref="AgainstDisallowedExtension"/>. A matching signature says nothing about active content: an
    /// SVG that runs script still starts with <c>&lt;svg</c> (see <see cref="AgainstMaliciousContent(byte[], string, string)"/>).
    /// </remarks>
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
    /// Validates that the file extension is not in the dangerous list: executables and scripts, server-side
    /// pages and handlers (.aspx, .ashx, .asmx, .php, .phtml, .jsp, .config including web.config), active
    /// content (.html, .htm, .svg), and launchers (.lnk, .jar).
    /// </summary>
    /// <remarks>
    /// The extension is read the way Windows stores the name: trailing dots and spaces are removed
    /// (<c>evil.exe.</c> and <c>"evil.exe "</c> are saved as <c>evil.exe</c>), and a name containing <c>:</c>
    /// is rejected (<c>evil.exe::$DATA</c> writes an NTFS alternate data stream). This is a denylist; prefer
    /// <see cref="AgainstDisallowedExtension"/> as the control.
    /// </remarks>
    /// <param name="fileName">The file name to validate.</param>
    /// <param name="parameterName">Parameter name for error messages.</param>
    public static void AgainstDangerousFileExtension(this string fileName, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            throw new ArgumentException($"{parameterName} file name cannot be empty.", parameterName);

        var extension = GetStoredExtension(fileName, parameterName);
        if (DangerousExtensions.Contains(extension))
            throw new ArgumentException($"{parameterName} has a dangerous file extension '{extension}'.", parameterName);
    }

    /// <summary>
    /// Validates that the file extension is in the allowed list.
    /// </summary>
    /// <remarks>
    /// The extension is read the way Windows stores the name: trailing dots and spaces are removed, and a name
    /// containing <c>:</c> is rejected, so <c>evil.exe:.jpg</c> (an alternate data stream of
    /// <c>evil.exe</c>) cannot pass as a <c>.jpg</c>.
    /// </remarks>
    /// <param name="fileName">The file name to validate.</param>
    /// <param name="allowedExtensions">Array of allowed extensions (e.g., ".jpg", ".png").</param>
    /// <param name="parameterName">Parameter name for error messages.</param>
    public static void AgainstDisallowedExtension(this string fileName, string[] allowedExtensions, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            throw new ArgumentException($"{parameterName} file name cannot be empty.", parameterName);

        var extension = GetStoredExtension(fileName, parameterName);
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
    /// Best-effort check that an upload claimed as an image or SVG carries no active content. Every SVG is
    /// rejected: SVG is active content by design and can run script through elements, event-handler
    /// attributes and links. An image (.jpg, .jpeg, .png, .gif, .bmp, .webp, .tiff, .ico) is rejected when it
    /// starts with a PE (<c>MZ</c>) header or contains <c>&lt;script</c>, <c>javascript:</c> or
    /// <c>&lt;?php</c> (ASCII, case-insensitive) anywhere in the content. Other extensions are not inspected.
    /// </summary>
    /// <remarks>
    /// This is a denylist. It does not detect Office macros, polyglot files, or script in other encodings.
    /// </remarks>
    /// <param name="fileBytes">The whole file content; all of it is scanned.</param>
    /// <param name="claimedExtension">The file extension claimed (e.g., ".jpg").</param>
    /// <param name="parameterName">Parameter name for error messages.</param>
    public static void AgainstMaliciousContent(this byte[] fileBytes, string claimedExtension, string parameterName)
    {
        if (!RejectOrNeedsScan(claimedExtension, parameterName)) return;
        ArgumentNullException.ThrowIfNull(fileBytes);

        // Check for MZ header (exe/dll) embedded in image
        if (fileBytes.Length > 2 && fileBytes[0] == 0x4D && fileBytes[1] == 0x5A)
            throw new ArgumentException($"{parameterName} contains an embedded executable.", parameterName);

        if (ContainsScriptMarker(fileBytes))
            throw new ArgumentException($"{parameterName} contains embedded script content.", parameterName);
    }

    /// <summary>
    /// Applies <see cref="AgainstMaliciousContent(byte[], string, string)"/> to the whole stream. A stream
    /// longer than <paramref name="maxScanBytes"/> is rejected rather than scanned in part, because a marker
    /// placed past the scanned prefix would otherwise pass. Streams of uninspected types are not read.
    /// The position of a seekable stream is restored; a non-seekable stream is consumed.
    /// </summary>
    /// <param name="fileStream">The upload content.</param>
    /// <param name="claimedExtension">The file extension claimed (e.g., ".jpg").</param>
    /// <param name="parameterName">Parameter name for error messages.</param>
    /// <param name="maxScanBytes">The largest stream that is scanned; set it to your upload size limit.</param>
    public static void AgainstMaliciousContent(this Stream fileStream, string claimedExtension, string parameterName, long maxScanBytes)
    {
        ArgumentNullException.ThrowIfNull(fileStream);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxScanBytes);
        if (!RejectOrNeedsScan(claimedExtension, parameterName)) return;

        var originalPosition = fileStream.CanSeek ? fileStream.Position : -1;

        // Buffers the whole stream, bounded by maxScanBytes. Scanning in overlapping chunks would keep memory
        // flat, at the cost of carrying markers across chunk boundaries.
        using var content = new MemoryStream();
        var chunk = new byte[81920];
        try
        {
            // An earlier reader (a header sniffer, another validator) may have advanced the stream; a marker
            // or an MZ header before the current position must still be scanned.
            if (fileStream.CanSeek) fileStream.Position = 0;

            int read;
            while ((read = fileStream.Read(chunk, 0, chunk.Length)) > 0)
            {
                if (content.Length + read > maxScanBytes)
                    throw new ArgumentException($"{parameterName} is larger than the {maxScanBytes}-byte scan limit.", parameterName);
                content.Write(chunk, 0, read);
            }
        }
        finally
        {
            if (fileStream.CanSeek) fileStream.Position = originalPosition;
        }

        content.ToArray().AgainstMaliciousContent(claimedExtension, parameterName);
    }

    /// <summary>
    /// Rejects SVG outright and reports whether the claimed type is an image whose content must be scanned.
    /// </summary>
    private static bool RejectOrNeedsScan(string claimedExtension, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(claimedExtension)) return false;
        if (!claimedExtension.StartsWith('.')) claimedExtension = "." + claimedExtension;

        if (string.Equals(claimedExtension, ".svg", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException($"{parameterName} is an SVG, which can carry script; sanitize it before accepting it.", parameterName);

        return Array.Exists(ImageExtensions, image => string.Equals(claimedExtension, image, StringComparison.OrdinalIgnoreCase));
    }

    private static bool ContainsScriptMarker(ReadOnlySpan<byte> content)
    {
        int offset;
        while ((offset = content.IndexOfAny(ScriptMarkerFirstBytes)) >= 0)
        {
            content = content[offset..];
            foreach (var marker in ScriptMarkers)
            {
                if (content.Length >= marker.Length && Ascii.EqualsIgnoreCase(content[..marker.Length], marker))
                    return true;
            }
            content = content[1..];
        }
        return false;
    }

    /// <summary>
    /// The extension as Windows stores the name: directories dropped, trailing dots and spaces removed.
    /// A ':' is rejected because it addresses an NTFS alternate data stream (or a drive).
    /// </summary>
    private static string GetStoredExtension(string fileName, string parameterName)
    {
        var name = fileName.AsSpan(fileName.AsSpan().LastIndexOfAny('/', '\\') + 1).TrimEnd(". ");
        if (name.Contains(':'))
            throw new ArgumentException($"{parameterName} contains ':', which addresses an alternate data stream.", parameterName);
        return Path.GetExtension(name).ToString();
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
