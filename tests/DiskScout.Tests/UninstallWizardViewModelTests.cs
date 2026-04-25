using DiskScout.Models;
using DiskScout.Services;
using DiskScout.ViewModels.UninstallWizard;
using FluentAssertions;
using Serilog;

namespace DiskScout.Tests;

/// <summary>
/// Task 1 (Plan 09-05) tests — wizard state machine + ResidueTreeNode hierarchy semantics.
///
/// All service dependencies are stubbed via hand-written fakes (project policy: no new NuGet,
/// see Plan 09-01 and Plan 09-03 SUMMARY for precedent).
/// </summary>
public class UninstallWizardViewModelTests
{
    private static readonly ILogger Log =
        new LoggerConfiguration().MinimumLevel.Warning().CreateLogger();

    private static InstalledProgram MakeTarget(string display = "Acme App", string? publisher = "Acme Inc.") =>
        new(
            RegistryKey: "AcmeApp",
            Hive: RegistryHive.LocalMachine64,
            DisplayName: display,
            Publisher: publisher,
            Version: "1.0.0",
            InstallDate: null,
            InstallLocation: @"C:\Program Files\Acme",
            UninstallString: @"""C:\Program Files\Acme\uninstall.exe""",
            RegistryEstimatedSizeBytes: 1_000_000,
            ComputedSizeBytes: 0);

    private static UninstallWizardViewModel BuildWizard(InstalledProgram? target = null) =>
        new(
            Log,
            target ?? MakeTarget(),
            new FakeInstallTracker(),
            new FakeInstallTraceStore(),
            new FakeUninstallerDriver(),
            new FakeResidueScanner(),
            new FakePublisherRuleEngine(),
            new FakeFileDeletion());

    // ---------- Test 1 ----------
    [Fact]
    public void Constructor_InitializesAtSelectionStep_WithSelectionViewModel()
    {
        var vm = BuildWizard();

        vm.CurrentStep.Should().Be(WizardStep.Selection);
        vm.CurrentStepViewModel.Should().BeOfType<SelectionStepViewModel>();
        vm.Target.DisplayName.Should().Be("Acme App");
    }

    // ---------- Test 2 ----------
    [Fact]
    public void Selection_NextCommand_TransitionsToPreview()
    {
        var vm = BuildWizard();

        vm.GoToPreviewCommand.Execute(null);

        vm.CurrentStep.Should().Be(WizardStep.Preview);
        vm.CurrentStepViewModel.Should().BeOfType<PreviewStepViewModel>();
    }

    // ---------- Test 3 ----------
    [Fact]
    public void Preview_BackCommand_TransitionsBackToSelection()
    {
        var vm = BuildWizard();
        vm.GoToPreviewCommand.Execute(null);
        vm.CurrentStep.Should().Be(WizardStep.Preview);

        vm.GoBackToSelectionCommand.Execute(null);

        vm.CurrentStep.Should().Be(WizardStep.Selection);
        vm.CurrentStepViewModel.Should().BeOfType<SelectionStepViewModel>();
    }

    // ---------- Test 4 ----------
    [Fact]
    public void Cancel_RaisesCloseRequested_AndCancelsWizardCts()
    {
        var vm = BuildWizard();
        // Move to RunUninstall so the wizard creates an active CTS.
        vm.GoToRunUninstallCommand.Execute(null);

        var token = vm.WizardCancellationToken;
        bool closeRaised = false;
        vm.CloseRequested += (_, _) => closeRaised = true;

        vm.CancelCommand.Execute(null);

        closeRaised.Should().BeTrue();
        token.IsCancellationRequested.Should().BeTrue();
    }

