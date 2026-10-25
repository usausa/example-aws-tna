namespace Tna.Contracts.Ops;

using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

// 運営者の関数の JSON の設定。TnaJson と同じ作りで、運営者の API のコンテキストと検証を使う (画面には載せない)
public static class TnaOpsJson
{
    public static void Configure(JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.TypeInfoResolver = TnaOpsJsonContext.Default.WithAddedModifier(OpsContractValidation.Checks.RequireProperties);
        options.AllowOutOfOrderMetadataProperties = true;
        options.Converters.Add(new Rfc3339Converter());
    }

    public static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions();
        Configure(options);
        options.MakeReadOnly();
        return options;
    }
}
