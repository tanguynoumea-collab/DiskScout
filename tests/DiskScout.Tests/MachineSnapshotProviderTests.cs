using DiskScout.Models;
using DiskScout.Services;
using FluentAssertions;
using Serilog;

namespace DiskScout.Tests;

/// <summary>
/// Tests for <see cref="MachineSnapshotProvider"/> — covers TTL cache hit
/// (reference equality), explicit Invalidate, index-set population, graceful
/// per-source failure, and concurrent-call deduplication.
///
/// Project policy: hand-written fakes only (no Moq for new code, per Plan 09-05
/// precedent — see CONTEXT.md "Established Patterns").
/// </summary>
public class MachineSnapshotProviderTests
{
    private readonly ILogger _logger = new LoggerConfiguration().MinimumLevel.Warning().CreateLogger();

    // -------------------------------------------------------------------------
    // Test 1: All-empty fakes -> non-null snapshot, zero entries, indexes empty.
    // -------------------------------------------------------------------------
    [Fact]
    public async Task GetAsync_with_empty_enumerators_returns_snapshot_with_empty_lists()
    {
        var provider = new MachineSnapshotProvider(
            _logger,
            new FakeServiceEnumerator(Array.Empty<(string, string, string?)>()),
            new FakeDriverEnumerator(Array.Empty<(string, string?, string?, string?)>()),
            new FakeAppxEnumerator(Array.Empty<(string, string?, string?, string?)>()),
            new FakeScheduledTaskEnumerator(Array.Empty<(string, string?, string?)>()));

        var snap = await provider.GetAsync();

        snap.Should().NotBeNull();
        snap.Services.Should().BeEmpty();
        snap.Drivers.Should().BeEmpty();
        snap.AppxPackages.Should().BeEmpty();
        snap.ScheduledTasks.Should().BeEmpty();
        snap.ServiceBinaryPathPrefixes.Should().BeEmpty();
        snap.DriverProviderTokens.Should().BeEmpty();
        snap.AppxInstallLocationPrefixes.Should().BeEmpty();
    }

    // -------------------------------------------------------------------------
    // Test 2: Two GetAsync calls within the TTL return the SAME instance
    //         (reference equality) — proves the cache hit path.
    // -------------------------------------------------------------------------
    [Fact]
    public async Task GetAsync_called_twice_within_TTL_returns_same_instance()
    {
        var fakeService = new FakeServiceEnumerator(new[] { ("svc1", "Service 1", (string?)@"C:\Program Files\Vendor\svc.exe") });
        var provider = new MachineSnapshotProvider(
            _logger, fakeService,
            new FakeDriverEnumerator(Array.Empty<(string, string?, string?, string?)>()),
            new FakeAppxEnumerator(Array.Empty<(string, string?, string?, string?)>()),
            new FakeScheduledTaskEnumerator(Array.Empty<(string, string?, string?)>()),
            ttl: TimeSpan.FromMinutes(5));

        var s1 = await provider.GetAsync();
        var s2 = await provider.GetAsync();

        ReferenceEquals(s1, s2).Should().BeTrue("snapshot must be cached within the TTL window");
        fakeService.CallCount.Should().Be(1, "the underlying enumerator must run only once");
    }

    // -------------------------------------------------------------------------
    // Test 3: Invalidate() forces a rebuild on the next GetAsync — new instance.
    // -------------------------------------------------------------------------
    [Fact]
    public async Task Invalidate_forces_rebuild_on_next_GetAsync()
    {
        var fakeService = new FakeServiceEnumerator(Array.Empty<(string, string, string?)>());
        var provider = new MachineSnapshotProvider(
            _logger, fakeService,
            new FakeDriverEnumerator(Array.Empty<(string, string?, string?, string?)>()),
            new FakeAppxEnumerator(Array.Empty<(string, string?, string?, string?)>()),
            new FakeScheduledTaskEnumerator(Array.Empty<(string, string?, string?)>()));

        var s1 = await provider.GetAsync();
        provider.Invalidate();
        var s2 = await provider.GetAsync();

        ReferenceEquals(s1, s2).Should().BeFalse("Invalidate must force a fresh snapshot");
        fakeService.CallCount.Should().Be(2, "the enumerator must run again after invalidation");
    }

