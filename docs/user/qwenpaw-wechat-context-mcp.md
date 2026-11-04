# QwenPaw 微信分析上下文配置

配置完成后，QwenPaw 在分析微信会话前可以查询 Fairy 中维护的联系人、公司和项目上下文。查询只返回必要的名称、项目名称、标签和匹配方式，不会复制微信数据库或联系人数据库，也不会发送聊天正文。

## 需要准备

- Fairy 与 QwenPaw 同机时使用 `127.0.0.1`；仅在跨设备部署时填写 Fairy 所在 Windows 计算机的实际可达 IP；
- QwenPaw 服务器能够访问该计算机的 TCP `18766` 端口；
- Python 3；
- QwenPaw 中负责微信分析的 Agent，当前默认是 `WeChatAnalysist`。

## 配置 Fairy

复制 [Fairy 配置模板](../../src/Lumine.Fairy.Desktop/wechat-context.local.json.example) 为：

```text
src/Lumine.Fairy.Desktop/wechat-context.local.json
```

填写以下内容：

```json
{
  "host": "127.0.0.1",
  "port": 18766,
  "token": "两端共用的随机 Token",
  "timeoutSeconds": 10
}
```

Token 只保存在本机配置文件中，不要发送到聊天、文档或 Git。构建或发布 Fairy 后，该配置会自动放到可执行文件旁边；重启 Fairy 后配置生效。

## 配置 QwenPaw MCP

复制 [MCP 配置模板](../../tools/lumine-fairy-wechat-context-mcp/wechat-context.local.json.example) 为：

```text
tools/lumine-fairy-wechat-context-mcp/wechat-context.local.json
```

将其中的 `host`、`port` 和 `token` 填成与 Fairy 配置完全相同的值，并将以下两个文件放到 QwenPaw 服务器的持久化目录：

```text
server.py
wechat-context.local.json
```

在 QwenPaw 中注册一个 stdio MCP。命令示例：

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

MCP 配置文件与 `server.py` 放在同一目录，启动时不需要额外的环境变量。

## 配置微信分析 Skill

将 [微信分析上下文 Skill](qwenpaw/skills/wechat-analysis-context/SKILL.md) 复制到 QwenPaw 的自定义 Skill 目录：

```text
%USERPROFILE%\\.qwenpaw\\customized_skills\\wechat-analysis-context\\SKILL.md
```

然后在 QwenPaw 的 **Agent > Skills** 中启用或重新加载。该 Skill 只在微信分析需要识别联系人、公司或项目时调用 `resolve_wechat_context`，不会读取 SQLite 或聊天正文。

## 验证

在能访问 Fairy 的机器上执行：

```powershell
Invoke-RestMethod `
  -Uri 'http://127.0.0.1:18766/health' `
  -Headers @{ Authorization = 'Bearer <Token>' }
```

在 QwenPaw MCP 管理器中确认：

1. MCP 已启用；
2. `tools/list` 能看到 `resolve_wechat_context`；
3. 使用真实微信会话元数据执行一次 `tools/call`。

返回 `matched: false` 表示没有唯一匹配，不代表 MCP 故障。

## 常见问题

| 现象 | 处理方式 |
| --- | --- |
| `connection refused` | 检查 Fairy 是否运行、IP/端口是否正确，以及 Windows 防火墙 |
| `401 unauthorized` | 检查两份配置中的 Token 是否完全一致 |
| `Access is denied` | 同机部署先确认 Fairy 正在运行；跨设备部署时再为 Fairy 的实际 IP 和端口添加 Windows URL ACL |
| MCP 没有工具 | 检查 `server.py` 与 `wechat-context.local.json` 是否在同一目录，并确认 MCP 使用 `stdio` |
| `matched: false` | 检查本地联系人、微信会话 ID、公司和项目资料；不要自行猜测归属 |

Fairy 端和 MCP 端的本地配置文件均已加入 Git 忽略规则，上传或提交时不会包含 Token。
