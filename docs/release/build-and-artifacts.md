# 构建与发布工件规范

Chater 的发布工件必须针对以下 RID 分别构建：`win-x64`、`win-arm64`、`osx-x64`、`osx-arm64`。Markdown 使用开源的 `LiveMarkdown.Avalonia` 渲染，并保持 Native AOT 和 trimming 兼容。

GitHub Actions 规则：Pull Request 只执行多平台测试；推送严格匹配 `vX.Y.Z`（例如 `v1.1.0`）的新 Tag 后，Actions 会构建发布包、自动创建对应的 GitHub Release，并将每个平台的 zip 应用包上传为 Release 附件。Release 发布包不包含 PDB 调试文件。

本地回归命令：

```bash
dotnet restore Chater.sln
dotnet build Chater.sln --configuration Release --no-restore
dotnet test Chater.sln --configuration Release --no-build
dotnet restore Chater/Chater.csproj --runtime <RID> --property:Configuration=Release -p:SelfContained=true
dotnet publish Chater/Chater.csproj --configuration Release --runtime <RID> --self-contained true --no-restore
```

每次 Release 发布都必须保存对应的可执行工件、Windows `.pdb`、macOS `.dSYM`、SBOM、校验和和版本清单。Windows 产物必须签名，macOS 产物必须签名并 notarize；签名身份与 notarization 凭据只存在于 CI 密钥存储中。
