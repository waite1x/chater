using System.Runtime.InteropServices;
using System.Diagnostics;

namespace Chater.Services;

internal static partial class MacAccessibility
{
    public static bool IsTrusted()
    {
        if (!OperatingSystem.IsMacOS())
        {
            return true;
        }

        try
        {
            return AXIsProcessTrusted();
        }
        catch (DllNotFoundException)
        {
            return false;
        }
    }

    public static void OpenSettings()
    {
        if (!OperatingSystem.IsMacOS())
        {
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "open",
                Arguments = "x-apple.systempreferences:com.apple.preference.security?Privacy_Accessibility",
                UseShellExecute = false,
                CreateNoWindow = true
            });
        }
        catch
        {
            // The status message still contains the manual navigation path.
        }
    }

    [LibraryImport("/System/Library/Frameworks/ApplicationServices.framework/ApplicationServices")]
    [return: MarshalAs(UnmanagedType.I1)]
    private static partial bool AXIsProcessTrusted();
}
