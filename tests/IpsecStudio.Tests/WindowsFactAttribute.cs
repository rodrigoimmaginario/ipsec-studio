using Xunit;
namespace IpsecStudio.Tests;

public sealed class WindowsFactAttribute : FactAttribute {
    public WindowsFactAttribute() {
        if (!OperatingSystem.IsWindows()) Skip = "Requires Windows PowerShell; run by the Windows local gate.";
    }
}
