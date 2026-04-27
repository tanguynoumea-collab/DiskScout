using DiskScout.Services;
using FluentAssertions;

namespace DiskScout.Tests;

/// <summary>
/// Regression-locks the Plan 10-02 promotion of <see cref="IServiceEnumerator"/>
/// + <see cref="IScheduledTaskEnumerator"/> from <c>internal</c> to <c>public</c>
/// (and asserts the two NEW interfaces <see cref="IDriverEnumerator"/> +
/// <see cref="IAppxEnumerator"/> are public from the start). If a future refactor
/// re-internalizes any of these four interfaces, this test fails immediately —
/// the snapshot provider + future MultiSourceMatcher (Plan 10-04) all consume
/// these interfaces and require public visibility for cross-assembly DI.
/// </summary>
public class EnumeratorPromotionTests
{
    [Fact]
    public void All_4_enumerator_interfaces_are_public()
    {
        typeof(IServiceEnumerator).IsPublic
            .Should().BeTrue("IServiceEnumerator was promoted to public in Plan 10-02");
        typeof(IScheduledTaskEnumerator).IsPublic
            .Should().BeTrue("IScheduledTaskEnumerator was promoted to public in Plan 10-02");
        typeof(IDriverEnumerator).IsPublic
            .Should().BeTrue("IDriverEnumerator is new in Plan 10-02 — must be public");
        typeof(IAppxEnumerator).IsPublic
            .Should().BeTrue("IAppxEnumerator is new in Plan 10-02 — must be public");
    }

    [Fact]
    public void All_4_enumerator_interfaces_live_in_DiskScout_Services_namespace()
    {
        typeof(IServiceEnumerator).Namespace.Should().Be("DiskScout.Services");
        typeof(IScheduledTaskEnumerator).Namespace.Should().Be("DiskScout.Services");
        typeof(IDriverEnumerator).Namespace.Should().Be("DiskScout.Services");
        typeof(IAppxEnumerator).Namespace.Should().Be("DiskScout.Services");
    }

    [Fact]
    public void ResidueScanner_exposes_static_factories_for_default_service_and_scheduled_task_enumerators()
    {
        // Plan 10-02 contract: ResidueScanner keeps the concrete WmiServiceEnumerator
        // + SchTasksEnumerator as nested-private types and exposes them via two
        // public static factory methods so App.xaml.cs (Plan 10-04) can inject the
        // same defaults the scanner uses.
        var residueType = typeof(ResidueScanner);
        var serviceFactory = residueType.GetMethod(
            nameof(ResidueScanner.CreateDefaultServiceEnumerator),
            new[] { typeof(Serilog.ILogger) });
        serviceFactory.Should().NotBeNull("App.xaml.cs DI wiring depends on this factory");
        serviceFactory!.IsStatic.Should().BeTrue();
        serviceFactory.IsPublic.Should().BeTrue();
        serviceFactory.ReturnType.Should().Be(typeof(IServiceEnumerator));

        var taskFactory = residueType.GetMethod(
            nameof(ResidueScanner.CreateDefaultScheduledTaskEnumerator),
            new[] { typeof(Serilog.ILogger) });
        taskFactory.Should().NotBeNull("App.xaml.cs DI wiring depends on this factory");
        taskFactory!.IsStatic.Should().BeTrue();
        taskFactory.IsPublic.Should().BeTrue();
        taskFactory.ReturnType.Should().Be(typeof(IScheduledTaskEnumerator));
    }
}