    // -------------------------------------------------------------------------
    // Test 4: TTL expiry forces a rebuild — supply a tiny TTL and wait past it.
    // -------------------------------------------------------------------------
    [Fact]
    public async Task GetAsync_after_TTL_expiry_rebuilds_and_returns_new_instance()
    {
        var fakeService = new FakeServiceEnumerator(Array.Empty<(string, string, string?)>());
        var provider = new MachineSnapshotProvider(
            _logger, fakeService,
            new FakeDriverEnumerator(Array.Empty<(string, string?, string?, string?)>()),
            new FakeAppxEnumerator(Array.Empty<(string, string?, string?, string?)>()),
            new FakeScheduledTaskEnumerator(Array.Empty<(string, string?, string?)>()),
            ttl: TimeSpan.FromMilliseconds(50));

        var s1 = await provider.GetAsync();
        await Task.Delay(120);
        var s2 = await provider.GetAsync();

        ReferenceEquals(s1, s2).Should().BeFalse("expired TTL must trigger a rebuild");
        fakeService.CallCount.Should().Be(2);
    }

    // -------------------------------------------------------------------------
    // Test 5: Index sets are populated correctly from the source data.
    // -------------------------------------------------------------------------
    [Fact]
    public async Task Index_sets_are_populated_from_source_data()
    {
        var services = new[]
        {
            ("svc1", "Service 1", (string?)@"C:\Program Files\Vendor\svc.exe"),
            ("svc2", "Service 2", (string?)@"C:\Windows\System32\drivers\thing.sys"),
            ("svc3", "Service 3", (string?)null), // no binary path
        };
        var drivers = new[]
        {
            ("oem01.inf", (string?)"vendor.inf", (string?)"NVIDIA Corporation", (string?)"Display"),
            ("oem02.inf", (string?)"audio.inf", (string?)"Realtek Semiconductor Corp.", (string?)"Sound"),
        };
        var appx = new[]
        {
            ("Microsoft.WindowsCalculator_10.0.0_x64__8wekyb3d8bbwe", (string?)"Microsoft.WindowsCalculator_8wekyb3d8bbwe",
             (string?)"CN=Microsoft", (string?)@"C:\Program Files\WindowsApps\Microsoft.WindowsCalculator_10.0.0_x64__8wekyb3d8bbwe"),
        };

        var provider = new MachineSnapshotProvider(
            _logger,
            new FakeServiceEnumerator(services),
            new FakeDriverEnumerator(drivers),
            new FakeAppxEnumerator(appx),
            new FakeScheduledTaskEnumerator(Array.Empty<(string, string?, string?)>()));

        var snap = await provider.GetAsync();

        snap.Services.Should().HaveCount(3);
        snap.Drivers.Should().HaveCount(2);
        snap.AppxPackages.Should().HaveCount(1);

        // ServiceBinaryPathPrefixes contains the parent dir of each non-null binary path.
        snap.ServiceBinaryPathPrefixes.Should().Contain(@"C:\Program Files\Vendor");
        snap.ServiceBinaryPathPrefixes.Should().Contain(@"C:\Windows\System32\drivers");

        // DriverProviderTokens contains tokens of each driver's Provider field.
        snap.DriverProviderTokens.Should().Contain("NVIDIA");
        snap.DriverProviderTokens.Should().Contain("Corporation");
        snap.DriverProviderTokens.Should().Contain("Realtek");

        // AppxInstallLocationPrefixes contains each Appx package's InstallLocation.
        snap.AppxInstallLocationPrefixes.Should()
            .Contain(@"C:\Program Files\WindowsApps\Microsoft.WindowsCalculator_10.0.0_x64__8wekyb3d8bbwe");
    }

    // -------------------------------------------------------------------------
    // Test 6: When one enumerator throws, snapshot still builds with empty list
    //         for that source (graceful degradation).
    // -------------------------------------------------------------------------
    [Fact]
    public async Task GetAsync_substitutes_empty_list_when_enumerator_throws()
    {
        var provider = new MachineSnapshotProvider(
            _logger,
            new FakeServiceEnumerator(new[] { ("svc1", "S1", (string?)null) }),
            new ThrowingDriverEnumerator(),  // <-- throws on first call
            new FakeAppxEnumerator(Array.Empty<(string, string?, string?, string?)>()),
            new FakeScheduledTaskEnumerator(Array.Empty<(string, string?, string?)>()));

        var snap = await provider.GetAsync();

        snap.Should().NotBeNull();
        snap.Services.Should().HaveCount(1, "the surviving service enumerator must still populate");
        snap.Drivers.Should().BeEmpty("the throwing driver enumerator must degrade to an empty list");
        snap.AppxPackages.Should().BeEmpty();
        snap.ScheduledTasks.Should().BeEmpty();
    }

