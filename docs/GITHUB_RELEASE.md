# 上传 GitHub 与发布 v1.2-preview

## 上传源码

1. 在 GitHub 新建空仓库，例如 `LMUDualSenseHaptics`。
2. 解压源码包，将 **LMUDualSenseHaptics 文件夹内的内容**放在仓库根目录，确保 `.github`、`.gitignore`、`.gitattributes` 也上传。根目录应直接包含 `README.md`、`LMUDualSenseHaptics.sln`、`src`、`tests` 和 `scripts`。
3. 提交后，仓库的 Actions → “Windows build and release” 将构建 Windows x64 应用并运行 C# 回归测试。可在成功运行页面下载名为 `windows-x64-v1.2-preview` 的构建产物；解压该产物后取得应用 ZIP。

命令行上传时可执行以下步骤，并填入你自己仓库的实际地址：

```powershell
git init
git add .
git commit -m "Release v1.2-preview"
git branch -M main
git remote add origin https://github.com/YOUR_USERNAME/LMUDualSenseHaptics.git
git push -u origin main
```

## 发布预览版

在源码提交已上传且构建通过后，推送版本标签：

```powershell
git tag v1.2-preview
git push origin v1.2-preview
```

标签构建会先核对标签与项目版本一致，再构建、测试和打包；通过后创建带有 Pre-release 标记且不设为 Latest 的 GitHub Release，附上自包含应用 ZIP 和 SHA-256 校验文件。发布说明来自 `docs/RELEASE_NOTES.md`。工作流仅在标签发布步骤获得写入 Release 的权限。

也可在本机运行 `build.cmd`，随后在 GitHub Releases 手动新建 `v1.2-preview`，上传 `artifacts/LMUDualSenseHaptics-win-x64-v1.2-preview.zip` 和对应 `.sha256`，粘贴发布说明。

## 后续版本

修改 `src/LMUDualSenseHaptics/LMUDualSenseHaptics.csproj` 的 `Version`（本版 1.2.0-preview）和 `ReleaseTag`（本版 v1.2-preview），同步窗口标题、README、CHANGELOG 和发布说明，再推送相同版本的 `vX.Y.Z` 标签。构建脚本从项目读取版本号，自动命名应用 ZIP。

## 源码与应用的分发内容

- **仓库**保留源码、测试及合成测试样本、构建脚本、用户和技术资料、MIT 与第三方许可。
- **应用 ZIP**包含程序及运行时、README、使用指南、发布说明和第三方许可。
- 编译缓存、个人配置、完整驾驶日志及旧版开发报告不提交到仓库。

## 从预览分支发布

`preview/v1.2` 分支只有提交说明明确包含 `[publish-preview]` 时才进入发布任务；仍须先通过完整构建、回归及回放。发布使用当前已验证的提交创建 `v1.2-preview` 标签，不改动 main。已有同名 Release 时创建失败，不移动或覆盖旧标签。
