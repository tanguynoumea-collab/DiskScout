using DiskScout.Models;
using DiskScout.Services;
using FluentAssertions;
using Serilog;
using Serilog.Core;

namespace DiskScout.Tests;

public class NativeUninstallerDriverTests
{
    private static readonly ILogger Logger =
        new LoggerConfiguration().MinimumLevel.Warning().CreateLogger();

    private static InstalledProgram MakeProgram(string? uninstallString, string displayName = "TestApp") =>
        new InstalledProgram(
            RegistryKey: "{TEST}",
            Hive: RegistryHive.LocalMachine64,
            DisplayName: displayName,
            Publisher: "TestPub",
            Version: "1.0.0",
            InstallDate: null,
            InstallLocation: null,
            UninstallString: uninstallString,
            RegistryEstimatedSizeBytes: 0,
            ComputedSizeBytes: 0);

    // ---- Test 1: Inno-style quoted unins000.exe with no args, no preferSilent ----
    [Fact]
    public void Parse_Inno_Quoted_NoArgs_NoSilent()
    {
        var driver = new NativeUninstallerDriver(Logger);
        var program = MakeProgram("\"C:\\Program Files\\App\\unins000.exe\"");

        var cmd = driver.ParseCommand(program, quietUninstallString: null, preferSilent: false);

        cmd.Should().NotBeNull();
        cmd!.ExecutablePath.Should().Be(@"C:\Program Files\App\unins000.exe");
        cmd.Arguments.Should().Be(string.Empty);
        cmd.Kind.Should().Be(InstallerKind.InnoSetup);
        cmd.IsSilent.Should().BeFalse();
        cmd.WorkingDirectory.Should().Be(@"C:\Program Files\App");
    }

    // ---- Test 2: Inno-style with existing /SILENT and _?= sentinel; preferSilent=true ----
    [Fact]
    public void Parse_Inno_PreservesUnderscoreQuestionEquals_AndAppendsInnoSilentFlags()
    {
        var driver = new NativeUninstallerDriver(Logger);
        var program = MakeProgram("\"C:\\Program Files\\App\\unins000.exe\" /SILENT _?=C:\\Program Files\\App");

        var cmd = driver.ParseCommand(program, quietUninstallString: null, preferSilent: true);

        cmd.Should().NotBeNull();
        cmd!.IsSilent.Should().BeTrue();
        cmd.Kind.Should().Be(InstallerKind.InnoSetup);
        cmd.Arguments.Should().Contain("_?=C:\\Program Files\\App");
        cmd.Arguments.Should().Contain("/SUPPRESSMSGBOXES");
        cmd.Arguments.Should().Contain("/NORESTART");
    }

    // ---- Test 3: MSI /I{guid} -> /X{guid}, append /qn /norestart ----
    [Fact]
    public void Parse_Msi_ConvertsInstallToUninstall_AndAppendsQuietFlags()
    {
        var driver = new NativeUninstallerDriver(Logger);
        var program = MakeProgram("MsiExec.exe /I{12345678-1234-1234-1234-123456789012}");

        var cmd = driver.ParseCommand(program, quietUninstallString: null, preferSilent: true);

        cmd.Should().NotBeNull();
        cmd!.Kind.Should().Be(InstallerKind.MsiExec);
        cmd.IsSilent.Should().BeTrue();
        cmd.ExecutablePath.Should().EndWith("MsiExec.exe");
        cmd.Arguments.Should().Contain("/X{12345678-1234-1234-1234-123456789012}");
        cmd.Arguments.Should().NotContain("/I{");
        cmd.Arguments.Should().Contain("/qn");
        cmd.Arguments.Should().Contain("/norestart");
    }

    // ---- Test 4: MSI /X{guid} preferSilent=false -> no /qn, IsSilent=false ----
    [Fact]
    public void Parse_Msi_NoSilent_DoesNotAppendQuietFlags()
    {
        var driver = new NativeUninstallerDriver(Logger);
        var program = MakeProgram("MsiExec.exe /X{12345678-1234-1234-1234-123456789012}");

        var cmd = driver.ParseCommand(program, quietUninstallString: null, preferSilent: false);

        cmd.Should().NotBeNull();
        cmd!.Kind.Should().Be(InstallerKind.MsiExec);
        cmd.IsSilent.Should().BeFalse();
        cmd.Arguments.Should().Contain("/X{12345678-1234-1234-1234-123456789012}");
        cmd.Arguments.Should().NotContain("/qn");
    }

