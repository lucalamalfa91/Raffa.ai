using System.Runtime.CompilerServices;

namespace Raffa.Api.Tests.TestSupport;

/// <summary>
/// Every <c>WebApplicationFactory</c> host this suite builds would otherwise watch its
/// appsettings*.json for changes — one inotify instance per host. Hundreds of per-test hosts exhaust
/// a Linux runner's limit (128) and every later host fails to start with "The configured user limit
/// (128) on the number of inotify instances has been reached". No test edits config at runtime, so
/// the reload is switched off for the whole assembly before any host is built (CI sets the same
/// variables on the <c>dotnet test</c> step).
/// </summary>
internal static class NoConfigReloadInTests
{
    [ModuleInitializer]
    internal static void Initialize()
    {
        Environment.SetEnvironmentVariable("DOTNET_hostBuilder__reloadConfigOnChange", "false");
        Environment.SetEnvironmentVariable("DOTNET_USE_POLLING_FILE_WATCHER", "true");
    }
}
