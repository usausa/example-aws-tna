namespace Tna.Contracts;

using System.Globalization;
using System.Text.Json;

using Tna.Contracts.Ops;

// 生成した型と、ソース生成のコンテキスト
public sealed class TnaJsonTest
{
    [Fact]
    public void EnumIsWrittenWithTheValueOfTheSpecification()
    {
        Assert.Equal("\"monthlyVariable\"", JsonSerializer.Serialize(WorkSystem.MonthlyVariable, TnaJsonContext.Default.WorkSystem));
        Assert.Equal(WorkSystem.MonthlyVariable, JsonSerializer.Deserialize("\"monthlyVariable\"", TnaJsonContext.Default.WorkSystem));
    }

    [Fact]
    public void DiscriminatorMayComeLast()
    {
        var request = JsonSerializer.Deserialize("""{"reason":"hospital","type":"leave"}""", TnaJsonContext.Default.RequestCreate);

        var leave = Assert.IsType<LeaveRequestCreate>(request);
        Assert.Equal("hospital", leave.Reason);
    }

    [Fact]
    public void DerivedTypeIsWrittenWithTheDiscriminator()
    {
        LoginResponse response = new AuthenticatedResult { AccessToken = "token", TokenType = "Bearer", ExpiresIn = 900, Scope = "employee" };

        using var document = JsonDocument.Parse(JsonSerializer.Serialize(response, TnaJsonContext.Default.LoginResponse));

        Assert.Equal("authenticated", document.RootElement.GetProperty("status").GetString());
        Assert.Equal("token", document.RootElement.GetProperty("accessToken").GetString());
    }

    // 運営者の API には自分のコンテキストがある。共通の型 (Problem など) は本体の型を使う
    [Fact]
    public void OperatorTypesHaveTheirOwnContext()
    {
        var tenant = JsonSerializer.Deserialize(
            """{"tenantId":"01JA0B1C2D3E4F5G6H7J8K9M0N","tenantCode":"example","name":"Example","timeZone":"Asia/Tokyo","status":"suspended"}""",
            TnaOpsJsonContext.Default.Tenant);

        Assert.Equal(TenantStatus.Suspended, tenant?.Status);
        Assert.Null(typeof(TnaOpsJsonContext).Assembly.GetType("Tna.Contracts.Ops.Problem"));
        Assert.Null(typeof(TnaOpsJsonContext).Assembly.GetType("Tna.Contracts.Ops.ValidationIssueSeverity"));
    }

    [Fact]
    public void ConfiguredOptionsUseTheContext()
    {
        var options = new JsonSerializerOptions();
        TnaJson.Configure(options);

        Assert.Equal("\"flex\"", JsonSerializer.Serialize(WorkSystem.Flex, options));
        Assert.IsType<LeaveRequestCreate>(JsonSerializer.Deserialize<RequestCreate>(
            """{"reason":"hospital","leave":{"leaveTypeId":"01JA0B1C2D3E4F5G6H7J8K9M0N","from":"2026-10-09","unit":"fullDay"},"type":"leave"}""",
            options));
    }

