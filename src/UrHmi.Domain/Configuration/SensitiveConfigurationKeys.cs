namespace UrHmi.Domain.Configuration;

public static class SensitiveConfigurationKeys
{
    public static readonly IReadOnlySet<string> Names = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "password",
        "pwd",
        "secret",
        "secrets",
        "credential",
        "credentials",
        "token",
        "accesstoken",
        "access_token",
        "refreshtoken",
        "refresh_token",
        "apikey",
        "api_key",
        "connectionstring"
    };

    public static bool IsSensitive(string propertyName) => Names.Contains(propertyName);
}
