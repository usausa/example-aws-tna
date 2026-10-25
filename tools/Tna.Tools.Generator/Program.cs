using Tna.Generator;

// OpenAPI の文書から作るファイルを書く。--verify では書かずに、コミットしたファイルが最新かを確かめる
//
//   dotnet run --project tools/Tna.Tools.Generator -- [--verify]
//
// 作るもの: 要求の本文の検証 (本体と運営者) と、それが使うパターン (Tna.Contracts)
var verify = false;
foreach (var argument in args)
{
    switch (argument)
    {
        case "--verify":
            verify = true;
            break;
        default:
            Console.Error.WriteLine("Usage: [--verify]");
            return 2;
    }
}

var root = RepositoryRoot.Find();
ValidationModel[] models =
[
    ValidationModel.Read(root, "docs/api/dist/openapi.yaml", "Tna.Contracts", "ContractValidation"),
    ValidationModel.Read(root, "docs/api/dist/ops-openapi.yaml", "Tna.Contracts.Ops", "OpsContractValidation")
];
var patterns = PatternRegistry.Create(models.SelectMany(ValidationWriter.Patterns));
var succeeded = true;
succeeded &= GeneratedFile.Write(root, PatternRegistry.OutputFile, patterns.Build(), verify);
succeeded &= GeneratedFile.Write(root, "src/Tna.Contracts/Contracts/ContractValidation.g.cs", ValidationWriter.Build(models[0], patterns), verify);
succeeded &= GeneratedFile.Write(root, "src/Tna.Contracts/Contracts/Ops/OpsContractValidation.g.cs", ValidationWriter.Build(models[1], patterns), verify);
return succeeded ? 0 : 1;
