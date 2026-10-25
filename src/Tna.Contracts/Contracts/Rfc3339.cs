namespace Tna.Contracts;

using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

// API の時点は、オフセット付きの RFC 3339。オフセットのない値は断る。動いている機械のタイムゾーン
// (Lambda では UTC、PC では地方時) で読まれてしまうため
public static partial class Rfc3339
{
    public static bool TryParse(string? text, out DateTimeOffset value)
    {
        value = default;
        return (text is not null) && Shape().IsMatch(text) &&
               DateTimeOffset.TryParse(text.ToUpperInvariant(), CultureInfo.InvariantCulture, DateTimeStyles.None, out value);
    }

    // 日付、T、7桁までの小数を含む時刻、Z かオフセット (T と Z は小文字でもよい)
    [GeneratedRegex(@"^[0-9]{4}-[0-9]{2}-[0-9]{2}[Tt][0-9]{2}:[0-9]{2}:[0-9]{2}(\.[0-9]{1,7})?([Zz]|[+-][0-9]{2}:[0-9]{2})\z", RegexOptions.CultureInvariant)]
    private static partial Regex Shape();
}

// 時点の JSON。オフセット付きのときだけ読み (ないと本文を INVALID_BODY として断る)、オフセット付きで書く
public sealed class Rfc3339Converter : JsonConverter<DateTimeOffset>
{
    public override DateTimeOffset Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        (reader.TokenType == JsonTokenType.String) && Rfc3339.TryParse(reader.GetString(), out var value)
            ? value
            : throw new JsonException("A point in time must be RFC 3339 with its offset.");

    public override void Write(Utf8JsonWriter writer, DateTimeOffset value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.WriteStringValue(value);
    }
}
