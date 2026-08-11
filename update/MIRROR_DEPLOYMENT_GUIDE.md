# HealthAutoArrange 国内镜像部署教程

本教程指导维护者把 `update-dist` 分支镜像到自己控制的国内静态源（GitCode / Gitee / 对象存储），让国内玩家通过 RSA 签名验证的 HTTPS 链路获取更新包。

> **信任模型**：镜像只换 transport，不换信任根。客户端始终用仓库内嵌的 RSA-2048 公钥验证 `latest.txt` 签名 + ZIP 的 SHA-256/size。即使镜像被劫持，攻击者没有私钥也无法签发可被接受的更新。

---

## 目录布局要求

客户端的 `MirrorManifestUrl` 必须指向一个 HTTPS URL，该 URL 返回签名过的 `latest.txt`。从该 manifest URL，客户端会推导出同源的 package URL：

```
<manifest URL 所在目录>/packages/<version>/<assetName>
```

例如 manifest URL 为 `https://my-mirror.com/haa/latest.txt`，version 为 `1.1.6`，assetName 为 `HealthAutoArrange-1.1.6.zip`，则 package URL 为：

```
https://my-mirror.com/haa/packages/1.1.6/HealthAutoArrange-1.1.6.zip
```

所以你的镜像必须保持以下目录结构：

```
/latest.txt                                    ← 签名 manifest（从 GitHub update-dist 分支复制）
/packages/
  /1.1.6/
    /HealthAutoArrange-1.1.6.zip               ← 与 GitHub Release 相同的 ZIP
  /1.1.5/
    /HealthAutoArrange-1.1.5.zip
  ...
```

---

## 方案 A：GitCode Pull 镜像（最省事）

GitCode 是 CSDN 运营的国内 Git 托管，支持从 GitHub 自动 Pull 镜像仓库的分支/标签。

### 步骤

1. **登录 GitCode**（https://gitcode.com），点击右上角「+」→「新建仓库」→「导入仓库」。

2. **填入 GitHub 仓库 URL**：`https://github.com/purrfecto114-lgtm/HealthAutoArrange`

3. **选择镜像模式**：GitCode 会自动同步所有分支（包括 `update-dist`）和标签。

4. **获取 raw URL**：
   - 进入你的 GitCode 仓库 → 切换到 `update-dist` 分支 → 找到 `latest.txt`
   - 点击「原始文件」按钮，URL 格式为：
     ```
     https://gitcode.com/<你的用户名>/HealthAutoArrange/raw/update-dist/latest.txt
     ```
   - package URL 为：
     ```
     https://gitcode.com/<你的用户名>/HealthAutoArrange/raw/update-dist/packages/1.1.6/HealthAutoArrange-1.1.6.zip
     ```

5. **配置客户端**：在游戏目录 `BepInEx/config/com.healthautoarrange.plugin.cfg` 的 `[Updates]` 段设置：
   ```ini
   [Updates]
   MirrorManifestUrl = https://gitcode.com/<你的用户名>/HealthAutoArrange/raw/update-dist/latest.txt
   AllowMirror = true
   ```

### 优点
- 零运维：GitCode 自动同步 GitHub
- 免费
- HTTPS 自带

### 缺点
- 同步频率由 GitCode 控制（通常几分钟到几小时延迟）
- raw URL 路径结构可能与 GitHub 不同（需实测）
- GitCode 的 SLA 不如自建对象存储

---

## 方案 B：Gitee Pull 镜像（国内老牌）

Gitee（码云）是国内最老牌的 Git 托管，支持从 GitHub 镜像仓库。

### 步骤

1. **登录 Gitee**（https://gitee.com），点击右上角「+」→「从 GitHub/GitLab 导入仓库」。

2. **授权并选择仓库**：`purrfecto114-lgtm/HealthAutoArrange`

3. **获取 raw URL**：
   - 切换到 `update-dist` 分支 → 找到 `latest.txt`
   - 点击「原始数据」按钮，URL 格式为：
     ```
     https://gitee.com/<你的用户名>/HealthAutoArrange/raw/update-dist/latest.txt
     ```

4. **配置客户端**：同方案 A，把 URL 换成 Gitee 的。

### 注意事项
- Gitee 免费版对仓库大小和流量有限制
- Gitee 的 Pull 镜像同步频率通常比 GitCode 快（几分钟内）
- Gitee 可能对仓库内容做审核（含二进制 ZIP 的仓库可能被标记）

---

## 方案 C：对象存储自建镜像（最可控）

适合需要稳定 SLA + 高带宽的维护者。推荐阿里云 OSS / 腾讯云 COS / 七牛云 Kodo。

### 步骤（以阿里云 OSS 为例）

1. **创建 Bucket**：
   - 地域：华东1（杭州）或华北2（北京）
   - 读写权限：公共读
   - 域名：绑定自定义域名 + HTTPS 证书（Let's Encrypt 免费）

2. **同步 update-dist 分支内容到 OSS**：

   ```bash
   # 在本地克隆 update-dist 分支
   git clone -b update-dist https://github.com/purrfecto114-lgtm/HealthAutoArrange.git haa-update-dist
   cd haa-update-dist

   # 用 ossutil 上传（需先配置 ak/sk）
   ossutil cp -r ./ oss://your-bucket/haa/ --include "*" --update
   ```

