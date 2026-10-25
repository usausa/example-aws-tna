namespace Tna.Contracts;

using System.Text.Json;

// 要求の本文のスキーマから作った検証。項目ごとに JSON Pointer が付く
public sealed class ContractValidationTest
{
    private static readonly JsonSerializerOptions Options = TnaJson.CreateOptions();

    [Fact]
    public void BodyIsCheckedWithItsSchema()
    {
        var login = new LoginRequest { TenantCode = "ab!", LoginId = " ", Password = "secret" };

        var issues = ContractValidation.Checks.Validate(login);

        Assert.Equal([("/tenantCode", "INVALID_FORMAT"), ("/loginId", "REQUIRED")], issues.Select(static x => (x.Field, x.Code)));
        Assert.True(ContractValidation.Checks.Covers(typeof(LoginRequest)));
    }

    // プロパティのスキーマに書いたオブジェクトと、配列の要素
    [Fact]
    public void NestedItemsHaveTheirPointers()
    {
        var route = Read<ApprovalRoute>("""
            {
              "name": "標準",
              "isDefault": false,
              "appliesTo": { "requestTypes": ["leave"], "departmentIds": ["01JA0B1C2D3E4F5G6H7J8K9M0N", "d2"] },
              "steps": [
                { "order": 1, "approverRule": { "type": "departmentManager", "levelsUp": 0 } },
                { "order": 2, "approverRule": { "type": "departmentManager", "levelsUp": 0 } },
                { "order": 3, "approverRule": { "type": "departmentManager", "levelsUp": 0 } },
                { "order": 4, "approverRule": { "type": "departmentManager", "levelsUp": 0 } },
                { "order": 5, "approverRule": { "type": "departmentManager", "levelsUp": 0 } },
                { "order": 6, "approverRule": { "type": "departmentManager", "levelsUp": 0 } }
              ]
            }
            """);

        var issues = ContractValidation.Checks.Validate(route);

        Assert.Contains(issues, static x => (x.Field, x.Code) == ("/appliesTo/departmentIds/1", "INVALID_FORMAT"));
        Assert.Contains(issues, static x => (x.Field, x.Code) == ("/steps", "TOO_MANY_ITEMS"));
    }

    // 派生の型は、基底の型と自分のプロパティで確かめる
    [Fact]
    public void DerivedTypeIsCheckedWithItsBase()
    {
        var request = Read<RequestCreate>($$$"""
            {"type":"leave","reason":"{{{new string('a', 501)}}}","leave":{"leaveTypeId":"x","from":"2026-10-09","unit":"fullDay"}}
            """);

        var issues = ContractValidation.Checks.Validate(request);

        Assert.IsType<LeaveRequestCreate>(request);
        Assert.Equal(
            [("/leave/leaveTypeId", "INVALID_FORMAT"), ("/reason", "TOO_LONG")],
            issues.Select(static x => (x.Field, x.Code)).Order());
    }

    // プロパティの中の種類で形が変わる型の値は、実行時の型で確かめる
    [Fact]
    public void NestedDerivedTypeIsCheckedWithItsKind()
    {
        var route = Read<ApprovalRoute>("""
            {
              "name": "標準",
              "isDefault": false,
              "appliesTo": {},
              "steps": [
                { "order": 1, "approverRule": { "type": "departmentManager", "levelsUp": -1 } },
                { "order": 2, "approverRule": { "type": "specificEmployees", "employeeIds": [] } }
              ]
            }
            """);
        var request = Read<RequestActionInput>("""{"action":"resubmit","resubmission":{"type":"leave","reason":"r","leave":{"leaveTypeId":"T#1","from":"2026-10-09","unit":"fullDay"}}}""");

        Assert.Equal(
            [("/steps/0/approverRule/levelsUp", "OUT_OF_RANGE"), ("/steps/1/approverRule/employeeIds", "TOO_FEW_ITEMS")],
            ContractValidation.Checks.Validate(route).Select(static x => (x.Field, x.Code)));
        Assert.Equal(
            [("/resubmission/leave/leaveTypeId", "INVALID_FORMAT")],
            ContractValidation.Checks.Validate(request).Select(static x => (x.Field, x.Code)));
    }

    [Fact]
    public void ValidBodyHasNoErrors()
    {
        var punch = Read<PunchCreateRequest>("""{"type":"clockIn","location":{"latitude":35.68,"longitude":139.76},"note":"直行"}""");

        Assert.Empty(ContractValidation.Checks.Validate(punch));
    }

    // 応答の型には検証がない
    [Fact]
    public void TypeWithoutChecksIsAccepted()
    {
        Assert.Empty(ContractValidation.Checks.Validate(new Problem()));
        Assert.False(ContractValidation.Checks.Covers(typeof(Problem)));
    }

    private static T Read<T>(string json) => JsonSerializer.Deserialize<T>(json, Options)!;
}
