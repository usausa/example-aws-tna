namespace Tna.Frontend;

using Bunit;

using MudBlazor.Services;

internal static class TestContextExtensions
{
    // MudBlazor の部品を描けるようにする。部品が呼ぶ JavaScript は何もしない
    public static void AddMudBlazor(this BunitContext context)
    {
        context.Services.AddMudServices();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
    }
}
