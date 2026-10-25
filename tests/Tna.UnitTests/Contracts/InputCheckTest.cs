namespace Tna.Contracts;

using System.Text.RegularExpressions;

// 項目の誤りのコードと文言
public sealed partial class InputCheckTest
{
    [Theory]
    [InlineData(null, true, "REQUIRED", "必須です。")]
    [InlineData("  ", true, "REQUIRED", "必須です。")]
    [InlineData("ab", false, "TOO_SHORT", "3文字以上で入力してください。")]
    [InlineData("abcdef", false, "TOO_LONG", "5文字以内で入力してください。")]
    [InlineData("ab-1", false, "INVALID_FORMAT", "形式が正しくありません。")]
    public void TextIsChecked(string? value, bool required, string code, string message)
    {
        var check = new InputCheck();

        check.Text("/name", value, required, minLength: 3, maxLength: 5, pattern: Letters());

        var issue = Assert.Single(check.Issues);
        Assert.Equal(("/name", code, message, ValidationIssueSeverity.Error), (issue.Field, issue.Code, issue.Message, issue.Severity));
    }

    // 文字はコードポイントで数えるため、サロゲートペアの文字も1文字
    [Fact]
    public void LengthIsCountedInCharacters()
    {
        var check = new InputCheck();

        check.Text("/name", "𠮷野家", true, maxLength: 3);
        check.Text("/note", "𠮷𠮷𠮷𠮷", false, maxLength: 3);

        Assert.Equal(3, InputCheck.Length("𠮷野家"));
        Assert.Equal("/note", Assert.Single(check.Issues).Field);
    }

    [Fact]
    public void OptionalTextMayBeMissingOrEmpty()
    {
        var check = new InputCheck();

        check.Text("/a", null, false, maxLength: 5);
        check.Text("/b", String.Empty, false, pattern: Letters());

        Assert.Equal(["/b"], check.Issues.Select(static x => x.Field));
    }

    [Theory]
    [InlineData("taro@example.com", true)]
    [InlineData("taro@example", false)]
    [InlineData("taro@.example", false)]
    [InlineData("taro example@example.com", false)]
    [InlineData("@example.com", false)]
    [InlineData("taro@a@example.com", false)]
    public void EmailHasTheSimpleShape(string value, bool valid)
    {
        var check = new InputCheck();

        check.Text("/email", value, false, format: TextFormat.Email);

        Assert.Equal(valid, check.IsValid);
        Assert.All(check.Issues, static x => Assert.Equal("メールアドレスの形式が正しくありません。", x.Message));
    }

    [Theory]
    [InlineData(3000d, 0d, 2879d, null, null, "0以上2879以下で入力してください。")]
    [InlineData(-1d, 0d, null, null, null, "0以上で入力してください。")]
    [InlineData(43201d, null, 43200d, null, null, "43200以下で入力してください。")]
    [InlineData(6000d, null, null, null, 6000d, "6000未満で入力してください。")]
    [InlineData(0d, null, null, 0d, null, "0より大きい値を入力してください。")]
    public void NumberOutOfRangeSaysTheRange(double value, double? minimum, double? maximum, double? exclusiveMinimum, double? exclusiveMaximum, string message)
    {
        var check = new InputCheck();

        check.Number("/minutes", value, minimum, maximum, exclusiveMinimum, exclusiveMaximum);

        var issue = Assert.Single(check.Issues);
        Assert.Equal(("OUT_OF_RANGE", message), (issue.Code, issue.Message));
    }

    [Fact]
    public void NumberIsAMultipleOfItsStep()
    {
        var check = new InputCheck();

        check.Number("/days", 1.5, multipleOf: 0.5);
        check.Number("/days", 1.25, multipleOf: 0.5);
        check.Number("/days", null, minimum: 1);

        var issue = Assert.Single(check.Issues);
        Assert.Equal(("NOT_MULTIPLE", "0.5単位で入力してください。"), (issue.Code, issue.Message));
    }

    [Fact]
    public void ItemsAreCounted()
    {
        var check = new InputCheck();

        check.Count("/steps", 0, minItems: 1, maxItems: 7);
        check.Count("/steps", 8, minItems: 1, maxItems: 7);
        check.Count("/steps", 7, minItems: 1, maxItems: 7);

        Assert.Equal(
            [("TOO_FEW_ITEMS", "1件以上にしてください。"), ("TOO_MANY_ITEMS", "7件以内にしてください。")],
            check.Issues.Select(static x => (x.Code, x.Message)));
    }

    // JSON では列挙に数も書けるため、列挙にない数を断る
    [Fact]
    public void EnumMustBeAMember()
    {
        var check = new InputCheck();

        check.Defined("/type", PunchType.ClockIn);
        check.Defined("/type", (PunchType)99);
        check.Defined<PunchType>("/other", null);

        var issue = Assert.Single(check.Issues);
        Assert.Equal(("/type", "INVALID_VALUE", "選べない値です。"), (issue.Field, issue.Code, issue.Message));
    }

    // JSON Pointer の ~ と / を書き換える
    [Fact]
    public void KeyIsEscapedInTheJsonPointer()
    {
        Assert.Equal("a~1b~0c", InputCheck.Escape("a/b~c"));
    }

    [GeneratedRegex(@"^[a-z]+\z", RegexOptions.CultureInvariant)]
    private static partial Regex Letters();
}
