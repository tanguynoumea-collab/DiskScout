using System.Runtime.CompilerServices;
using System.Windows;

[assembly:ThemeInfo(
    ResourceDictionaryLocation.None,            //where theme specific resource dictionaries are located
                                                //(used if a resource is not found in the page,
                                                // or application resource dictionaries)
    ResourceDictionaryLocation.SourceAssembly   //where the generic resource dictionary is located
                                                //(used if a resource is not found in the page,
                                                // app, or any theme specific resource dictionaries)
)]

// Allow the test project to reach into internal members (e.g. NativeUninstallerDriver's
// test-only constructor that accepts a custom hardTimeout for Timeout test coverage).
[assembly: InternalsVisibleTo("DiskScout.Tests")]
