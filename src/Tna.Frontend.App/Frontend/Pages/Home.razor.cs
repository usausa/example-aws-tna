namespace Tna.Frontend.Pages;

using Microsoft.AspNetCore.Components;

using Tna.Frontend.Forms;

public sealed partial class Home
{
    private DateOnly today;

    [Inject]
    public required TimeProvider TimeProvider { get; set; }

    protected override void OnInitialized() => today = Display.Today(TimeProvider);
}
