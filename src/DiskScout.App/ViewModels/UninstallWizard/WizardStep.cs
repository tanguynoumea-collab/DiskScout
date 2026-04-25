namespace DiskScout.ViewModels.UninstallWizard;

/// <summary>
/// State machine for the Uninstall Wizard. The wizard advances through these steps in order
/// with optional Back navigation between Selection / Preview and Selection / Preview / RunUninstall.
/// </summary>
public enum WizardStep
{
    /// <summary>Step 1 — confirm program selection + silent toggle.</summary>
    Selection,

    /// <summary>Step 2 — preview known residues from install trace + publisher rules.</summary>
    Preview,

    /// <summary>Step 3 — run the native uninstaller with live output streaming.</summary>
    RunUninstall,

    /// <summary>Step 4 — deep residue scan post-uninstall.</summary>
    ResidueScan,

    /// <summary>Step 5 — checkable tree + final irreversible-confirmation modal.</summary>
    ConfirmDelete,

    /// <summary>Step 6 — final report (HTML/JSON exportable). Reached after a successful confirmed deletion.</summary>
    Report,

    /// <summary>Terminal state after the user closes the report.</summary>
    Done,
}
