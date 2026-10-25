namespace Tna.Generator;

// 要求の本文のスキーマから作る検証
public sealed class ValidationWriterTest : IDisposable
{
    private const string Document = """
        openapi: 3.1.0
        paths:
          /items:
            post:
              requestBody:
                content:
                  application/json:
                    schema:
                      $ref: '#/components/schemas/ItemCreate'
          /animals:
            post:
              requestBody:
                content:
                  application/merge-patch+json:
                    schema:
                      $ref: '#/components/schemas/Animal'
        components:
          schemas:
            Code:
              type: string
              pattern: '^[A-Z]{3}$'
            ItemCreate:
              type: object
              required: [id, code, count, tags, detail]
              properties:
                id:
                  type: string
                  readOnly: true
                code:
                  $ref: '#/components/schemas/Code'
                count:
                  type: integer
                  minimum: 1
                  maximum: 10
                tags:
                  type: array
                  maxItems: 3
                  items:
                    type: string
                    maxLength: 5
                size:
                  type: [number, 'null']
                  multipleOf: 0.5
                detail:
                  type: object
                  required: [note]
                  properties:
                    note:
                      type: string
                      maxLength: 20
                '1':
                  type: integer
                  minimum: 0
                pet:
                  $ref: '#/components/schemas/Animal'
            Animal:
              type: object
              required: [kind]
              properties:
                kind:
                  type: string
                  enum: [dog]
              discriminator:
                propertyName: kind
                mapping:
                  dog: '#/components/schemas/Dog'
            Dog:
              allOf:
                - $ref: '#/components/schemas/Animal'
                - type: object
                  required: [bark]
                  properties:
                    bark:
                      type: string
                      maxLength: 3
        """;

    private readonly string root = Path.Combine(Path.GetTempPath(), "tna-generator-tests", Guid.NewGuid().ToString("N"));

    public ValidationWriterTest()
    {
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "spec.yaml"), Document);
    }

    [Fact]
    public void ChecksFollowTheConstraints()
    {
        var code = Build();

        Assert.Contains(".Check<ItemCreate>(CheckItemCreate)", code, StringComparison.Ordinal);
        Assert.Contains("check.Text(path + \"/code\", value.Code, true, pattern: ContractPatterns.Code());", code, StringComparison.Ordinal);
        Assert.Contains("check.Number(path + \"/count\", value.Count, minimum: 1, maximum: 10);", code, StringComparison.Ordinal);
        Assert.Contains("check.Count(path + \"/tags\", v1.Count, maxItems: 3);", code, StringComparison.Ordinal);
        Assert.Contains("check.Text(p2 + \"/\" + i3.ToString(CultureInfo.InvariantCulture), item4, true, maxLength: 5);", code, StringComparison.Ordinal);
        Assert.Contains("check.Number(path + \"/size\", value.Size, multipleOf: 0.5);", code, StringComparison.Ordinal);
        Assert.Contains("check.Text(p6 + \"/note\", v5.Note, true, maxLength: 20);", code, StringComparison.Ordinal);

        // 数字で始まる名前には、型の生成と同じく _ を前に付ける
        Assert.Contains("check.Number(path + \"/1\", value._1, minimum: 0);", code, StringComparison.Ordinal);
    }

    // 必須の項目 (読み取り専用は除く) と、プロパティのオブジェクトはプロパティから見つける
    [Fact]
    public void RequiredPropertiesAreListed()
    {
        var code = Build();

        Assert.Contains(".Required<ItemCreate>(\"code\", \"count\", \"tags\", \"detail\")", code, StringComparison.Ordinal);
        Assert.Contains(".Required((ItemCreate x) => x.Detail, \"note\")", code, StringComparison.Ordinal);
        Assert.DoesNotContain("/id", code, StringComparison.Ordinal);
    }

    // 判別の項目はクラスのプロパティではない。派生の型は基底の型を確かめ、自分の必須の項目を持つ
    [Fact]
    public void DerivedTypeChecksItsBase()
    {
        var code = Build();

        Assert.Contains(".Check<Dog>(CheckDog)", code, StringComparison.Ordinal);
        Assert.Contains("        CheckAnimal(value, check, path);", code, StringComparison.Ordinal);
        Assert.Contains(".Required<Dog>(\"bark\")", code, StringComparison.Ordinal);
        Assert.DoesNotContain("/kind", code, StringComparison.Ordinal);
        Assert.DoesNotContain("Required<Animal>", code, StringComparison.Ordinal);
    }

    // プロパティの中の種類で形が変わる値は、実行時の型で確かめる
    [Fact]
    public void NestedPolymorphicValueIsCheckedAsItsKind()
    {
        var code = Build();

        Assert.Contains("CheckAnyAnimal(v7, check, path + \"/pet\");", code, StringComparison.Ordinal);
        Assert.Contains("private static void CheckAnyAnimal(Animal value, InputCheck check, string path)", code, StringComparison.Ordinal);
        Assert.Contains("""
                        case Dog derived:
                            CheckDog(derived, check, path);
                            break;
                        default:
                            CheckAnimal(value, check, path);
                            break;
            """.ReplaceLineEndings("\r\n"), code, StringComparison.Ordinal);
    }

    // 検証が扱わない制約は、捨てずに断る
    [Theory]
    [InlineData("type: array\nuniqueItems: true\nitems:\n  type: string", "uniqueItems")]
    [InlineData("type: string\nformat: uuid", "format uuid")]
    [InlineData("type: integer\nmaxLength: 3", "maxLength")]
    [InlineData("type: object\nadditionalProperties: false\nproperties:\n  a:\n    type: string", "additionalProperties false")]
    [InlineData("oneOf:\n  - type: string\n  - type: integer", "oneOf")]
    public void UnknownConstraintIsRefused(string schema, string message)
    {
        File.WriteAllText(Path.Combine(root, "other.yaml"), $$"""
            openapi: 3.1.0
            paths:
              /things:
                post:
                  requestBody:
                    content:
                      application/json:
                        schema:
                          $ref: '#/components/schemas/Thing'
            components:
              schemas:
                Thing:
                  type: object
                  properties:
                    value:
                      {{schema.Replace("\n", "\n          ", StringComparison.Ordinal)}}
            """);

        var e = Assert.Throws<InvalidDataException>(() => ValidationModel.Read(root, "other.yaml", "Tna.Contracts", "ContractValidation"));

        Assert.Contains(message, e.Message, StringComparison.Ordinal);
    }

    // JSON Schema と同じく $ は文字列の終わり。パターンの名前は短いものを選ぶ
    [Fact]
    public void PatternsHaveOneName()
    {
        var patterns = PatternRegistry.Create([("^[0-9]{3}$", "NullableUlid"), ("^[0-9]{3}$", "Ulid"), ("^a$", "Ulid")]);

        var code = patterns.Build();

        Assert.Equal("Ulid", patterns.NameOf("^[0-9]{3}$"));
        Assert.Equal("Ulid2", patterns.NameOf("^a$"));
        Assert.Contains("[GeneratedRegex(@\"^[0-9]{3}\\z\", RegexOptions.CultureInvariant)]", code, StringComparison.Ordinal);
    }

    public void Dispose() => Directory.Delete(root, true);

    private string Build()
    {
        var model = ValidationModel.Read(root, "spec.yaml", "Tna.Contracts", "ContractValidation");
        return ValidationWriter.Build(model, PatternRegistry.Create(ValidationWriter.Patterns(model)));
    }
}
