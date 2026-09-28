using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace NovaChat.Server.Data;

public sealed class AppDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var current = Directory.GetCurrentDirectory();
        var projectDirectory = File.Exists(Path.Combine(current, "NovaChat.Server.csproj"))
            ? current : Path.Combine(current, "NovaChat.Server");
        if (!Directory.Exists(projectDirectory)) projectDirectory = AppContext.BaseDirectory;
        var environment = Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT")
            ?? Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Production";
        var configuration = new ConfigurationBuilder()
            .SetBasePath(projectDirectory)
            .AddJsonFile("appsettings.json", optional: true)
            .AddJsonFile($"appsettings.{environment}.json", optional: true)
            .AddEnvironmentVariables()
            .AddCommandLine(args)
            .Build();
        var options = new DbContextOptionsBuilder<AppDbContext>();
        options.UseVexaDatabase(configuration);
        return new AppDbContext(options.Options);
    }
}
