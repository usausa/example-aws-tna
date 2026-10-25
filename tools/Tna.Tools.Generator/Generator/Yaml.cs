namespace Tna.Generator;

using System.Globalization;

using YamlDotNet.RepresentationModel;

// 結合した OpenAPI の文書の節点を読む
internal static class Yaml
{
    public static YamlMappingNode Load(string file)
    {
        var stream = new YamlStream();
        using (var reader = File.OpenText(file))
        {
            stream.Load(reader);
        }

        return (YamlMappingNode)stream.Documents[0].RootNode;
    }

    public static YamlNode? Child(YamlMappingNode? node, string key) =>
        (node is not null) && node.Children.TryGetValue(new YamlScalarNode(key), out var value) ? value : null;

    public static YamlMappingNode? Mapping(YamlMappingNode? node, string key) => Child(node, key) as YamlMappingNode;

    public static YamlSequenceNode? Sequence(YamlMappingNode? node, string key) => Child(node, key) as YamlSequenceNode;

    public static string? Scalar(YamlMappingNode? node, string key) => (Child(node, key) as YamlScalarNode)?.Value;

    // 対応表の項目。なければ空
    public static IEnumerable<KeyValuePair<YamlNode, YamlNode>> Entries(YamlMappingNode? node) => node is null ? [] : node.Children;

    public static List<string> Strings(YamlNode? node) =>
        node is YamlSequenceNode sequence ? sequence.Children.Select(static x => ((YamlScalarNode)x).Value!).ToList() : [];

    public static double? Number(YamlMappingNode? node, string key) =>
        Scalar(node, key) is { } value ? Double.Parse(value, NumberStyles.Float, CultureInfo.InvariantCulture) : null;

    public static int? Integer(YamlMappingNode? node, string key) =>
        Scalar(node, key) is { } value ? Int32.Parse(value, NumberStyles.Integer, CultureInfo.InvariantCulture) : null;

    public static bool Flag(YamlMappingNode? node, string key) =>
        Boolean.TryParse(Scalar(node, key), out var value) && value;

    // 参照の最後の部分 (#/components/schemas/Ulid なら Ulid)
    public static string? Reference(YamlMappingNode? node) =>
        Scalar(node, "$ref") is { } reference ? reference[(reference.LastIndexOf('/') + 1)..] : null;
}
