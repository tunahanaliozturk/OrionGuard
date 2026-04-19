namespace Moongazing.OrionGuard.Extensions;

/// <summary>
/// Guards for validating application environment and configuration at startup.
/// Call these during application initialization to fail-fast on misconfiguration.
/// </summary>
public static class ConfigurationGuards
{
    /// <summary>
    /// Validates that an environment variable exists and is not empty.
    /// </summary>
    public static string AgainstMissingEnvVar(string variableName)
    {
        var value = Environment.GetEnvironmentVariable(variableName);
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException($"Required environment variable '{variableName}' is not set.");
        }
        return value;
    }

    /// <summary>
    /// Validates that an environment variable exists and meets minimum length (for secrets).
    /// </summary>
    public static string AgainstWeakEnvVar(string variableName, int minLength)
    {
        var value = AgainstMissingEnvVar(variableName);
        if (value.Length < minLength)
        {
            throw new InvalidOperationException($"Environment variable '{variableName}' must be at least {minLength} characters (likely a weak secret).");
        }
        return value;
    }

    /// <summary>
    /// Validates that a connection string from environment variable is properly formatted.
    /// Checks for key=value pairs separated by semicolons.
    /// </summary>
    public static string AgainstInvalidConnectionStringEnv(string variableName)
    {
        var value = AgainstMissingEnvVar(variableName);
        if (!value.Contains('='))
        {
            throw new InvalidOperationException($"Environment variable '{variableName}' does not appear to be a valid connection string.");
        }
        return value;
    }

    /// <summary>
    /// Validates that a certificate file exists at the specified path.
    /// </summary>
    public static void AgainstMissingCertificate(string filePath, string parameterName)
    {
        if (!System.IO.File.Exists(filePath))
        {
            throw new InvalidOperationException($"Certificate file not found at '{filePath}' ({parameterName}).");
        }
    }

    /// <summary>
    /// Validates that a URL environment variable is a valid HTTPS URL.
    /// Use this for API endpoints, webhook URLs, etc.
    /// </summary>
    public static Uri AgainstInsecureUrl(string variableName)
    {
        var value = AgainstMissingEnvVar(variableName);
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri))
        {
            throw new InvalidOperationException($"Environment variable '{variableName}' is not a valid URL.");
        }
        if (uri.Scheme != Uri.UriSchemeHttps)
        {
            throw new InvalidOperationException($"Environment variable '{variableName}' must use HTTPS, not {uri.Scheme}.");
        }
        return uri;
    }

    /// <summary>
    /// Validates that a port number from environment variable is valid (1-65535).
    /// </summary>
    public static int AgainstInvalidPort(string variableName)
    {
        var value = AgainstMissingEnvVar(variableName);
        if (!int.TryParse(value, out var port) || port < 1 || port > 65535)
        {
            throw new InvalidOperationException($"Environment variable '{variableName}' must be a valid port number (1-65535). Got: '{value}'.");
        }
        return port;
    }

    /// <summary>
    /// Validates multiple required environment variables at once. Throws with all missing vars listed.
    /// </summary>
    public static void AgainstMissingEnvVars(params string[] variableNames)
    {
        var missing = new List<string>();
        foreach (var name in variableNames)
        {
            if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(name)))
            {
                missing.Add(name);
            }
        }
        if (missing.Count > 0)
        {
            throw new InvalidOperationException($"Required environment variables are not set: {string.Join(", ", missing)}");
        }
    }

    /// <summary>
    /// Validates that the application is not running in a specific environment (e.g., prevent debug config in production).
    /// </summary>
    public static void AgainstEnvironment(string forbiddenEnvironment)
    {
        var current = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT")
                   ?? Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT");
        if (string.Equals(current, forbiddenEnvironment, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"This operation is not allowed in the '{forbiddenEnvironment}' environment.");
        }
    }

    /// <summary>
    /// Validates that the app is running in one of the allowed environments.
    /// </summary>
    public static void AgainstUnexpectedEnvironment(params string[] allowedEnvironments)
    {
        var current = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT")
                   ?? Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT")
                   ?? "Production";
        bool allowed = false;
        for (int i = 0; i < allowedEnvironments.Length; i++)
        {
            if (string.Equals(current, allowedEnvironments[i], StringComparison.OrdinalIgnoreCase))
            {
                allowed = true;
                break;
            }
        }
        if (!allowed)
        {
            throw new InvalidOperationException($"Unexpected environment '{current}'. Allowed: {string.Join(", ", allowedEnvironments)}.");
        }
    }
}
