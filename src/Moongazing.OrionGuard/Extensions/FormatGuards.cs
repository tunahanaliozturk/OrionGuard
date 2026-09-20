using System.Buffers;
using System.Collections.Frozen;
using System.Globalization;

namespace Moongazing.OrionGuard.Extensions;

/// <summary>
/// Universal format validation guards for common data formats used in any application.
/// Covers geographic coordinates, network addresses, identifiers, and international standards.
/// </summary>
public static class FormatGuards
{
    #region Geographic Coordinates

    /// <summary>
    /// Validates that a value is a valid latitude (-90 to 90).
    /// </summary>
    public static void AgainstInvalidLatitude(this double value, string parameterName)
    {
        if (double.IsNaN(value) || double.IsInfinity(value) || value < -90.0 || value > 90.0)
        {
            throw new ArgumentOutOfRangeException(parameterName, value,
                $"{parameterName} must be a valid latitude between -90 and 90.");
        }
    }

    /// <summary>
    /// Validates that a value is a valid longitude (-180 to 180).
    /// </summary>
    public static void AgainstInvalidLongitude(this double value, string parameterName)
    {
        if (double.IsNaN(value) || double.IsInfinity(value) || value < -180.0 || value > 180.0)
        {
            throw new ArgumentOutOfRangeException(parameterName, value,
                $"{parameterName} must be a valid longitude between -180 and 180.");
        }
    }

    /// <summary>
    /// Validates that latitude and longitude form a valid coordinate pair.
    /// </summary>
    public static void AgainstInvalidCoordinates(double latitude, double longitude, string parameterName)
    {
        latitude.AgainstInvalidLatitude(parameterName);
        longitude.AgainstInvalidLongitude(parameterName);
    }

    #endregion

    #region Network Formats

    /// <summary>
    /// Validates that a string is a valid MAC address (e.g., "00:1A:2B:3C:4D:5E" or "00-1A-2B-3C-4D-5E").
    /// </summary>
    public static void AgainstInvalidMacAddress(this string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException($"{parameterName} cannot be empty.", parameterName);
        }

        var cleaned = value.Replace(":", "").Replace("-", "").Replace(".", "");

        if (cleaned.Length != 12)
        {
            throw new ArgumentException($"{parameterName} is not a valid MAC address.", parameterName);
        }

