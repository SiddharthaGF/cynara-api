using System.Runtime.CompilerServices;

namespace Cynara.Api.Tests.Support;

/// <summary>
/// Switches every file watcher created inside the test host to polling
/// change tokens instead of native inotify watchers.
/// </summary>
/// <remarks>
/// Each <c>WebApplicationFactory</c> instance builds its own host whose
/// configuration providers register file watchers that survive factory
/// disposal until finalization. A full-suite run creates hundreds of these
/// watchers and exhausts the kernel's fs.inotify.max_user_instances quota
/// on Linux, after which every later test fails with IOException from
/// PhysicalFileProvider.Watch. Polling change tokens avoid inotify
/// entirely; tests never depend on hot-reload latency.
/// </remarks>
internal static class TestEnvironment
{
    [ModuleInitializer]
    internal static void Initialize()
    {
        Environment.SetEnvironmentVariable(
            "DOTNET_USE_POLLING_FILE_WATCHER",
            "true");
    }
}