    // ---------- Test 5 ----------
    [Fact]
    public void ResidueTreeNode_ParentChecked_PropagatesToChildren()
    {
        var leaf1 = new ResidueTreeNode { Path = @"C:\foo", SizeBytes = 100, IsChecked = false };
        var leaf2 = new ResidueTreeNode { Path = @"C:\bar", SizeBytes = 200, IsChecked = false };
        var parent = new ResidueTreeNode { Label = "FS", IsChecked = false };
        parent.Children.Add(leaf1);
        parent.Children.Add(leaf2);

        parent.IsChecked = true;

        leaf1.IsChecked.Should().Be(true);
        leaf2.IsChecked.Should().Be(true);
    }

    [Fact]
    public void ResidueTreeNode_ParentUnchecked_PropagatesUncheckToChildren()
    {
        var leaf = new ResidueTreeNode { Path = @"C:\foo", SizeBytes = 100, IsChecked = true };
        var parent = new ResidueTreeNode { Label = "FS", IsChecked = true };
        parent.Children.Add(leaf);

        parent.IsChecked = false;

        leaf.IsChecked.Should().Be(false);
    }

    // ---------- Test 6 ----------
    [Fact]
    public void ResidueTreeNode_EnumerateLeavesChecked_OnlyReturnsChecked()
    {
        var leafA = new ResidueTreeNode { Path = @"C:\a", SizeBytes = 10, IsChecked = true };
        var leafB = new ResidueTreeNode { Path = @"C:\b", SizeBytes = 20, IsChecked = false };
        var leafC = new ResidueTreeNode { Path = @"C:\c", SizeBytes = 30, IsChecked = true };

        var parent = new ResidueTreeNode { Label = "Root" };
        parent.Children.Add(leafA);
        parent.Children.Add(leafB);
        parent.Children.Add(leafC);

        var leaves = parent.EnumerateLeavesChecked().ToList();

        leaves.Should().HaveCount(2);
        leaves.Should().Contain(l => l.Path == @"C:\a");
        leaves.Should().Contain(l => l.Path == @"C:\c");
        leaves.Should().NotContain(l => l.Path == @"C:\b");
    }

    // ---------- Test 7 ----------
    [Fact]
    public void ConfirmDeleteStep_TotalSelectedBytesAndCount_UpdateOnLeafCheck()
    {
        var vm = BuildWizard();
        vm.AllResidueFindings.Add(new ResidueFinding(
            ResidueCategory.Filesystem, @"C:\Users\Test\AcmeData", 5_000,
            "test", ResidueTrustLevel.MediumConfidence, ResidueSource.NameHeuristic));
        vm.AllResidueFindings.Add(new ResidueFinding(
            ResidueCategory.Filesystem, @"C:\Users\Test\AcmeCache", 3_000,
            "test", ResidueTrustLevel.MediumConfidence, ResidueSource.NameHeuristic));

        vm.GoToConfirmDeleteCommand.Execute(null);
        var step = (ConfirmDeleteStepViewModel)vm.CurrentStepViewModel!;

        step.TotalSelectedBytes.Should().Be(0);
        step.SelectedCount.Should().Be(0);

        // Tick the first leaf (under the single category root).
        var firstLeaf = step.Roots[0].Children[0];
        firstLeaf.IsChecked = true;

        step.SelectedCount.Should().Be(1);
        step.TotalSelectedBytes.Should().BeGreaterThan(0);
    }

    [Fact]
    public void ConfirmDeleteStep_BuildTree_FiltersUnsafePathsViaResiduePathSafety()
    {
        var vm = BuildWizard();
        // Two findings: one safe (Acme app data), one unsafe (Windows\System32).
        vm.AllResidueFindings.Add(new ResidueFinding(
            ResidueCategory.Filesystem, @"C:\Users\Test\AcmeApp\config.dat", 100,
            "trace", ResidueTrustLevel.HighConfidence, ResidueSource.TraceMatch));
        vm.AllResidueFindings.Add(new ResidueFinding(
            ResidueCategory.Filesystem, @"C:\Windows\System32\AcmeDriver.dll", 200,
            "fake", ResidueTrustLevel.MediumConfidence, ResidueSource.NameHeuristic));

        vm.GoToConfirmDeleteCommand.Execute(null);
        var step = (ConfirmDeleteStepViewModel)vm.CurrentStepViewModel!;

        // Tree should contain only the safe leaf.
        var allLeafPaths = step.Roots.SelectMany(r => r.Children).Select(l => l.Path).ToList();
        allLeafPaths.Should().Contain(@"C:\Users\Test\AcmeApp\config.dat");
        allLeafPaths.Should().NotContain(@"C:\Windows\System32\AcmeDriver.dll");
    }