        foreach (var c in cleaned)
        {
            if (!IsHexChar(c))
            {
                throw new ArgumentException($"{parameterName} is not a valid MAC address.", parameterName);
            }
        }
    }

    /// <summary>
    /// Validates that a string is a valid hostname (RFC 1123): ASCII letters, digits and hyphens only.
    /// Internationalized names must be passed in their ASCII (punycode, <c>xn--</c>) form, which also keeps
    /// look-alike Unicode hosts (Cyrillic <c>а</c> for Latin <c>a</c>) out.
    /// </summary>
    public static void AgainstInvalidHostname(this string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 253)
        {
            throw new ArgumentException($"{parameterName} is not a valid hostname.", parameterName);
        }

        var labels = value.Split('.');
        foreach (var label in labels)
        {
            if (label.Length == 0 || label.Length > 63)
            {
                throw new ArgumentException($"{parameterName} is not a valid hostname.", parameterName);
            }

            if (label.StartsWith('-') || label.EndsWith('-'))
            {
                throw new ArgumentException($"{parameterName} is not a valid hostname.", parameterName);
            }

            foreach (var c in label)
            {
                if (!char.IsAsciiLetterOrDigit(c) && c != '-')
                {
                    throw new ArgumentException($"{parameterName} is not a valid hostname.", parameterName);
                }
            }
        }
    }

    /// <summary>
    /// Validates that a string is a valid CIDR notation (e.g., "192.168.1.0/24" or "10.0.0.0/8").
    /// </summary>
    public static void AgainstInvalidCidr(this string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException($"{parameterName} cannot be empty.", parameterName);
        }

        var parts = value.Split('/');
        if (parts.Length != 2)
        {
            throw new ArgumentException($"{parameterName} is not a valid CIDR notation.", parameterName);
        }

        if (!System.Net.IPAddress.TryParse(parts[0], out var ip))
        {
            throw new ArgumentException($"{parameterName} is not a valid CIDR notation.", parameterName);
        }

        if (!int.TryParse(parts[1], CultureInfo.InvariantCulture, out int prefix))
        {
            throw new ArgumentException($"{parameterName} is not a valid CIDR notation.", parameterName);
        }

        int maxPrefix = ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6 ? 128 : 32;
        if (prefix < 0 || prefix > maxPrefix)
        {
            throw new ArgumentException($"{parameterName} is not a valid CIDR notation.", parameterName);
        }
    }

    #endregion

    #region International Standards

    private static readonly FrozenSet<string> Iso3166Alpha2 = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "AD","AE","AF","AG","AI","AL","AM","AO","AQ","AR","AS","AT","AU","AW","AX","AZ",
        "BA","BB","BD","BE","BF","BG","BH","BI","BJ","BL","BM","BN","BO","BQ","BR","BS","BT","BV","BW","BY","BZ",
        "CA","CC","CD","CF","CG","CH","CI","CK","CL","CM","CN","CO","CR","CU","CV","CW","CX","CY","CZ",
        "DE","DJ","DK","DM","DO","DZ",
        "EC","EE","EG","EH","ER","ES","ET",
        "FI","FJ","FK","FM","FO","FR",
        "GA","GB","GD","GE","GF","GG","GH","GI","GL","GM","GN","GP","GQ","GR","GS","GT","GU","GW","GY",
        "HK","HM","HN","HR","HT","HU",
        "ID","IE","IL","IM","IN","IO","IQ","IR","IS","IT",
        "JE","JM","JO","JP",
        "KE","KG","KH","KI","KM","KN","KP","KR","KW","KY","KZ",
        "LA","LB","LC","LI","LK","LR","LS","LT","LU","LV","LY",
        "MA","MC","MD","ME","MF","MG","MH","MK","ML","MM","MN","MO","MP","MQ","MR","MS","MT","MU","MV","MW","MX","MY","MZ",
        "NA","NC","NE","NF","NG","NI","NL","NO","NP","NR","NU","NZ",
        "OM",
        "PA","PE","PF","PG","PH","PK","PL","PM","PN","PR","PS","PT","PW","PY",
        "QA",
        "RE","RO","RS","RU","RW",
        "SA","SB","SC","SD","SE","SG","SH","SI","SJ","SK","SL","SM","SN","SO","SR","SS","ST","SV","SX","SY","SZ",
        "TC","TD","TF","TG","TH","TJ","TK","TL","TM","TN","TO","TR","TT","TV","TW","TZ",
        "UA","UG","UM","US","UY","UZ",
        "VA","VC","VE","VG","VI","VN","VU",
        "WF","WS",
        "YE","YT",
        "ZA","ZM","ZW"
    }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Validates that a string is a valid ISO 3166-1 alpha-2 country code (e.g., "US", "TR", "DE").
    /// </summary>
    public static void AgainstInvalidCountryCode(this string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value) || !Iso3166Alpha2.Contains(value.Trim()))
        {
            throw new ArgumentException($"{parameterName} is not a valid ISO 3166-1 alpha-2 country code.", parameterName);
        }
    }

    /// <summary>
    /// Validates that a string is a valid IANA time zone identifier (e.g., "America/New_York", "Europe/Istanbul").
    /// </summary>
    public static void AgainstInvalidTimeZoneId(this string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException($"{parameterName} cannot be empty.", parameterName);
        }

        try
        {
            TimeZoneInfo.FindSystemTimeZoneById(value);
        }
        catch (Exception)
        {
            // The lookup is not limited to TimeZoneNotFoundException: on Windows, an id such as
            // "Pacific Standard Time\Dynamic DST" reaches a registry subkey and throws
            // InvalidTimeZoneException or NullReferenceException. Every failure means "not a valid id".
            throw new ArgumentException($"{parameterName} is not a valid time zone identifier.", parameterName);
        }
    }

    /// <summary>
    /// Validates that a string is a valid BCP 47 / IETF language tag (e.g., "en", "en-US", "tr-TR").
    /// </summary>
    public static void AgainstInvalidLanguageTag(this string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException($"{parameterName} cannot be empty.", parameterName);
        }

        try
        {
            var culture = CultureInfo.GetCultureInfo(value);
            if (culture.LCID == 4096 && !value.Equals("und", StringComparison.OrdinalIgnoreCase))
            {
                // LCID 4096 = custom culture, only allow if it has a valid name
                if (string.IsNullOrEmpty(culture.Name))
                {
                    throw new ArgumentException($"{parameterName} is not a valid language tag.", parameterName);
                }
            }
        }
        catch (CultureNotFoundException)
        {
            throw new ArgumentException($"{parameterName} is not a valid language tag.", parameterName);
        }
    }

    #endregion

    #region Token & Identifier Formats

    /// <summary>
    /// Validates that a string has a valid JWT structure (three Base64URL-encoded segments separated by dots).
    /// Does not verify the signature or claims.
    /// </summary>
    public static void AgainstInvalidJwtFormat(this string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException($"{parameterName} cannot be empty.", parameterName);
        }

        var parts = value.Split('.');
        if (parts.Length != 3)
        {
            throw new ArgumentException($"{parameterName} is not a valid JWT (expected 3 segments).", parameterName);
        }

        // Each segment must be valid Base64URL
        for (int i = 0; i < parts.Length; i++)
        {
            var part = parts[i];
            if (part.Length == 0 && i != 2) // Only the signature may be empty (unsecured JWT)
            {
                throw new ArgumentException($"{parameterName} is not a valid JWT.", parameterName);
            }

            foreach (var c in part)
            {
                if (!char.IsAsciiLetterOrDigit(c) && c != '-' && c != '_' && c != '=')
                {
                    throw new ArgumentException($"{parameterName} is not a valid JWT.", parameterName);
                }
            }
        }
    }

    // Keys used by the ADO.NET providers for SQL Server, PostgreSQL, MySQL, SQLite and Oracle, by OLE DB and
    // ODBC, and by Azure Storage, Service Bus, Event Hubs and App Configuration connection strings.
    private static readonly FrozenSet<string> RecognizedConnectionStringKeys = new[]
    {
        "Server", "Data Source", "DataSource", "Host", "Hostname", "Address", "Addr", "Network Address",
        "Port", "Database", "Initial Catalog", "Dbq", "Filename", "Mode",
        "User ID", "UserID", "User", "Uid", "Username", "User Name", "Password", "Pwd",
        "Integrated Security", "Trusted_Connection", "Persist Security Info", "Encrypt",
        "TrustServerCertificate", "Trust Server Certificate", "SSL Mode", "SslMode", "Connection Timeout",
        "Connect Timeout", "Timeout", "Command Timeout", "Pooling", "Min Pool Size", "Max Pool Size", "Application Name", "ApplicationIntent",
        "MultipleActiveResultSets", "AttachDbFilename", "Search Path", "Provider", "Driver", "Dsn",
        "DefaultEndpointsProtocol", "AccountName", "AccountKey", "EndpointSuffix", "BlobEndpoint",
        "QueueEndpoint", "TableEndpoint", "FileEndpoint", "SharedAccessSignature", "Endpoint", "UseDevelopmentStorage",
        "SharedAccessKeyName", "SharedAccessKey", "EntityPath", "Id", "Secret",
    }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Validates that a string is a valid connection string: at least one <c>key=value</c> pair whose key is
    /// recognized by a common ADO.NET provider (SQL Server, PostgreSQL, MySQL, SQLite, Oracle), OLE DB/ODBC,
    /// or an Azure SDK connection string (Storage, Service Bus, Event Hubs, App Configuration). Keys compare
    /// case-insensitively. URI-style strings (<c>mongodb://...</c>) and Redis configuration strings
    /// (<c>host:6379,password=...</c>) are not <c>key=value;</c> connection strings and are rejected.
    /// </summary>
    public static void AgainstInvalidConnectionString(this string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException($"{parameterName} cannot be empty.", parameterName);
        }

        // Connection strings must contain at least one semicolon-separated key=value pair
        if (!value.Contains('='))
        {
            throw new ArgumentException($"{parameterName} is not a valid connection string.", parameterName);
        }

        var pairs = value.Split(';', StringSplitOptions.RemoveEmptyEntries);
        bool hasValidPair = false;
        foreach (var pair in pairs)
        {
            var eqIndex = pair.IndexOf('=');
            if (eqIndex > 0 && eqIndex < pair.Length - 1)
            {
                var key = pair[..eqIndex].Trim();
                if (RecognizedConnectionStringKeys.Contains(key))
                {
                    hasValidPair = true;
                    break;
                }
            }
        }

        if (!hasValidPair)
        {
            throw new ArgumentException($"{parameterName} is not a valid connection string.", parameterName);
        }
    }

    private static readonly SearchValues<char> Base64Alphabet =
        SearchValues.Create("ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+/=");

    /// <summary>
    /// Validates that a string is a valid Base64-encoded value: ASCII Base64 alphabet only, length a multiple
    /// of 4, and padding that <see cref="Convert.FromBase64String(string)"/> accepts. Surrounding whitespace
    /// is ignored; whitespace inside the value is not.
    /// </summary>
    public static void AgainstInvalidBase64String(this string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException($"{parameterName} cannot be empty.", parameterName);
        }

        var span = value.AsSpan().Trim();
        if (span.Length % 4 != 0 ||
            span.ContainsAnyExcept(Base64Alphabet) ||
            !Convert.TryFromBase64Chars(span, new byte[span.Length / 4 * 3], out _))
        {
            throw new ArgumentException($"{parameterName} is not a valid Base64 string.", parameterName);
        }
    }

    #endregion

    #region Private Helpers

    private static bool IsHexChar(char c)
    {
        return c is (>= '0' and <= '9') or (>= 'a' and <= 'f') or (>= 'A' and <= 'F');
    }

    #endregion
}
