namespace NovaChat.Client.Services;

public static class VexaEnvironment
{
    private const string EnvironmentVariable = "VEXA_ENVIRONMENT";

    public static string Name
    {
        get
        {
            var value = Environment.GetEnvironmentVariable(EnvironmentVariable)?.Trim();

            return value?.ToLowerInvariant() switch
            {
                "staging" => "Staging",
                "development" => "Development",
                _ => "Development"
            };
        }
    }

    public static string ServerBaseUrl =>
        Name == "Staging"
            ? "http://localhost:5257/"
            : "http://localhost:5256/";
}
