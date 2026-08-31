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
  "host": "192.168.1.20",
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

Fairy 默认不启动上下文接口，只有本地配置包含非空 Token 时才启动。只在同一台计算机使用时，`host` 可以填写 `127.0.0.1`；QwenPaw 在 NAS 上运行时，必须填写 Fairy Windows 计算机的局域网 IP。

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

在 QwenPaw 的 MCP 管理界面或实际部署配置中注册 stdio MCP：

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

## QwenPaw 自执行部署包

交付给具备本地文件和 MCP 管理权限的 QwenPaw 部署 Agent 时，可以提供 `lumine-fairy-wechat-analysis-skill-mcp.zip`。压缩包包含 Skill、stdio bridge、无密钥配置模板、MCP 注册模板和 `SEND_TO_QWENPAW.md` 安装任务。部署 Agent 负责将文件放入 QwenPaw 的持久化目录、注册 MCP 并执行协议验证。

压缩包不包含共享 Token；Token 必须由 QwenPaw 的安全 Secret 或配置机制写入 `wechat-context.local.json`，并与 Fairy Windows 客户端保持一致。Fairy 不提供可直接远程写入 QwenPaw Skill 或 MCP 管理配置的 API。

## Windows 网络权限

Fairy 绑定非回环地址时，Windows 可能需要 URL ACL。请在管理员 PowerShell 中执行以下命令，并替换为实际地址：

```powershell
netsh http add urlacl url=http://192.168.1.20:18766/ user="$env:USERDOMAIN\$env:USERNAME"
```

当 QwenPaw 在 NAS `192.168.1.100` 上运行时，可只允许 NAS 访问该端口：

```powershell
New-NetFirewallRule `
  -DisplayName 'Lumine Fairy WeChat Context MCP' `
  -Direction Inbound `
  -Protocol TCP `
  -LocalPort 18766 `
  -Action Allow `
  -Profile Private `
  -RemoteAddress 192.168.1.100
```

不要将 `18766` 做公网端口转发。

## 验证

健康检查：

```powershell
Invoke-RestMethod `
  -Uri 'http://192.168.1.20:18766/health' `
  -Headers @{ Authorization = 'Bearer <Token>' }
```

然后在 QwenPaw MCP 管理器中执行 `initialize`、`tools/list` 和一次 `tools/call`，应能看到：

```text
resolve_wechat_context
```

返回 `matched: false` 是合法结果，表示本地联系人管理中没有唯一映射。
