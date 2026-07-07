using Avalonia;
using Avalonia.Headless;
using Apex.Desktop;

[assembly: Avalonia.Headless.AvaloniaTestApplication(typeof(Apex.Desktop.Tests.TestAppBuilder))]

namespace Apex.Desktop.Tests;

/// <summary>
/// Boots the real <see cref="App"/> in Avalonia's headless platform so UI smoke tests can
/// construct and drive the actual <c>MainWindow</c> without a display.
/// </summary>
public static class TestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UseSkia()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
}
