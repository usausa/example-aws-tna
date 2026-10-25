namespace Tna.Contracts;

using System.Globalization;
using System.Text.RegularExpressions;

// 項目の誤りのコード
public static class ValidationCodes
{
    public const string Required = "REQUIRED";

    public const string TooShort = "TOO_SHORT";

    public const string TooLong = "TOO_LONG";

    public const string OutOfRange = "OUT_OF_RANGE";

    public const string NotMultiple = "NOT_MULTIPLE";

    public const string InvalidFormat = "INVALID_FORMAT";

    public const string InvalidValue = "INVALID_VALUE";

    public const string TooFewItems = "TOO_FEW_ITEMS";

    public const string TooManyItems = "TOO_MANY_ITEMS";

    // 本文の JSON が壊れている、ほかの型の値がある、または必須の項目がない
    public const string InvalidBody = "INVALID_BODY";
}

// 検証が扱う文字列の形式 (OpenAPI の format)。本文の日付は日付として読む
public enum TextFormat
{
    None,

    Email,

    Uri,

    UriReference,

    Date,

    DateTime
}

// 入力 (本文または引数) の誤りを、API のコードと日本語の文言で集める。関数と画面が同じ文言を出すため。
// 生成した検証 (ContractValidation) は、項目の位置 (本文では JSON Pointer、引数では名前) を付けて呼ぶ
public sealed class InputCheck
{
    public const string RequiredMessage = "必須です。";

    public const string InvalidValueMessage = "値が正しくありません。";

    private readonly List<ValidationIssue> issues = [];

    public IReadOnlyList<ValidationIssue> Issues => issues;

    public bool IsValid => issues.Count == 0;

    // JSON Pointer (RFC 6901) の一部。~ と / を書き換える
    public static string Escape(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        return name.Replace("~", "~0", StringComparison.Ordinal).Replace("/", "~1", StringComparison.Ordinal);
    }

    // 文字列の長さを、JSON Schema と同じく文字 (Unicode のコードポイント) で数える
    public static int Length(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var length = 0;
        var runes = value.EnumerateRunes();
        while (runes.MoveNext())
        {
            length++;
        }

        return length;
    }

    public void Add(string field, string code, string message) =>
        issues.Add(new ValidationIssue { Severity = ValidationIssueSeverity.Error, Code = code, Message = message, Field = field });

    // 値のない項目。必須のときだけ誤りにする
    public void Missing(string field, bool required)
    {
        if (required)
        {
            Add(field, ValidationCodes.Required, RequiredMessage);
        }
    }

    // 文字列。必須の文字列は、空白でない文字を含まなければならない
    public void Text(
        string field,
        string? value,
        bool required,
        int? minLength = null,
        int? maxLength = null,
        Regex? pattern = null,
        TextFormat format = TextFormat.None,
        string? constant = null)
    {
        if (value is null)
        {
            Missing(field, required);
            return;
        }

        if (required && String.IsNullOrWhiteSpace(value))
        {
            Add(field, ValidationCodes.Required, RequiredMessage);
            return;
        }

        // UTF-16 の単位の数は文字の数より少なくならないため、多くの文字列は数え直さずに済む
        if ((minLength is { } min) && ((value.Length < min) || (Length(value) < min)))
        {
            Add(field, ValidationCodes.TooShort, $"{min}文字以上で入力してください。");
            return;
        }

        if ((maxLength is { } max) && (value.Length > max) && (Length(value) > max))
        {
            Add(field, ValidationCodes.TooLong, $"{max}文字以内で入力してください。");
            return;
        }

        if ((constant is not null) && !String.Equals(value, constant, StringComparison.Ordinal))
        {
            Add(field, ValidationCodes.InvalidValue, "選べない値です。");
            return;
        }

        if ((pattern is not null) && !pattern.IsMatch(value))
        {
            Add(field, ValidationCodes.InvalidFormat, "形式が正しくありません。");
            return;
        }

        if (!Matches(value, format))
        {
            Add(field, ValidationCodes.InvalidFormat, FormatMessage(format));
        }
    }

