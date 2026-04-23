using System.Runtime.Versioning;
using System.Security.Principal;

namespace TraceHunter.Capture;

[SupportedOSPlatform("windows")]
public sealed class PrivilegeProbe : IPrivilegeProbe
{
    public bool IsElevated()
    {
        using var identity = WindowsIdentity.GetCurrent();
        var principal = new WindowsPrincipal(identity);
        return principal.IsInRole(WindowsBuiltInRole.Administrator);
    }
}
