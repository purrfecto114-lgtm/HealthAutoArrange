# 1.1.6

- 保留 1.1.5-v2 的 Moodle 生命周期/重入稳定性修复，不改自动 Moodle 发现和排序核心。
- F8 改为独立编辑快照；关闭/重载时保护未保存修改。
- 保存语义改为先应用再持久化，失败状态更明确。
- 优化小窗口布局、固定 footer、tooltip 位置、更新徽标和下载进度。
- 新增签名安全 updater：RSA-SHA256 manifest、SHA-256 + size 包校验、HTTPS、响应大小上限、staging-only。
- updater 支持官方源、jsDelivr fallback，以及任意保持 `latest.txt` + `packages/<version>/<asset>` 布局的签名国内镜像。
- Release workflow 生成 signed manifest，并验证发布私钥与客户端公钥匹配。
- 新增 updater trust-key consistency checker。
