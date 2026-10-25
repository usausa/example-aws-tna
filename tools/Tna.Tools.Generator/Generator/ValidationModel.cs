namespace Tna.Generator;

using YamlDotNet.RepresentationModel;

// 要求の本文の値が何か (スキーマの参照をたどった後)
internal enum ValueKind
{
    // 確かめない: 真偽値、日付 (日付として読む)、ファイル、自由なオブジェクト
    None,

    Text,

    Number,

    // 列挙 (文字列か数)。型と一緒に C# の列挙を作る
    Enum,

    // コンポーネントのオブジェクト (コンポーネントの名前の C# のクラス)
    Object,

    // プロパティのスキーマに書いたオブジェクト (クラスの名前は型の生成が付ける)
    InlineObject,

    Array,

    // 項目の名前が自由で、値にスキーマのあるオブジェクト
    Map
}

// 値の制約
internal sealed record ValueShape(ValueKind Kind, bool Nullable)
{
    public string? ObjectName { get; init; }

    public InlineObjectModel? Inline { get; init; }

    public ValueShape? Items { get; init; }

    public int? MinLength { get; init; }

    public int? MaxLength { get; init; }

    public string? Pattern { get; init; }

    // ContractPatterns の中のパターンの名前の候補: パターンを持つスキーマの名前、または持ち主とプロパティの名前
    public string? PatternName { get; init; }

    public string? Format { get; init; }

    public string? Constant { get; init; }

    public double? Minimum { get; init; }

    public double? Maximum { get; init; }

    public double? ExclusiveMinimum { get; init; }

    public double? ExclusiveMaximum { get; init; }

    public double? MultipleOf { get; init; }

    public int? MinItems { get; init; }

    public int? MaxItems { get; init; }
}

internal sealed record PropertyModel(string JsonName, string CSharpName, bool Required, ValueShape Shape);

internal sealed record InlineObjectModel(IReadOnlyList<PropertyModel> Properties, IReadOnlyList<string> RequiredNames);

// コンポーネントのオブジェクト。自分のプロパティと、派生の型なら基底の型 (基底と足したプロパティの allOf)
internal sealed record ObjectModel(string Name, string? BaseName, IReadOnlyList<PropertyModel> Properties, IReadOnlyList<string> RequiredNames)
{
    // 種類で形が変わる型の派生の型 (判別の項目の対応表)
    public IReadOnlyList<string> DerivedNames { get; init; } = [];
}

// 結合した OpenAPI の文書の、要求の本文のスキーマ。本文の型と、そこから届くすべての型
internal sealed class ValidationModel
{
    private readonly Dictionary<string, YamlMappingNode> schemas;

    private readonly Dictionary<string, ObjectModel> objects = [with(StringComparer.Ordinal)];

    private readonly HashSet<string> visiting = [with(StringComparer.Ordinal)];

    private ValidationModel(string file, string ns, string className, Dictionary<string, YamlMappingNode> schemas)
    {
        File = file;
        Namespace = ns;
        ClassName = className;
        this.schemas = schemas;
    }

    public string File { get; }

    public string Namespace { get; }

    public string ClassName { get; }

    // 検証のあるコンポーネントのオブジェクト
    public IReadOnlyCollection<ObjectModel> Objects => objects.Values;

    public static ValidationModel Read(string root, string file, string ns, string className)
    {
        var document = Yaml.Load(Path.Combine(root, file));
        var components = Yaml.Mapping(Yaml.Mapping(document, "components"), "schemas") ?? throw new InvalidDataException($"No schemas: {file}");
        var schemas = components.Children.ToDictionary(static x => ((YamlScalarNode)x.Key).Value!, static x => (YamlMappingNode)x.Value, StringComparer.Ordinal);
        var model = new ValidationModel(file, ns, className, schemas);

        // JSON と JSON Merge Patch の本文 (部分更新は、適用した結果をリソースのスキーマで確かめる)
        foreach (var name in RequestBodies(document).Order(StringComparer.Ordinal))
        {
            model.AddObject(name);
        }

        return model;
    }

