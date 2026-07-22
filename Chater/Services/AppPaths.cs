namespace Chater.Services;

public sealed class AppPaths
{
    public const string ApplicationName = "Chater";

    public AppPaths(string applicationDataDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(applicationDataDirectory);
        ApplicationDataDirectory = applicationDataDirectory;
    }

    public string ApplicationDataDirectory { get; }

    public string DatabasePath => Path.Combine(ApplicationDataDirectory, "chater.db");

    public string LogsDirectory => Path.Combine(ApplicationDataDirectory, "logs");

    public string ExportsDirectory => Path.Combine(ApplicationDataDirectory, "exports");

    public static AppPaths CreateDefault()
    {
        var root = OperatingSystem.IsMacOS()
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "Application Support")
            : Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        return new AppPaths(Path.Combine(root, ApplicationName));
    }

    public void EnsureCreated()
    {
        Directory.CreateDirectory(ApplicationDataDirectory);
        Directory.CreateDirectory(LogsDirectory);
        Directory.CreateDirectory(ExportsDirectory);
    }
}