    // 数 (整数かどうかによらない)。OpenAPI 3.1 では exclusiveMinimum と exclusiveMaximum も数で書く
    public void Number(
        string field,
        double? value,
        double? minimum = null,
        double? maximum = null,
        double? exclusiveMinimum = null,
        double? exclusiveMaximum = null,
        double? multipleOf = null)
    {
        if (value is not { } number)
        {
            return;
        }

        var low = (minimum is { } a && (number < a)) || (exclusiveMinimum is { } b && (number <= b));
        var high = (maximum is { } c && (number > c)) || (exclusiveMaximum is { } d && (number >= d));
        if (low || high)
        {
            Add(field, ValidationCodes.OutOfRange, RangeMessage(minimum, maximum, exclusiveMinimum, exclusiveMaximum));
            return;
        }

        if ((multipleOf is { } step) && (Math.Abs((number / step) - Math.Round(number / step)) > 1e-9))
        {
            Add(field, ValidationCodes.NotMultiple, $"{Format(step)}単位で入力してください。");
        }
    }

    // 決まった値のどれかでなければならない文字列 (引数の列挙)
    public void OneOf(string field, string? value, IReadOnlyCollection<string> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        if ((value is not null) && !values.Contains(value, StringComparer.Ordinal))
        {
            Add(field, ValidationCodes.InvalidValue, "選べない値です。");
        }
    }

    // 列挙。JSON では列挙に数も書けるため、列挙にない数を断る
    public void Defined<T>(string field, T value)
        where T : struct, Enum
    {
        if (!Enum.IsDefined(value))
        {
            Add(field, ValidationCodes.InvalidValue, "選べない値です。");
        }
    }

    public void Defined<T>(string field, T? value)
        where T : struct, Enum
    {
        if (value is { } member)
        {
            Defined(field, member);
        }
    }

    // 配列の要素の数
    public void Count(string field, int count, int? minItems = null, int? maxItems = null)
    {
        if ((minItems is { } min) && (count < min))
        {
            Add(field, ValidationCodes.TooFewItems, $"{min}件以上にしてください。");
        }
        else if ((maxItems is { } max) && (count > max))
        {
            Add(field, ValidationCodes.TooManyItems, $"{max}件以内にしてください。");
        }
    }

    // 型に合わない引数 (経路やクエリの引数) の値
    public void InvalidValue(string field) => Add(field, ValidationCodes.InvalidValue, InvalidValueMessage);

    private static bool Matches(string value, TextFormat format) => format switch
    {
        TextFormat.Email => IsEmail(value),
        TextFormat.Uri => Uri.TryCreate(value, UriKind.Absolute, out _),
        TextFormat.UriReference => Uri.TryCreate(value, UriKind.RelativeOrAbsolute, out _),
        TextFormat.Date => DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _),
        TextFormat.DateTime => Rfc3339.TryParse(value, out _),
        _ => true
    };

    // メールアドレスの簡単な形。前の部分の後に @ が1つだけあり、空白がなく、ドメインの中に . がある
    private static bool IsEmail(string value)
    {
        var at = value.IndexOf('@', StringComparison.Ordinal);
        if ((at <= 0) || (at != value.LastIndexOf('@')) || value.Any(Char.IsWhiteSpace))
        {
            return false;
        }

        var domain = value.AsSpan(at + 1);
        var dot = domain.IndexOf('.');
        return (dot > 0) && (dot < domain.Length - 1);
    }

    private static string FormatMessage(TextFormat format) => format switch
    {
        TextFormat.Email => "メールアドレスの形式が正しくありません。",
        TextFormat.Uri or TextFormat.UriReference => "URL の形式が正しくありません。",
        TextFormat.Date => "日付の形式が正しくありません。",
        TextFormat.DateTime => "日時の形式が正しくありません。",
        _ => "形式が正しくありません。"
    };

    private static string RangeMessage(double? minimum, double? maximum, double? exclusiveMinimum, double? exclusiveMaximum)
    {
        var low = minimum is { } a ? $"{Format(a)}以上" : exclusiveMinimum is { } b ? $"{Format(b)}より大きく" : null;
        var high = maximum is { } c ? $"{Format(c)}以下" : exclusiveMaximum is { } d ? $"{Format(d)}未満" : null;
        return (low, high) switch
        {
            (null, null) => InvalidValueMessage,
            (_, null) when exclusiveMinimum is not null && minimum is null => $"{Format(exclusiveMinimum.Value)}より大きい値を入力してください。",
            (_, null) => $"{low}で入力してください。",
            (null, _) => $"{high}で入力してください。",
            _ => $"{low}{high}で入力してください。"
        };
    }

    private static string Format(double number) => number.ToString("0.##########", CultureInfo.InvariantCulture);
}