    // 生成したクラスのプロパティの名前。型の生成と同じ付け方にする (数字で始まる名前には _ を前に付ける)
    public static string PascalCase(string name) => Char.IsAsciiDigit(name[0]) ? "_" + name : Char.ToUpperInvariant(name[0]) + name[1..];

    private static IEnumerable<string> RequestBodies(YamlMappingNode document)
    {
        var paths = Yaml.Mapping(document, "paths") ?? throw new InvalidDataException("No paths");
        foreach (var item in paths.Children.Values.OfType<YamlMappingNode>())
        {
            foreach (var operation in item.Children.Values.OfType<YamlMappingNode>())
            {
                var content = Yaml.Mapping(Yaml.Mapping(operation, "requestBody"), "content");
                foreach (var media in Yaml.Entries(content))
                {
                    var type = ((YamlScalarNode)media.Key).Value!;
                    if (type.EndsWith("json", StringComparison.Ordinal) && Yaml.Reference(Yaml.Mapping((YamlMappingNode)media.Value, "schema")) is { } name)
                    {
                        yield return name;
                    }
                }
            }
        }
    }

    private static bool IsObject(YamlMappingNode schema) =>
        (Yaml.Child(schema, "properties") is not null) ||
        (Yaml.Child(schema, "allOf") is not null) ||
        ((Yaml.Scalar(schema, "type") == "object") && (Yaml.Child(schema, "additionalProperties") is null) && (Yaml.Child(schema, "enum") is null));

    // null を除いたスキーマの型 ("type: [string, 'null']" は null を許す文字列)
    private static (string? Type, bool Nullable) TypeOf(YamlMappingNode schema) =>
        Yaml.Child(schema, "type") switch
        {
            YamlScalarNode scalar => (scalar.Value, scalar.Value == "null"),
            YamlSequenceNode sequence => (Yaml.Strings(sequence).FirstOrDefault(static x => x != "null"), Yaml.Strings(sequence).Contains("null")),
            _ => (null, false)
        };

    // コンポーネントのオブジェクトと、その基底の型とプロパティの型を、1回だけ加える
    private void AddObject(string name)
    {
        if (objects.ContainsKey(name) || !visiting.Add(name))
        {
            return;
        }

        var schema = schemas[name];
        SchemaKeywords.RequireKnownInBody(schema, name);
        string? baseName = null;
        var own = schema;
        if (Yaml.Sequence(schema, "allOf") is { } parts)
        {
            var references = parts.Children.OfType<YamlMappingNode>().Select(Yaml.Reference).OfType<string>().ToList();
            var inline = parts.Children.OfType<YamlMappingNode>().Where(static x => Yaml.Reference(x) is null).ToList();
            if ((references.Count != 1) || (inline.Count > 1))
            {
                throw new InvalidDataException($"{name}: a derived type must be allOf with one base and one object");
            }

            baseName = references[0];
            own = inline.SingleOrDefault() ?? [];
            SchemaKeywords.RequireKnownInBody(own, name);
            AddObject(baseName);
        }

        // 種類で形が変わる型の判別の項目は、JSON の付加情報で、クラスのプロパティではない
        var discriminator = Yaml.Scalar(Yaml.Mapping(schema, "discriminator"), "propertyName");
        var (properties, required) = ReadProperties(own, name, discriminator);
        var allRequired = baseName is null ? required : [.. objects[baseName].RequiredNames, .. required];
        var derivedNames = Yaml.Entries(Yaml.Mapping(Yaml.Mapping(schema, "discriminator"), "mapping"))
            .Select(static x => ((YamlScalarNode)x.Value).Value!)
            .Select(static x => x[(x.LastIndexOf('/') + 1)..])
            .ToList();
        objects[name] = new ObjectModel(name, baseName, properties, allRequired) { DerivedNames = derivedNames };
        visiting.Remove(name);

        // 種類で形が変わる本文の派生の型は、それぞれのスキーマで確かめる
        foreach (var derived in derivedNames)
        {
            AddObject(derived);
        }
    }

