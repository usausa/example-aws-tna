namespace Tna.Frontend.Layout;

using Microsoft.AspNetCore.Components.Web;

public sealed partial class MainLayout
{
    private ErrorBoundary? errorBoundary;

    // PC では開いたままにする。スマホでは閉じ、下のタブで移る
    private bool drawerOpen = true;

    // 別の画面に移ったら、前の画面のエラーを残さない
    protected override void OnParametersSet() => errorBoundary?.Recover();

    private void Recover() => errorBoundary?.Recover();
}