3. **配置 CDN 加速**（可选但推荐）：
   - 在 OSS 控制台绑定 CDN 域名
   - 配置缓存规则：`latest.txt` 缓存 60 秒，`packages/**` 缓存 7 天

4. **获取 URL**：
   ```
   https://your-cdn-domain.com/haa/latest.txt
   https://your-cdn-domain.com/haa/packages/1.1.6/HealthAutoArrange-1.1.6.zip
   ```

5. **配置客户端**：
   ```ini
   [Updates]
   MirrorManifestUrl = https://your-cdn-domain.com/haa/latest.txt
   AllowMirror = true
   ```

6. **自动同步**（每次发新 Release 后）：
   ```bash
   # GitHub Actions 或本地 cron
   git pull origin update-dist
   ossutil cp -r ./ oss://your-bucket/haa/ --include "*" --update
   ```

### 优点
- SLA 99.95%+
- 带宽可控（CDN 可选）
- 可自定义缓存策略

### 缺点
- 有成本（OSS 存储 + 流量费，小规模每月几元）
- 需自行维护同步脚本

---

## 方案 D：Cloudflare R2 + 自定义域名（国际+国内可达）

Cloudflare R2 提供零出口流量费的 S3 兼容存储，配合 Cloudflare CDN 可在国际和国内都达到不错的速度。

### 步骤

1. **创建 R2 bucket**：在 Cloudflare 控制台 → R2 → 创建 bucket，例如 `haa-updates`

2. **上传文件**（用 wrangler 或 rclone）：
   ```bash
   rclone copy ./update-dist/ r2:haa-updates/
   ```

3. **开启公共访问**：R2 → 自定义域名 → 绑定一个你的域名（如 `haa-mirror.yourdomain.com`）

4. **配置客户端**：
   ```ini
   [Updates]
   MirrorManifestUrl = https://haa-mirror.yourdomain.com/latest.txt
   AllowMirror = true
   ```

### 注意事项
- Cloudflare 在国内部分 ISP 可达性不稳定
- R2 免费额度：10 GB 存储 + 100 万次 Class A 操作/月
- 适合国际用户为主、国内为辅的分发场景

---

## 验证镜像是否生效

1. **浏览器直接访问 manifest URL**，应看到类似：
   ```
   schema=1
   version=1.1.6
   asset=HealthAutoArrange-1.1.6.zip
   size=60178
   sha256=5f507d7ae17f6f0d7be3aa4bf768cd3209acdde2cfed6bbdac13122ba0b4ef6e
   official=https://github.com/.../HealthAutoArrange-1.1.6.zip
   published=2026-08-11T00:45:00Z
   key_id=haa-rsa-2026-01
   sig=<base64 RSA-SHA256 signature>
   ```

2. **在游戏内 F8 设置窗口**：
   - 切换到「Updates」区域
   - 点击「Check now」
   - 状态应从 `Checking...` → `Update available: 1.1.6` 或 `Up to date`

3. **如果镜像不通**：客户端会自动 fallback 到官方 GitHub raw source + jsDelivr CDN，不会因为镜像失效而崩溃。

---

## 客户端配置完整示例

`BepInEx/config/com.healthautoarrange.plugin.cfg`：

```ini
[Updates]
# 官方源（始终启用，不可关闭）
# 自动从 raw.githubusercontent.com 获取 latest.txt

# 可选镜像源（留空则不使用镜像）
MirrorManifestUrl = https://gitcode.com/yourname/HealthAutoArrange/raw/update-dist/latest.txt

# 是否允许使用镜像（true/false）
AllowMirror = true

# 自动检查间隔（小时），0 = 仅手动检查
AutoCheckIntervalHours = 24
```

---

## 常见问题

**Q: 镜像同步延迟导致用户看到的版本比 GitHub 慢怎么办？**

A: 这是正常现象。客户端选最高已验签版本，所以即使镜像提供旧版本，也不会覆盖官方源的较新版本。用户下次检查时会自动发现新版本。

**Q: 如果我的镜像被攻击者篡改了 latest.txt 会怎样？**

A: 客户端会用内嵌的 RSA-2048 公钥验证签名。篡改后的 manifest 签名不匹配，会被拒绝。攻击者没有私钥就无法签发可被接受的 manifest。

**Q: 如果私钥泄露了怎么办？**

A: 立即发布一个新的客户端版本（如 1.1.7），内嵌新的 RSA 公钥。旧私钥签发的 manifest 对新客户端无效。同时撤销旧 Release 的 update-dist 分支内容。

**Q: 可不可以不用镜像，只靠官方源？**

A: 可以。`MirrorManifestUrl` 留空 + `AllowMirror = false` 即可。客户端会从 `raw.githubusercontent.com` + `cdn.jsdelivr.net` 两个官方源并发检查。国内用户可能遇到 GitHub raw 不稳定的情况，jsDelivr 历史上也有过大陆服务受限。
