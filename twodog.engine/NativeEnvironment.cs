using System;
using System.Runtime.InteropServices;

namespace twodog;

internal static class NativeEnvironment
{
    [DllImport("libc", SetLastError = true)]
    private static extern int setenv(string name, string value, int overwrite);

    internal static void SetVariable(string name, string value)
    {
        // On Unix, .NET's managed environment can differ from native getenv().
        if (OperatingSystem.IsAndroid() || OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
        {
            if (setenv(name, value, 1) != 0)
                throw new InvalidOperationException(
                    $"Failed to set native environment variable '{name}': {Marshal.GetLastPInvokeError()}");
        }
        else
            Environment.SetEnvironmentVariable(name, value);
    }
}
