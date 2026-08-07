namespace Chater.Services;

public sealed class AppPaths
{
    public const string ApplicationName = "Chater";

    public AppPaths(string applicationDataDirectory, string? logsDirectory = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(applicationDataDirectory);
        ApplicationDataDirectory = applicationDataDirectory;
        LogsDirectory = logsDirectory is null
            ? Path.Combine(applicationDataDirectory, "logs")
            : Path.GetFullPath(logsDirectory);
    }

    public string ApplicationDataDirectory { get; }

    public string DatabasePath => Path.Combine(ApplicationDataDirectory, "chater.db");

    public string LogsDirectory { get; }

    public string ExportsDirectory => Path.Combine(ApplicationDataDirectory, "exports");

    public static AppPaths CreateDefault()
    {
        var root = OperatingSystem.IsMacOS()
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "Application Support")
            : Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        return new AppPaths(
            Path.Combine(root, ApplicationName),
            Path.Combine(AppContext.BaseDirectory, "logs"));
    }

    public void EnsureCreated()
    {
        Directory.CreateDirectory(ApplicationDataDirectory);
        Directory.CreateDirectory(LogsDirectory);
        Directory.CreateDirectory(ExportsDirectory);
    }
}
