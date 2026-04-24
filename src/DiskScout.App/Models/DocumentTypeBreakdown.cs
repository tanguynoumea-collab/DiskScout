namespace DiskScout.Models;

public enum DocumentCategory
{
    Pdf,
    Xlsx,
    Rvt,
    Txt,
    Dll,
    Sys,
    Exe,
    Other,
}

public sealed record DocumentTypeBreakdown(
    long PdfBytes,
    long XlsxBytes,
    long RvtBytes,
    long TxtBytes,
    long DllBytes,
    long SysBytes,
    long ExeBytes,
    long OtherBytes)
{
    public static readonly DocumentTypeBreakdown Empty = new(0, 0, 0, 0, 0, 0, 0, 0);

    public long TotalBytes => PdfBytes + XlsxBytes + RvtBytes + TxtBytes + DllBytes + SysBytes + ExeBytes + OtherBytes;

    public double PdfPercent   => TotalBytes == 0 ? 0 : 100.0 * PdfBytes   / TotalBytes;
    public double XlsxPercent  => TotalBytes == 0 ? 0 : 100.0 * XlsxBytes  / TotalBytes;
    public double RvtPercent   => TotalBytes == 0 ? 0 : 100.0 * RvtBytes   / TotalBytes;
    public double TxtPercent   => TotalBytes == 0 ? 0 : 100.0 * TxtBytes   / TotalBytes;
    public double DllPercent   => TotalBytes == 0 ? 0 : 100.0 * DllBytes   / TotalBytes;
    public double SysPercent   => TotalBytes == 0 ? 0 : 100.0 * SysBytes   / TotalBytes;
    public double ExePercent   => TotalBytes == 0 ? 0 : 100.0 * ExeBytes   / TotalBytes;
    public double OtherPercent => TotalBytes == 0 ? 0 : 100.0 * OtherBytes / TotalBytes;

    public DocumentTypeBreakdown Add(DocumentTypeBreakdown other) => new(
        PdfBytes + other.PdfBytes,
        XlsxBytes + other.XlsxBytes,
        RvtBytes + other.RvtBytes,
        TxtBytes + other.TxtBytes,
        DllBytes + other.DllBytes,
        SysBytes + other.SysBytes,
        ExeBytes + other.ExeBytes,
        OtherBytes + other.OtherBytes);

    public DocumentTypeBreakdown AddFile(DocumentCategory category, long bytes) => category switch
    {
        DocumentCategory.Pdf  => this with { PdfBytes  = PdfBytes  + bytes },
        DocumentCategory.Xlsx => this with { XlsxBytes = XlsxBytes + bytes },
        DocumentCategory.Rvt  => this with { RvtBytes  = RvtBytes  + bytes },
        DocumentCategory.Txt  => this with { TxtBytes  = TxtBytes  + bytes },
        DocumentCategory.Dll  => this with { DllBytes  = DllBytes  + bytes },
        DocumentCategory.Sys  => this with { SysBytes  = SysBytes  + bytes },
        DocumentCategory.Exe  => this with { ExeBytes  = ExeBytes  + bytes },
        _                     => this with { OtherBytes = OtherBytes + bytes },
    };
}
