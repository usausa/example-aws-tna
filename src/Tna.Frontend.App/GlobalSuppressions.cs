[assembly: System.Diagnostics.CodeAnalysis.SuppressMessage("Maintainability", "CA1515:Consider making public types internal", Justification = "Blazor のルーターは public の画面だけを見つけ、Razor の部品は public で生成されるため")]
[assembly: System.Diagnostics.CodeAnalysis.SuppressMessage("Reliability", "CA2007:Do not directly await a Task", Justification = "部品の続きは描画の文脈で動かすため、ConfigureAwait(false) を使わない")]
