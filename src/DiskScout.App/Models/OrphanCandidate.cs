namespace DiskScout.Models;

public enum OrphanCategory
{
    AppDataOrphan,
    EmptyProgramFiles,
    StaleTemp,
    OrphanInstallerPatch,
    SystemArtifact,
    DevCache,
    BrowserCache,
    EmptyFolder,
    BrokenShortcut,
}

public sealed record OrphanCandidate(
    long NodeId,
    string FullPath,
    long SizeBytes,
    OrphanCategory Category,
    string Reason,
    double? MatchScore);