    // 要求の本文の必須の項目は、派生の型でも JSON になければならない
    [Theory]
    [InlineData("""{"reason":"hospital","type":"leave"}""")]
    [InlineData("""{"leave":{"from":"2026-10-09","unit":"fullDay"},"type":"leave"}""")]
    public void RequiredPropertyMustBeInTheJson(string json)
    {
        var options = TnaJson.CreateOptions();

        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<RequestCreate>(json, options));
    }

    // 運営者の API には自分の検証がある
    [Fact]
    public void OperatorOptionsRequireTheirProperties()
    {
        var options = TnaOpsJson.CreateOptions();

        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<TenantCreate>("{}", options));
    }

    // 決まった形のない JSON は JsonElement で、ソース生成のコンテキストが読み書きする
    [Fact]
    public void FreeFormJsonIsReadAndWrittenAsItIs()
    {
        using var details = JsonDocument.Parse("""{"threshold":80,"minutes":[2160,2700]}""");
        var alert = new Alert
        {
            AlertId = "01JA0B1C2D3E4F5G6H7J8K9M0N",
            Kind = AlertKind.OvertimeAgreement,
            Severity = AlertSeverity.Warning,
            Message = "message",
            Details = details.RootElement.Clone(),
            Status = AlertStatus.Open,
            CreatedAt = new DateTimeOffset(2026, 10, 11, 9, 0, 0, TimeSpan.FromHours(9))
        };

        var json = JsonSerializer.Serialize(alert, TnaJsonContext.Default.Alert);
        var read = JsonSerializer.Deserialize(json, TnaJsonContext.Default.Alert);

        Assert.Contains("""
            "details":{"threshold":80,"minutes":[2160,2700]}
            """, json, StringComparison.Ordinal);
        Assert.Equal(2700, read?.Details?.GetProperty("minutes")[1].GetInt32());
    }

    // ジョブの要約も、決まった形のない JSON
    [Fact]
    public void SummaryOfJobIsWritten()
    {
        using var summary = JsonDocument.Parse("""{"created":3,"failed":[{"row":2}]}""");
        var result = new Result { Summary = summary.RootElement.Clone() };

        var json = JsonSerializer.Serialize(result, TnaJsonContext.Default.Result);

        Assert.EndsWith("""
            "summary":{"created":3,"failed":[{"row":2}]}}
            """, json, StringComparison.Ordinal);
    }

    // 知らない項目は読み飛ばし、書き戻さない
    [Fact]
    public void UnknownPropertyIsNotWrittenBack()
    {
        var employee = JsonSerializer.Deserialize(
            """{"employeeId":"01JA0B1C2D3E4F5G6H7J8K9M0N","employeeCode":"00123","name":"山田 太郎","unknown":{"a":1}}""",
            TnaJsonContext.Default.EmployeeRef);

        var json = JsonSerializer.Serialize(employee, TnaJsonContext.Default.EmployeeRef);

        Assert.DoesNotContain("unknown", json, StringComparison.Ordinal);
        Assert.Null(typeof(EmployeeRef).GetProperty("AdditionalProperties"));
    }

    // 時点にはオフセットが要る。ないと、動いている機械のタイムゾーンで読まれてしまう
    [Theory]
    [InlineData("2026-10-11T09:00:00+09:00")]
    [InlineData("2026-10-11T00:00:00Z")]
    [InlineData("2026-10-11t00:00:00.1234567z")]
    public void PointInTimeWithOffsetIsRead(string text)
    {
        var request = JsonSerializer.Deserialize<PunchCreateRequest>(
            $$"""{"type":"clockIn","clientTime":"{{text}}"}""",
            TnaJson.CreateOptions());

        Assert.Equal(DateTimeOffset.Parse(text.ToUpperInvariant(), CultureInfo.InvariantCulture), request?.ClientTime);
        Assert.True(Rfc3339.TryParse(text, out _));
    }

    [Theory]
    [InlineData("2026-10-11T09:00:00")]
    [InlineData("2026-10-11 09:00:00+09:00")]
    [InlineData("2026-10-11T09:00+09:00")]
    [InlineData("2026-10-11")]
    public void PointInTimeWithoutOffsetIsRefused(string text)
    {
        var options = TnaJson.CreateOptions();

        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<PunchCreateRequest>(
            $$"""{"type":"clockIn","clientTime":"{{text}}"}""",
            options));
        Assert.False(Rfc3339.TryParse(text, out _));
    }

    // 運営者の API も、時点を同じように読む
    [Fact]
    public void OperatorPointInTimeNeedsItsOffset()
    {
        var options = TnaOpsJson.CreateOptions();

        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<Tenant>(
            """{"tenantId":"01JA0B1C2D3E4F5G6H7J8K9M0N","tenantCode":"example","name":"Example","timeZone":"Asia/Tokyo","status":"active","activeEmployeeCount":0,"createdAt":"2026-10-11T09:00:00"}""",
            options));
    }
}