    [Fact]
    public void Wizard_GoToConfirmDelete_SetsCurrentStepViewModelToConfirmDelete()
    {
        var vm = BuildWizard();
        vm.GoToConfirmDeleteCommand.Execute(null);

        vm.CurrentStep.Should().Be(WizardStep.ConfirmDelete);
        vm.CurrentStepViewModel.Should().BeOfType<ConfirmDeleteStepViewModel>();
    }

    // ============================================================
    // Hand-written fakes (no Moq — project policy precedent from
    // Plans 09-01 / 09-03 / 09-04).
    // ============================================================

    private sealed class FakeInstallTracker : IInstallTracker
    {
        public bool IsTracking => false;
        public Task StartAsync(string? cmd, string? hint, CancellationToken ct = default) => Task.CompletedTask;
        public Task<InstallTrace> StopAsync(CancellationToken ct = default) => throw new NotImplementedException();
    }

    private sealed class FakeInstallTraceStore : IInstallTraceStore
    {
        public Task SaveAsync(InstallTrace trace, CancellationToken ct = default) => Task.CompletedTask;
        public Task<IReadOnlyList<InstallTraceHeader>> ListAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<InstallTraceHeader>>(Array.Empty<InstallTraceHeader>());
        public Task<InstallTrace?> LoadAsync(string traceId, CancellationToken ct = default) =>
            Task.FromResult<InstallTrace?>(null);
        public Task<bool> DeleteAsync(string traceId, CancellationToken ct = default) => Task.FromResult(false);
    }

    private sealed class FakeUninstallerDriver : INativeUninstallerDriver
    {
        public UninstallCommand? ParseCommand(InstalledProgram p, string? quiet, bool preferSilent) =>
            new UninstallCommand(@"C:\fake\uninstall.exe", "", null, preferSilent, InstallerKind.Generic);
        public Task<UninstallOutcome> RunAsync(UninstallCommand cmd, IProgress<string>? output, CancellationToken ct) =>
            Task.FromResult(new UninstallOutcome(UninstallStatus.Success, 0, TimeSpan.Zero, 0, null));
    }

    private sealed class FakeResidueScanner : IResidueScanner
    {
        public Task<IReadOnlyList<ResidueFinding>> ScanAsync(
            ResidueScanTarget target, InstallTrace? trace, IProgress<string>? progress, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<ResidueFinding>>(Array.Empty<ResidueFinding>());
    }

    private sealed class FakePublisherRuleEngine : IPublisherRuleEngine
    {
        public Task LoadAsync(CancellationToken ct = default) => Task.CompletedTask;
        public IReadOnlyList<PublisherRule> AllRules => Array.Empty<PublisherRule>();
        public IReadOnlyList<PublisherRuleMatch> Match(string? publisher, string displayName) =>
            Array.Empty<PublisherRuleMatch>();
        public string ExpandTokens(string template, string? publisher, string displayName) => template;
    }

    private sealed class FakeFileDeletion : IFileDeletionService
    {
        public Task<DeletionResult> DeleteAsync(IReadOnlyList<string> paths, DiskScout.Helpers.DeleteMode mode, CancellationToken ct = default) =>
            Task.FromResult(new DeletionResult(paths.Select(p => new DeletionEntry(p, true, 0, null)).ToList()));
    }
}
