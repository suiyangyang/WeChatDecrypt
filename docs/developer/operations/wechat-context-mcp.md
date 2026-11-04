# 微信上下文 MCP 配置

本文说明局域网部署 Fairy 微信上下文 HTTP 接口和 QwenPaw stdio MCP 适配器的配置方式。

## 数据流

```text
QwenPaw 微信分析 Skill
        |
        | stdio MCP
        v
tools/lumine-fairy-wechat-context-mcp/server.py
        |
        | HTTP + Bearer Token
        v
Fairy Windows 客户端
  /api/v1/wechat-context/resolve
        |
        v
本地联系人、公司和项目数据
```

该接口只传输会话识别所需的元数据和脱敏后的业务上下文，不传输聊天正文、图片、附件、电话、邮箱、备注或数据库文件。

## 本地配置文件

两端使用相同的 JSON 字段，必须保持 `host`、`port` 和 `token` 一致：

```json
{
  "host": "127.0.0.1",
  "port": 18766,
  "token": "replace-with-shared-token",
  "timeoutSeconds": 10
}
```

Token 只写入本机配置，不要写入源码、日志、文档或提交记录。配置模板：

- Fairy：[wechat-context.local.json.example](../../../src/Lumine.Fairy.Desktop/wechat-context.local.json.example)
- MCP：[wechat-context.local.json.example](../../../tools/lumine-fairy-wechat-context-mcp/wechat-context.local.json.example)

### Fairy Windows 客户端

1. 将模板复制为 Fairy 项目目录下的 `src/Lumine.Fairy.Desktop/wechat-context.local.json`。
2. 将 `host` 改为运行 Fairy 的 Windows 计算机局域网 IP，填入随机 Token。
3. 构建或发布后，配置文件会自动复制到 Fairy 可执行文件旁边。
4. 完全退出并重新启动 Fairy。

Fairy 默认不启动上下文接口，只有本地配置包含非空 Token 时才启动。QwenPaw 与 Fairy 同机时使用 `127.0.0.1`；QwenPaw 在 Docker、NAS 或其它计算机上运行时，必须填写 Fairy Windows 计算机的实际可达地址。

### QwenPaw MCP 适配器

将以下文件复制到 QwenPaw 所在服务器的持久化目录：

```text
tools/lumine-fairy-wechat-context-mcp/server.py
tools/lumine-fairy-wechat-context-mcp/wechat-context.local.json
```

其中 `wechat-context.local.json` 由模板复制并填写，内容与 Fairy 端的 `host`、`port`、`token` 相同。启动 MCP 时不需要设置环境变量：

```text
python3 /app/working/tools/lumine-fairy-wechat-context-mcp/server.py
```

在 QwenPaw 的 **Agent > MCP** 管理界面或实际部署配置中注册 stdio MCP；同机运行时使用 `127.0.0.1:18766`：

```json
{
  "key": "lumine_fairy_wechat_context",
  "name": "lumine_fairy_wechat_context",
  "enabled": true,
  "transport": "stdio",
  "command": "python3",
  "args": [
    "/app/working/tools/lumine-fairy-wechat-context-mcp/server.py"
  ]
}
```

### 微信分析 Skill

将 `docs/user/qwenpaw/skills/wechat-analysis-context/SKILL.md` 安装到 QwenPaw 的自定义 Skill 目录：

```text
%USERPROFILE%\\.qwenpaw\\customized_skills\\wechat-analysis-context\\SKILL.md
```

然后在 **Agent > Skills** 中启用或重新加载。Skill 只负责在微信分析前调用 `resolve_wechat_context` 获取最小业务上下文，所有联系人归属修改仍由 Fairy 本地客户端完成。

### MCP 驱动审批策略

`WeChatAnalysist` 每轮分析都会调用 `resolve_wechat_context`。该驱动的 `policy.default_effect` 必须是 `allow`；若为 `ask`，每次调用都会被 QwenPaw 审批等待拦住（默认 5 分钟超时，等待期间不产生流式事件），Fairy 客户端会先以“QwenPaw 流式响应连续 120 秒没有新数据”报失败。

- 驱动卡路径：`workspaces/WeChatAnalysist/drivers/mcp/lumine_fairy_wechat_context.yaml`（容器内为 `/app/working/workspaces/...`）
- 重新注册 MCP 或升级 QwenPaw 后，如果该策略被重置为 `ask`，需要恢复为 `allow`，并跑一轮分析确认无新的 `Generic approval pending created` 日志

## QwenPaw 自执行部署包

交付给具备本地文件和 MCP 管理权限的 QwenPaw 部署 Agent 时，可以提供 `lumine-fairy-wechat-analysis-skill-mcp.zip`。压缩包包含 Skill、stdio bridge、无密钥配置模板、MCP 注册模板和 `SEND_TO_QWENPAW.md` 安装任务。部署 Agent 负责将文件放入 QwenPaw 的持久化目录、注册 MCP 并执行协议验证。

压缩包不包含共享 Token；Token 必须由 QwenPaw 的安全 Secret 或配置机制写入 `wechat-context.local.json`，并与 Fairy Windows 客户端保持一致。Fairy 不提供可直接远程写入 QwenPaw Skill 或 MCP 管理配置的 API。

## Windows 网络权限

Fairy 绑定非回环地址时，Windows 可能需要 URL ACL。请在管理员 PowerShell 中执行以下命令，并替换为实际地址：

```powershell
netsh http add urlacl url=http://<Fairy-IP>:18766/ user="$env:USERDOMAIN\$env:USERNAME"
```

当 QwenPaw 在其它设备上运行时，可只允许该设备的实际 IP 访问该端口：

```powershell
New-NetFirewallRule `
  -DisplayName 'Lumine Fairy WeChat Context MCP' `
  -Direction Inbound `
  -Protocol TCP `
  -LocalPort 18766 `
  -Action Allow `
  -Profile Private `
  -RemoteAddress <QwenPaw-IP>
```

不要将 `18766` 做公网端口转发。

## 验证

健康检查：

```powershell
Invoke-RestMethod `
  -Uri 'http://127.0.0.1:18766/health' `
  -Headers @{ Authorization = 'Bearer <Token>' }
```

然后在 QwenPaw MCP 管理器中执行 `initialize`、`tools/list` 和一次 `tools/call`，应能看到：

```text
resolve_wechat_context
```

返回 `matched: false` 是合法结果，表示本地联系人管理中没有唯一映射。
