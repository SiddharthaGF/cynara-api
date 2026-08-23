using System.Runtime.CompilerServices;

namespace Cynara.Api.Tests.Support;

/// <summary>
/// Switches every file watcher in the test host to polling change tokens
/// instead of native inotify watchers.
/// </summary>
/// <remarks>
/// Factory hosts register file watchers that survive disposal; a full-suite
/// run creates hundreds and exhausts the kernel's fs.inotify.max_user_instances
/// quota on Linux, after which later tests fail with IOException.
/// Polling avoids inotify entirely; tests never depend on hot-reload latency.
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
