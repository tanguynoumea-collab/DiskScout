using DiskScout.Helpers;
using FluentAssertions;

namespace DiskScout.Tests;

/// <summary>
/// Whitelist guard tests for <see cref="ResiduePathSafety"/>.
/// Every emitted residue finding must pass through <see cref="ResiduePathSafety.IsSafeToPropose"/>;
/// these tests pin the non-bypassable critical-path denylist.
/// </summary>
public class ResiduePathSafetyTests
{
    [Theory]
    [InlineData(@"C:\Windows\System32\drivers\etc\hosts")]
    [InlineData(@"C:\WINDOWS\System32\notepad.exe")]
    [InlineData(@"c:\windows\system32\kernel32.dll")]
    public void IsSafeToPropose_RejectsPathsUnderSystem32(string path)
    {
        ResiduePathSafety.IsSafeToPropose(path).Should().BeFalse();
    }

    [Theory]
    [InlineData(@"C:\Windows\SysWOW64\msvcrt.dll")]
    [InlineData(@"C:\Windows\WinSxS\amd64_microsoft.windows.common-controls\comctl32.dll")]
    [InlineData(@"C:\Windows\drivers\etc\hosts")]
    [InlineData(@"C:\Windows\Boot\EFI\bootmgfw.efi")]
    [InlineData(@"C:\Windows\Fonts\arial.ttf")]
    public void IsSafeToPropose_RejectsOtherWindowsCriticalDirs(string path)
    {
        ResiduePathSafety.IsSafeToPropose(path).Should().BeFalse();
    }

    [Theory]
    [InlineData(@"C:\Program Files\Windows Defender\MsMpEng.exe")]
    [InlineData(@"C:\Program Files (x86)\Windows Defender\Tools.exe")]
    [InlineData(@"C:\Program Files\Windows Security\AppX\foo.dll")]
    public void IsSafeToPropose_RejectsSecuritySoftwareDirs(string path)
    {
        ResiduePathSafety.IsSafeToPropose(path).Should().BeFalse();
    }

    [Theory]
    [InlineData(@"C:\Program Files (x86)\Common Files\microsoft shared\ink\inked.dll")]
    [InlineData(@"C:\PROGRAM FILES (X86)\COMMON FILES\foo.dll")]
    [InlineData(@"C:\Program Files\Common Files\System\ole32.dll")]
    public void IsSafeToPropose_RejectsCommonFilesSubtrees_CaseInsensitive(string path)
    {
        ResiduePathSafety.IsSafeToPropose(path).Should().BeFalse();
    }

    [Fact]
    public void IsSafeToPropose_AllowsAppDataAdobeReader()
    {
        ResiduePathSafety.IsSafeToPropose(@"C:\Users\Test\AppData\Local\AdobeReader")
            .Should().BeTrue();
    }

    [Fact]
    public void IsSafeToPropose_AllowsProgramFilesAdobeAcrobat()
    {
        ResiduePathSafety.IsSafeToPropose(@"C:\Program Files\Adobe\Acrobat DC")
            .Should().BeTrue();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void IsSafeToPropose_RejectsEmptyOrNull(string? path)
    {
        ResiduePathSafety.IsSafeToPropose(path!).Should().BeFalse();
    }

    [Fact]
    public void IsSafeToPropose_RejectsTcpipServiceRegistryKey()
    {
        ResiduePathSafety.IsSafeToPropose(@"HKLM\SYSTEM\CurrentControlSet\Services\Tcpip\Parameters")
            .Should().BeFalse();
    }

    [Fact]
    public void IsSafeToPropose_AllowsAdobeArmServiceRegistryKey()
    {
        ResiduePathSafety.IsSafeToPropose(@"HKLM\SYSTEM\CurrentControlSet\Services\AdobeARMservice")
            .Should().BeTrue();
    }

    [Theory]
    [InlineData(@"HKLM\SYSTEM\CurrentControlSet\Services\WinDefend")]
    [InlineData(@"HKLM\SOFTWARE\Microsoft\Windows Defender\Exclusions")]
    [InlineData(@"HKLM\SOFTWARE\Policies\Microsoft\Windows Defender\Settings")]
    public void IsSafeToPropose_RejectsDefenderRegistryKeys(string path)
    {
        ResiduePathSafety.IsSafeToPropose(path).Should().BeFalse();
    }

    [Theory]
    [InlineData("WinDefend")]
    [InlineData("TrustedInstaller")]
    [InlineData("EventLog")]
    [InlineData("RpcSs")]
    [InlineData("MpsSvc")]
    public void IsSafeServiceName_RejectsCriticalServices(string serviceName)
    {
        ResiduePathSafety.IsSafeServiceName(serviceName).Should().BeFalse();
    }

    [Theory]
    [InlineData("AdobeARMservice")]
    [InlineData("JetBrainsEtwHost")]
    [InlineData("MyCustomVendorService")]
    public void IsSafeServiceName_AllowsThirdPartyServices(string serviceName)
    {
        ResiduePathSafety.IsSafeServiceName(serviceName).Should().BeTrue();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void IsSafeServiceName_RejectsEmptyOrNull(string? serviceName)
    {
        ResiduePathSafety.IsSafeServiceName(serviceName!).Should().BeFalse();
    }

    [Fact]
    public void IsSafeServiceName_IsCaseInsensitive()
    {
        ResiduePathSafety.IsSafeServiceName("WINDEFEND").Should().BeFalse();
        ResiduePathSafety.IsSafeServiceName("trustedinstaller").Should().BeFalse();
    }
}
