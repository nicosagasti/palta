using System.Runtime.CompilerServices;

namespace Mandarina.Tests;

internal static class TestSetup
{
    /// <summary>Los tests no escriben en el log real del usuario.</summary>
    [ModuleInitializer]
    internal static void Init() =>
        Core.Log.Path = Path.Combine(Path.GetTempPath(), "mandarina-tests", "log.txt");
}
