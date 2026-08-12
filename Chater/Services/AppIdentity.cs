namespace Chater.Services;

/// <summary>
/// Separates local development instances from distributed application instances.
/// This prevents their operating-system permissions, startup registrations, and
/// user data from sharing an identity.
/// </summary>
public static class AppIdentity
{
#if DEBUG
    public const string ApplicationName = "Chater-dev";
    public const string MacBundleIdentifier = "com.chater.app.dev";
#else
    public const string ApplicationName = "Chater";
    public const string MacBundleIdentifier = "com.chater.app";
#endif
}
