namespace Tna.Contracts;

using System.Collections.Frozen;
using System.Text.Json.Serialization.Metadata;

// 文書の要求の本文の型から作った検証 (ContractValidation) と、その型の必須の項目。関数と画面が同じ検証を使う。
// 必須の項目は、JSON の読み取りでも求める
public sealed class ContractChecks
{
    private readonly FrozenDictionary<Type, Action<object, InputCheck, string>> checks;

    private readonly FrozenDictionary<Type, FrozenSet<string>> required;

    private ContractChecks(FrozenDictionary<Type, Action<object, InputCheck, string>> checks, FrozenDictionary<Type, FrozenSet<string>> required)
    {
        this.checks = checks;
        this.required = required;
    }

    public bool Covers(Type type) => checks.ContainsKey(type);

    // 本文の誤りを、項目ごとの JSON Pointer を付けて返す。検証のない型には誤りがない
    public IReadOnlyList<ValidationIssue> Validate(object value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var check = new InputCheck();
        if (checks.TryGetValue(value.GetType(), out var validate))
        {
            validate(value, check, String.Empty);
        }

        return check.Issues;
    }

    // ソース生成の JSON のコンテキストの型の情報を直す。仕様で必須の項目は JSON になければならない。
    // ないと、数・日付・列挙が既定の値になってしまうため
    public void RequireProperties(JsonTypeInfo info)
    {
        ArgumentNullException.ThrowIfNull(info);
        if ((info.Kind != JsonTypeInfoKind.Object) || !required.TryGetValue(info.Type, out var names))
        {
            return;
        }

        foreach (var property in info.Properties)
        {
            if (names.Contains(property.Name))
            {
                property.IsRequired = true;
            }
        }
    }

    // 生成したコードが埋める
    public sealed class Builder
    {
        private readonly Dictionary<Type, Action<object, InputCheck, string>> checks = [];

        private readonly Dictionary<Type, FrozenSet<string>> required = [];

        public Builder Check<T>(Action<T, InputCheck, string> check)
            where T : class
        {
            ArgumentNullException.ThrowIfNull(check);
            checks[typeof(T)] = (value, input, path) => check((T)value, input, path);
            return this;
        }

        public Builder Required<T>(params string[] names)
            where T : class => Require(typeof(T), names);

        // プロパティのスキーマに書いたオブジェクト。型の生成が名前を付けるため、プロパティから型を見つける。
        // 式は型を推論させるためだけに受け取り、呼ばない
        // ReSharper disable once ParameterOnlyUsedForPreconditionCheck.Global
        public Builder Required<TParent, TChild>(Func<TParent, TChild?> property, params string[] names)
            where TChild : class
        {
            ArgumentNullException.ThrowIfNull(property);
            return Require(typeof(TChild), names);
        }

        // プロパティのスキーマに書いた配列の要素のオブジェクト。式は型を推論させるためだけに受け取り、呼ばない
        // ReSharper disable once ParameterOnlyUsedForPreconditionCheck.Global
        public Builder RequiredItems<TParent, TItem>(Func<TParent, IEnumerable<TItem>?> items, params string[] names)
        {
            ArgumentNullException.ThrowIfNull(items);
            return Require(typeof(TItem), names);
        }

        public ContractChecks Build() => new(checks.ToFrozenDictionary(), required.ToFrozenDictionary());

        private Builder Require(Type type, IEnumerable<string> names)
        {
            required[type] = names.ToFrozenSet(StringComparer.Ordinal);
            return this;
        }
    }
}
