# HealthAutoArrange 1.1.8

- 移除 updater 的 RSA-2048 签名校验。1.1.7 已是仅通知模式（不下载、不解压、不替换任何 DLL），加上此前签名私钥已在分发过程中泄露，RSA 的实际安全价值已为零。
- 信任模型简化为：HTTPS-only + GitHub 主机白名单（`github.com` / `raw.githubusercontent.com`）+ 用户手动从 GitHub Release 下载。
- Manifest schema 升至 2，移除 `signature` / `keyId` / `assetName` / `sha256` / `size` / `officialUrl` / `mirrorUrl` 字段；保留 `schema`、`version`、`notesUrl`、`publishedUtc`。客户端解析时忽略未知字段（向后兼容 schema 1 manifest 的版本字段，但不验签）。
- 删除 `update/TRUSTED_UPDATE_PUBLIC_KEY.pem` 与 `tools/update_key_check.py`。CI 不再调用 `update_key_check.py`，改为调用 `version_check.py`。
- `release.yml` 不再要求 `UPDATE_SIGNING_PRIVATE_KEY_PEM` secret，发布流程不再签名 manifest；仍发布 `latest.txt` 到 `update-dist` 分支。
- `.gitignore` 调整：恢复 `*.pem` 防御性屏蔽；移除 `update-signing-private*.pem` 等专用规则（已无对应文件需要保留）。
- `SafeUpdater.cs` 文档注释明确说明安全模型与移除 RSA 的原因；保留所有 HTTPS / 大小上限 / 超时 / 主机白名单 / 单次启动检查的护栏。
- README 与 `update/README.md` 同步更新，标注旧公钥指纹已撤销（永不再使用）。
- 插件与程序集版本提升至 1.1.8 / 1.1.8.0。

## 兼容性

- 1.1.7 客户端无法读取 schema 2 的 manifest（强制 `schema=1`），但 1.1.7 仅为 pre-release（`prerelease: true, make_latest: false`），未在生产用户中推广。
- 1.1.8 客户端可解析 schema 1 manifest 的 `version` 字段做版本比较，但不验签；旧 manifest 中的 `notesUrl` 仍会被读取并要求为 GitHub HTTPS。
- 配置文件向前兼容；旧的 `[Updates]` 段中的 `AllowMirror` / `MirrorManifestUrl` 字段（1.1.6 残留）继续被忽略。

## 验证

- `python3 tools/static_smoke.py`：97 项契约全部通过，含新增 4 项针对 RSA 移除的负向检查。
- `python3 tools/version_check.py`：Plugin 1.1.8 / Assembly 1.1.8.0 / File 1.1.8.0 一致。
- `dotnet test HealthAutoArrange.Tests`：213 个 xUnit 测试全部通过。
- `dotnet build HealthAutoArrange.Plugin (net472)`：0 Warning, 0 Error。
