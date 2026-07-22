# 构建与发布工件规范

Chater 的发布工件必须针对以下 RID 分别构建：`win-x64`、`win-arm64`、`osx-x64`、`osx-arm64`。由于当前 Markdown 渲染依赖在 Native AOT 裁剪阶段会产生警告，CI 发布包使用自包含单文件模式，但显式关闭 Native AOT 和 trimming，以保证发布稳定性。

GitHub Actions 规则：Pull Request 只执行多平台测试；在 GitHub 中创建 Release 后，Actions 会读取该 Release 的 Tag（必须匹配 `vX.Y.Z`，例如 `v1.1.0`），构建发布包并上传到对应 Release 的附件中。每个平台的 zip 应用包都会作为 Release 附件上传。

本地回归命令：

```bash
dotnet restore Chater.sln
dotnet build Chater.sln --configuration Release --no-restore
dotnet test Chater.sln --configuration Release --no-build
dotnet restore Chater/Chater.csproj --runtime <RID> --property:Configuration=Release -p:SelfContained=true
dotnet publish Chater/Chater.csproj --configuration Release --runtime <RID> --self-contained true --no-restore
```

每次 Release 发布都必须保存对应的可执行工件、Windows `.pdb`、macOS `.dSYM`、SBOM、校验和和版本清单。Windows 产物必须签名，macOS 产物必须签名并 notarize；签名身份与 notarization 凭据只存在于 CI 密钥存储中。
