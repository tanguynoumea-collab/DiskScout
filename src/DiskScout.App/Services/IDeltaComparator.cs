using DiskScout.Models;

namespace DiskScout.Services;

public interface IDeltaComparator
{
    DeltaResult Compare(ScanResult before, ScanResult after);
}