    // -------------------------------------------------------------------------
    // Test 7: Concurrent GetAsync callers during a slow build see the same
    //         snapshot AND the underlying enumerator runs only once.
    // -------------------------------------------------------------------------
    [Fact]
    public async Task Concurrent_GetAsync_calls_only_build_once()
    {
        var slowService = new SlowFakeServiceEnumerator(delayMs: 80);
        var provider = new MachineSnapshotProvider(
            _logger,
            slowService,
            new FakeDriverEnumerator(Array.Empty<(string, string?, string?, string?)>()),
            new FakeAppxEnumerator(Array.Empty<(string, string?, string?, string?)>()),
            new FakeScheduledTaskEnumerator(Array.Empty<(string, string?, string?)>()));

        // Fire 10 GetAsync in parallel.
        var tasks = Enumerable.Range(0, 10)
            .Select(_ => provider.GetAsync())
            .ToArray();
        var results = await Task.WhenAll(tasks);

        // All 10 results must reference the same snapshot.
        for (int i = 1; i < results.Length; i++)
        {
            ReferenceEquals(results[0], results[i])
                .Should().BeTrue("concurrent callers must share the same snapshot");
        }
        slowService.CallCount
            .Should().Be(1, "the gate must serialize the build to exactly one underlying call");
    }

    // ---------- Hand-written fakes ----------

    private sealed class FakeServiceEnumerator : IServiceEnumerator
    {
        private readonly IReadOnlyList<(string, string, string?)> _items;
        public int CallCount { get; private set; }
        public FakeServiceEnumerator(IEnumerable<(string, string, string?)> items)
            => _items = items.ToList();
        public IEnumerable<(string Name, string DisplayName, string? BinaryPath)> EnumerateServices()
        {
            CallCount++;
            return _items;
        }
    }

    private sealed class SlowFakeServiceEnumerator : IServiceEnumerator
    {
        private readonly int _delayMs;
        public int CallCount { get; private set; }
        public SlowFakeServiceEnumerator(int delayMs) => _delayMs = delayMs;
        public IEnumerable<(string Name, string DisplayName, string? BinaryPath)> EnumerateServices()
        {
            CallCount++;
            Thread.Sleep(_delayMs);
            return Array.Empty<(string, string, string?)>();
        }
    }

    private sealed class FakeDriverEnumerator : IDriverEnumerator
    {
        private readonly IReadOnlyList<(string, string?, string?, string?)> _items;
        public int CallCount { get; private set; }
        public FakeDriverEnumerator(IEnumerable<(string, string?, string?, string?)> items)
            => _items = items.ToList();
        public IEnumerable<(string PublishedName, string? OriginalFileName, string? Provider, string? ClassName)> EnumerateDrivers()
        {
            CallCount++;
            return _items;
        }
    }

    private sealed class ThrowingDriverEnumerator : IDriverEnumerator
    {
        public IEnumerable<(string PublishedName, string? OriginalFileName, string? Provider, string? ClassName)> EnumerateDrivers()
        {
            throw new InvalidOperationException("Synthetic enumerator failure for graceful-degradation test.");
        }
    }

    private sealed class FakeAppxEnumerator : IAppxEnumerator
    {
        private readonly IReadOnlyList<(string, string?, string?, string?)> _items;
        public int CallCount { get; private set; }
        public FakeAppxEnumerator(IEnumerable<(string, string?, string?, string?)> items)
            => _items = items.ToList();
        public IEnumerable<(string PackageFullName, string? PackageFamilyName, string? Publisher, string? InstallLocation)> EnumerateAppxPackages()
        {
            CallCount++;
            return _items;
        }
    }

    private sealed class FakeScheduledTaskEnumerator : IScheduledTaskEnumerator
    {
        private readonly IReadOnlyList<(string, string?, string?)> _items;
        public int CallCount { get; private set; }
        public FakeScheduledTaskEnumerator(IEnumerable<(string, string?, string?)> items)
            => _items = items.ToList();
        public IEnumerable<(string TaskPath, string? Author, string? ActionPath)> EnumerateTasks()
        {
            CallCount++;
            return _items;
        }
    }
}
