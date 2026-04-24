namespace DiskScout.Services;

public enum ExportFormat
{
    Csv,
    Html,
}

public enum ExportPane
{
    Programs,
    Orphans,
    Tree,
}

public interface IExporter
{
    Task ExportAsync(
        ExportPane pane,
        ExportFormat format,
        string destinationPath,
        CancellationToken cancellationToken);
}
