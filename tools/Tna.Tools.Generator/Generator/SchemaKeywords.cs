namespace Tna.Generator;

using YamlDotNet.RepresentationModel;

// 生成した検証が扱えるスキーマのキーワード。検証が扱わない制約は確かめないまま捨てられてしまうため、生成の道具が断る
internal static class SchemaKeywords
{
    // 値を縛らないキーワード
    private static readonly HashSet<string> Annotations =
    [
        with(StringComparer.Ordinal),
        "$ref", "type", "description", "title", "examples", "example", "default", "deprecated", "readOnly", "writeOnly", "externalDocs"
    ];

    // 要求の本文の値のキーワード (スキーマの種類ごと)
    private static readonly Dictionary<string, HashSet<string>> BodyKeywords = new(StringComparer.Ordinal)
    {
        ["reference"] = [],
        ["oneOf"] = ["oneOf"],
        ["string"] = ["enum", "const", "format", "minLength", "maxLength", "pattern"],
        ["integer"] = ["enum", "format", "minimum", "maximum", "exclusiveMinimum", "exclusiveMaximum", "multipleOf"],
        ["number"] = ["enum", "format", "minimum", "maximum", "exclusiveMinimum", "exclusiveMaximum", "multipleOf"],
        ["boolean"] = [],
        ["array"] = ["items", "minItems", "maxItems"],
        ["object"] = ["properties", "required", "additionalProperties", "allOf", "discriminator"],
        ["null"] = [],
        ["any"] = []
    };

    private static readonly Dictionary<string, HashSet<string>> BodyFormats = new(StringComparer.Ordinal)
    {
        ["string"] = ["date", "date-time", "binary", "email", "uri", "uri-reference"],
        ["integer"] = ["int32", "int64"],
        ["number"] = ["float", "double"]
    };

    // 要求の本文の値のスキーマ
    public static void RequireKnownInBody(YamlMappingNode schema, string where)
    {
        var kind = KindOf(schema);
        RequireKnown(schema, kind, BodyKeywords[kind], BodyFormats.GetValueOrDefault(kind), where);

        // 知らない項目は読んで捨てるため、断る検証はない
        if (Yaml.Scalar(schema, "additionalProperties") == "false")
        {
            throw new InvalidDataException($"{where}: additionalProperties false is not checked, since the unknown properties are dropped");
        }

        // いくつかのスキーマのどれかになる値には、検証がない
        if (Yaml.Sequence(schema, "oneOf") is { } alternatives &&
            (alternatives.Children.OfType<YamlMappingNode>().Count(static x => Yaml.Scalar(x, "type") != "null") != 1))
        {
            throw new InvalidDataException($"{where}: oneOf must be one schema and null");
        }
    }

    // 参照、oneOf、null を除いた型、型のないプロパティか allOf のオブジェクト、どんな値でもよいもの
    private static string KindOf(YamlMappingNode schema)
    {
        if (Yaml.Child(schema, "$ref") is not null)
        {
            return "reference";
        }

        if (Yaml.Child(schema, "oneOf") is not null)
        {
            return "oneOf";
        }

        var type = Yaml.Child(schema, "type") switch
        {
            YamlScalarNode scalar => scalar.Value,
            YamlSequenceNode sequence => Yaml.Strings(sequence).FirstOrDefault(static x => x != "null") ?? "null",
            _ => null
        };
        return type ?? ((Yaml.Child(schema, "properties") is not null) || (Yaml.Child(schema, "allOf") is not null) ? "object" : "any");
    }

    private static void RequireKnown(YamlMappingNode schema, string kind, HashSet<string> keywords, HashSet<string>? formats, string where)
    {
        foreach (var keyword in schema.Children.Keys.Select(static x => ((YamlScalarNode)x).Value!))
        {
            if (!keyword.StartsWith("x-", StringComparison.Ordinal) && !Annotations.Contains(keyword) && !keywords.Contains(keyword))
            {
                throw new InvalidDataException($"{where}: the generated checks do not know {keyword} of a {kind}");
            }
        }

        if ((Yaml.Scalar(schema, "format") is { } format) && !(formats?.Contains(format) ?? false))
        {
            throw new InvalidDataException($"{where}: the generated checks do not know the format {format} of a {kind}");
        }
    }
}
