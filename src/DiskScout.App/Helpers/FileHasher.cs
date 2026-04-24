using System.IO;
using System.IO.Hashing;

namespace DiskScout.Helpers;

public static class FileHasher
{
    private const int PartialBytes = 64 * 1024; // first 64 KB
    private const int BufferSize = 256 * 1024;

    public static ulong ComputePartialHash(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            var hasher = new XxHash3();
            Span<byte> buffer = stackalloc byte[BufferSize];
            long read = 0;
            while (read < PartialBytes)
            {
                var toRead = (int)Math.Min(buffer.Length, PartialBytes - read);
                var n = stream.Read(buffer[..toRead]);
                if (n == 0) break;
                hasher.Append(buffer[..n]);
                read += n;
            }
            return hasher.GetCurrentHashAsUInt64();
        }
        catch
        {
            return 0;
        }
    }

    public static ulong ComputeFullHash(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            var hasher = new XxHash3();
            var buffer = new byte[BufferSize];
            int n;
            while ((n = stream.Read(buffer)) > 0)
            {
                hasher.Append(buffer.AsSpan(0, n));
            }
            return hasher.GetCurrentHashAsUInt64();
        }
        catch
        {
            return 0;
        }
    }
}