    // ---- Test 5: NSIS-style uninstall.exe -> /S appended, IsSilent=true ----
    [Fact]
    public void Parse_Nsis_AppendsCapitalSFlag()
    {
        var driver = new NativeUninstallerDriver(Logger);
        var program = MakeProgram("\"C:\\App\\uninstall.exe\"");

        var cmd = driver.ParseCommand(program, quietUninstallString: null, preferSilent: true);

        cmd.Should().NotBeNull();
        cmd!.Kind.Should().Be(InstallerKind.NsisExec);
        cmd.IsSilent.Should().BeTrue();
        cmd.Arguments.Should().Contain("/S");
        cmd.ExecutablePath.Should().Be(@"C:\App\uninstall.exe");
    }

    // ---- Test 6: Generic unknown installer + preferSilent=true, no quiet string -> null ----
    [Fact]
    public void Parse_Generic_PreferSilent_WithoutQuietString_ReturnsNull()
    {
        var driver = new NativeUninstallerDriver(Logger);
        var program = MakeProgram("\"C:\\App\\Setup.exe\" --uninstall");

        var cmd = driver.ParseCommand(program, quietUninstallString: null, preferSilent: true);

        cmd.Should().BeNull();
    }

    // Sanity: same generic, preferSilent=false -> non-silent UninstallCommand
    [Fact]
    public void Parse_Generic_NoPreferSilent_ReturnsCommandWithIsSilentFalse()
    {
        var driver = new NativeUninstallerDriver(Logger);
        var program = MakeProgram("\"C:\\App\\Setup.exe\" --uninstall");

        var cmd = driver.ParseCommand(program, quietUninstallString: null, preferSilent: false);

        cmd.Should().NotBeNull();
        cmd!.Kind.Should().Be(InstallerKind.Generic);
        cmd.IsSilent.Should().BeFalse();
        cmd.Arguments.Should().Be("--uninstall");
    }

    // ---- Test 7: QuietUninstallString provided, preferSilent=true -> use it directly ----
    [Fact]
    public void Parse_QuietStringUsedDirectly_WhenPreferSilent()
    {
        var driver = new NativeUninstallerDriver(Logger);
        var program = MakeProgram("\"C:\\App\\loud.exe\" /interactive");

        var cmd = driver.ParseCommand(
            program,
            quietUninstallString: "\"C:\\App\\silent-uninst.exe\" /quiet",
            preferSilent: true);

        cmd.Should().NotBeNull();
        cmd!.IsSilent.Should().BeTrue();
        cmd.ExecutablePath.Should().Be(@"C:\App\silent-uninst.exe");
        cmd.Arguments.Should().Be("/quiet");
    }

    // ---- Test 8: Empty / whitespace UninstallString -> null ----
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Parse_EmptyUninstallString_ReturnsNull(string? uninstallString)
    {
        var driver = new NativeUninstallerDriver(Logger);
        var program = MakeProgram(uninstallString);

        var cmd = driver.ParseCommand(program, quietUninstallString: null, preferSilent: false);

        cmd.Should().BeNull();
    }

    // ---- Test 9: Working directory derived from rooted exe path ----
    [Fact]
    public void Parse_WorkingDirectory_IsParentOfExe_WhenRooted()
    {
        var driver = new NativeUninstallerDriver(Logger);
        var program = MakeProgram("\"C:\\App\\unins000.exe\" /SILENT");

        var cmd = driver.ParseCommand(program, quietUninstallString: null, preferSilent: false);

        cmd.Should().NotBeNull();
        cmd!.WorkingDirectory.Should().Be(@"C:\App");
    }

    // ---- Bonus: MsiExec.exe without absolute path is not rooted -> WorkingDirectory null ----
    [Fact]
    public void Parse_Msi_NonRootedExePath_HasNullWorkingDirectory()
    {
        var driver = new NativeUninstallerDriver(Logger);
        var program = MakeProgram("MsiExec.exe /X{ABC}");

        var cmd = driver.ParseCommand(program, quietUninstallString: null, preferSilent: false);

        cmd.Should().NotBeNull();
        cmd!.WorkingDirectory.Should().BeNull();
    }

    // ---- Bonus: unquoted exe with arguments splits at first whitespace ----
    [Fact]
    public void Parse_Unquoted_SplitsAtFirstWhitespace()
    {
        var driver = new NativeUninstallerDriver(Logger);
        var program = MakeProgram(@"C:\App\unins000.exe /SILENT");

        var cmd = driver.ParseCommand(program, quietUninstallString: null, preferSilent: false);

        cmd.Should().NotBeNull();
        cmd!.ExecutablePath.Should().Be(@"C:\App\unins000.exe");
        cmd.Arguments.Should().Be("/SILENT");
        cmd.Kind.Should().Be(InstallerKind.InnoSetup);
    }
}
