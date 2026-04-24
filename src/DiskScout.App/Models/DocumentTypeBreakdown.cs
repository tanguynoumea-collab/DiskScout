namespace DiskScout.Models;

public enum DocumentCategory
{
    Pdf,
    Xlsx,
    Rvt,
    Txt,
    Other,
}

public sealed record DocumentTypeBreakdown(
    long PdfBytes,
    long XlsxBytes,
    long RvtBytes,
    long TxtBytes,
    long OtherBytes)
{
    public static readonly DocumentTypeBreakdown Empty = new(0, 0, 0, 0, 0);

    public long TotalBytes => PdfBytes + XlsxBytes + RvtBytes + TxtBytes + OtherBytes;

    public double PdfPercent => TotalBytes == 0 ? 0 : 100.0 * PdfBytes / TotalBytes;
    public double XlsxPercent => TotalBytes == 0 ? 0 : 100.0 * XlsxBytes / TotalBytes;
    public double RvtPercent => TotalBytes == 0 ? 0 : 100.0 * RvtBytes / TotalBytes;
    public double TxtPercent => TotalBytes == 0 ? 0 : 100.0 * TxtBytes / TotalBytes;
    public double OtherPercent => TotalBytes == 0 ? 0 : 100.0 * OtherBytes / TotalBytes;

    public DocumentTypeBreakdown Add(DocumentTypeBreakdown other) => new(
        PdfBytes + other.PdfBytes,
        XlsxBytes + other.XlsxBytes,
        RvtBytes + other.RvtBytes,
        TxtBytes + other.TxtBytes,
        OtherBytes + other.OtherBytes);

    public DocumentTypeBreakdown AddFile(DocumentCategory category, long bytes) => category switch
    {
        DocumentCategory.Pdf => this with { PdfBytes = PdfBytes + bytes },
        DocumentCategory.Xlsx => this with { XlsxBytes = XlsxBytes + bytes },
        DocumentCategory.Rvt => this with { RvtBytes = RvtBytes + bytes },
        DocumentCategory.Txt => this with { TxtBytes = TxtBytes + bytes },
        _ => this with { OtherBytes = OtherBytes + bytes },
    };
}