    private (List<PropertyModel> Properties, List<string> Required) ReadProperties(YamlMappingNode schema, string owner, string? discriminator)
    {
        var required = Yaml.Strings(Yaml.Child(schema, "required"));
        var properties = new List<PropertyModel>();
        var requiredNames = new List<string>();
        foreach (var (key, value) in Yaml.Entries(Yaml.Mapping(schema, "properties")))
        {
            var name = ((YamlScalarNode)key).Value!;
            var property = (YamlMappingNode)value;

            // 読み取り専用の項目は、サーバーが無視する
            if ((name == discriminator) || Yaml.Flag(property, "readOnly"))
            {
                continue;
            }

            var isRequired = required.Contains(name);
            if (isRequired)
            {
                requiredNames.Add(name);
            }

            properties.Add(new PropertyModel(name, PascalCase(name), isRequired, Shape(property, owner + PascalCase(name), false)));
        }

        return (properties, requiredNames);
    }

    // 値の形。オブジェクトか列挙への参照、または基本型の制約 (Ulid のような別名を通して合わせる)
    private ValueShape Shape(YamlMappingNode schema, string hint, bool nullable)
    {
        SchemaKeywords.RequireKnownInBody(schema, hint);
        if (Yaml.Reference(schema) is { } reference)
        {
            var target = schemas[reference];
            if (IsObject(target))
            {
                AddObject(reference);
                return new ValueShape(ValueKind.Object, nullable) { ObjectName = reference };
            }

            return Shape(target, reference, nullable);
        }

        // null を許すオブジェクトや列挙: 参照と null の oneOf
        if (Yaml.Sequence(schema, "oneOf") is { } alternatives)
        {
            var options = alternatives.Children.OfType<YamlMappingNode>().ToList();
            var value = options.Single(static x => Yaml.Scalar(x, "type") != "null");
            return Shape(value, hint, nullable || (options.Count > 1));
        }

        var (type, typeNullable) = TypeOf(schema);
        nullable |= typeNullable;
        if (Yaml.Child(schema, "enum") is not null)
        {
            return new ValueShape(ValueKind.Enum, nullable);
        }

        return type switch
        {
            "string" when Yaml.Scalar(schema, "format") is "date" or "date-time" or "binary" => new ValueShape(ValueKind.None, nullable),
            "string" => new ValueShape(ValueKind.Text, nullable)
            {
                MinLength = Yaml.Integer(schema, "minLength"),
                MaxLength = Yaml.Integer(schema, "maxLength"),
                Pattern = Yaml.Scalar(schema, "pattern"),
                PatternName = Yaml.Scalar(schema, "pattern") is null ? null : hint,
                Format = Yaml.Scalar(schema, "format") is "email" or "uri" or "uri-reference" ? Yaml.Scalar(schema, "format") : null,
                Constant = Yaml.Scalar(schema, "const")
            },
            "integer" or "number" => new ValueShape(ValueKind.Number, nullable)
            {
                Minimum = Yaml.Number(schema, "minimum"),
                Maximum = Yaml.Number(schema, "maximum"),
                ExclusiveMinimum = Yaml.Number(schema, "exclusiveMinimum"),
                ExclusiveMaximum = Yaml.Number(schema, "exclusiveMaximum"),
                MultipleOf = Yaml.Number(schema, "multipleOf")
            },
            "array" => new ValueShape(ValueKind.Array, nullable)
            {
                Items = Yaml.Mapping(schema, "items") is { } items ? Shape(items, hint + "Item", false) : new ValueShape(ValueKind.None, true),
                MinItems = Yaml.Integer(schema, "minItems"),
                MaxItems = Yaml.Integer(schema, "maxItems")
            },
            _ when Yaml.Child(schema, "properties") is not null => InlineObject(schema, hint, nullable),
            "object" when Yaml.Mapping(schema, "additionalProperties") is { } values => new ValueShape(ValueKind.Map, nullable) { Items = Shape(values, hint + "Value", false) },
            _ => new ValueShape(ValueKind.None, nullable)
        };
    }

    private ValueShape InlineObject(YamlMappingNode schema, string hint, bool nullable)
    {
        var (properties, required) = ReadProperties(schema, hint, null);
        return new ValueShape(ValueKind.InlineObject, nullable) { Inline = new InlineObjectModel(properties, required) };
    }
}
