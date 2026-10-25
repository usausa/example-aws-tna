namespace Tna.Contracts;

using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

// 関数と画面で共有する JSON の設定。すべての型をソース生成のコンテキストから取る。列挙は仕様の値で書き、
// 種類で形が変わる型の判別の項目は最後にあってもよい。要求の本文の必須の項目は JSON になければならず、時点にはオフセットが要る
public static class TnaJson
{
    public static void Configure(JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.TypeInfoResolver = TnaJsonContext.Default.WithAddedModifier(ContractValidation.Checks.RequireProperties);
        options.AllowOutOfOrderMetadataProperties = true;
        options.Converters.Add(new Rfc3339Converter());
    }

    // 関数で使う、書き換えのできない設定
    public static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions();
        Configure(options);
        options.MakeReadOnly();
        return options;
    }
}
