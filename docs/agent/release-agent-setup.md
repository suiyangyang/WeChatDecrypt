# Lumine.Fairy 部署配置指引（供 Agent 使用）

> 本文档供 AI Agent（Codex / Claude / Cursor 等）在**接收方 Windows 机器**上逐步完成 Lumine.Fairy 桌面客户端的安装与配置。
> 请严格按步骤执行；每步先执行“校验”再进入下一步。凡标注【需用户确认】的环节，**先向用户确认、拿到明确答复再继续**，不得替用户假设或代填敏感信息（密码、Token、密钥）。
> 全程遵守第 2 节“安全红线”。

## 0. 适用范围与前置条件

- 操作系统：Windows 10/11 x64。
- 微信：已安装，且**接收方要分析的微信账号当前在本机保持登录**。
- 网络：本机运行 QwenPaw 时使用默认 `http://127.0.0.1:8088/`；仅当 QwenPaw 在 Docker、NAS 或其它计算机运行时，才改为实际可达地址。
- 磁盘：解压后约 1.5 GB，建议预留 3 GB 空闲。
- 本包已**自包含 .NET 运行时**，接收方**无需**安装 .NET SDK/运行时。

## 1. 包内结构

解压后顶层目录应包含：

| 项 | 说明 |
| --- | --- |
| `Lumine.Fairy.Desktop.exe` | 主程序（Avalonia 桌面客户端，系统托盘运行） |
| `tools/todolist/` | 独立的 “Todo / 日报” 应用（主程序会拉起此目录里的 `Lumine.Fairy.TodoList.App.exe`） |
| `tools/contact-management/` | 独立的“联系人管理”兼容入口（主窗口内也有内嵌联系人管理页） |
| `WeChatDataAnalysisRelease/` | 微信数据工具（Electron 客户端 + 本地解析后端），用于检测微信、获取数据库密钥、解密数据库 |
| `Docs/user/` | 面向用户的功能文档（分析、设置、Todo、QwenPaw MCP、用户准备与联系人配置等） |
| `Docs/developer/operations/` | 运维文档（运行时、第三方通知 HTTP 接口、MCP） |
| `Docs/mcp/` | QwenPaw 微信上下文 MCP 的模板文件（`server.py` 与两端配置模板） |
| `AGENT-SETUP.md` | 本文档 |
| `README.md` | 面向人的简短说明 |

主程序期望的**后端可执行文件固定路径**（相对主程序目录）：

```text
WeChatDataAnalysisRelease/WeChatDataAnalysis/resources/backend/wechat-backend.exe
```

因此**解压后不要拆散 `WeChatDataAnalysisRelease` 目录**，也尽量不要把主程序目录改成含中文/空格的长路径。

## 2. 安全红线（务必遵守）

1. **微信数据库密钥、聊天正文、QwenPaw 账号密码、各类 Token** 不得写入文档、日志、聊天、通知，不得上传到任何第三方（包括 QwenPaw 之外的渠道）。
2. 接收方必须**用自己的微信账号、在本机登录**后生成密钥；不要复用或拷贝别人机器上的 `account_keys.json`。
3. QwenPaw 登录令牌用 DPAPI 按**当前 Windows 用户**加密，存于 `%LOCALAPPDATA%\Lumine.Fairy\qwenpaw.token`，**不可跨机拷贝**；换机器需重新登录。登录用的服务器地址、用户名和密码同样以 DPAPI 加密存于同目录 `qwenpaw.credential`（用于下次启动自动登录），同样不可跨机拷贝，也不要读取或回显内容。
4. 端口 `18765`（通知接口）/ `18766`（MCP）只供本机或受信任局域网访问，**禁止**配置公网端口转发；同一台计算机使用 `127.0.0.1` 时无需额外开放防火墙。
5. 本包发布前已剔除开发机上的局域网 Token；若你在文档/代码里看到占位值，按各节说明重新生成两端一致的随机 Token。

## 3. 步骤一：解压与首启

1. 将压缩包解压到一个**固定、简短、无中文/空格**的目录，例如 `D:\Lumine.Fairy`。
2. 双击 `Lumine.Fairy.Desktop.exe` 启动。首次启动会自动初始化数据目录 `%LOCALAPPDATA%\Lumine.Fairy\`（内含 `settings.json`、数据库、日志等）。
3. 程序以**系统托盘**方式常驻；若已有一个实例在运行，再次启动会自动唤起已有的 Fairy 主窗口；若窗口尚未创建，则从托盘图标打开窗口（单实例策略）。

【校验】托盘出现 Fairy 图标；`%LOCALAPPDATA%\Lumine.Fairy\` 目录已生成。

## 4. 步骤二：准备微信数据（关键，缺这步无法分析）

后端需要 `<数据根目录>\output\account_keys.json` 才肯启动。数据根目录默认是**主程序所在目录**，可用用户环境变量 `WECHAT_TOOL_DATA_DIR` 覆盖。

1. 保持要分析的微信账号在本机登录。
2. 运行 `WeChatDataAnalysisRelease\WeChatDataAnalysis\WeChatDataAnalysis.exe`。
3. 在其**解密页面**完成：
   - 微信检测（识别到当前登录账号）；
   - 获取该账号对应的**数据库密钥**；
   - 用该账号对应的 `db_storage` 路径执行**数据库解密**。
4. 完成后应产生 `output/account_keys.json` 以及 `output/databases`（解密后的数据库）。工具界面通常会显示输出路径。
5. 让 Fairy 的后端能读到该输出：
   - 若输出就在**主程序目录**下的 `output\`，无需额外配置；
   - 若输出在别处，把**包含该 `output` 子目录的目录**设为用户环境变量：
     ```powershell
     [Environment]::SetEnvironmentVariable('WECHAT_TOOL_DATA_DIR', 'D:\WeChatData', 'User')
     ```
     （`D:\WeChatData` 换成实际“包含 output 的目录”。）
6. **完全退出 Fairy（包括系统托盘实例）**后重新启动。

【校验】
```powershell
# 若未设 WECHAT_TOOL_DATA_DIR，则检查主程序目录：
Test-Path 'D:\Lumine.Fairy\output\account_keys.json'   # 换成实际数据根目录
Get-Item 'D:\Lumine.Fairy\output\account_keys.json' | Select-Object Length   # 必须 > 0
```
若仍缺文件，Fairy 微信分析通知会给出**期望的 `account_keys.json` 绝对路径**，据此对齐数据目录再重启。

> 提示：微信后端端口固定 `10492`，由 Fairy 自动拉起，无需手动启动；若被旧实例占用，退出旧的 WeChatDataAnalysis / Remote PC 实例后再试。

## 5. 步骤三：登录 QwenPaw

1. 打开 **设置 > 微信分析 > QwenPaw 登录**。
2. `ServerUrl` 默认 `http://127.0.0.1:8088/`，这是 QwenPaw 本机默认 Console 地址；如果 QwenPaw 在 Docker、NAS 或其它计算机运行，改为 QwenPaw 实际可达的地址。
3. 【需用户确认】向用户索取 **QwenPaw 用户名 / 密码**，填入登录框。**不要在终端、日志或回复中打印密码。**
4. 点击登录；成功后从下拉选择**分析 Agent**（默认 `WeChatAnalysist`）。选择会保存，重启沿用。
5. 登录令牌自动写入 `%LOCALAPPDATA%\Lumine.Fairy\qwenpaw.token`（DPAPI 加密）。
6. 登录成功后，服务器地址、用户名、密码和分析 Agent 会一起记在本机（`qwenpaw.credential`，DPAPI 加密）；下次启动 Fairy 自动登录 QwenPaw 并恢复该 Agent，无需重新填写。

【校验】设置页显示“已连接 QwenPaw，分析 Agent：WeChatAnalysist”（或所选 Agent 名）。

## 6. 步骤四（可选）：第三方通知接口（端口 18765）

用于**局域网内其它程序**向 Fairy 推送通知 / 执行动作 / 提交问题处理结果。

- 仅本机调用：无需额外配置，`http://127.0.0.1:18765` 即可用。
- 局域网访问需要两步（都在 Fairy 所在机器操作）；QwenPaw 与 Fairy 同机时无需这些步骤：
  1. **管理员** PowerShell 注册 URL ACL（只需一次）：
     ```powershell
     netsh http add urlacl url=http://+:18765/ user="$env:USERDOMAIN\$env:USERNAME"
     netsh http show urlacl
     ```
  2. **防火墙**放行 TCP 18765，建议限制来源 IP：
     ```powershell
     New-NetFirewallRule -DisplayName 'Lumine Fairy Notification API' -Direction Inbound -Protocol TCP -LocalPort 18765 -Action Allow -Profile Private -RemoteAddress <调用方IP>
     ```
- **Bearer Token**：以 Fairy 设置页“第三方通知接口”显示的 Token 为准（当前默认 `local-dev-notification-token`），放入 `Authorization: Bearer <token>`。
- 详见 `Docs/developer/operations/third-party-notification-api.md`。
- **禁止**为 18765 配置公网端口转发。

【校验】在能访问 Fairy 的机器上：
```powershell
Invoke-RestMethod -Uri 'http://<Fairy-IP>:18765/api/v1/notifications' -Method Post -Headers @{ Authorization = 'Bearer <token>' } -ContentType 'application/json' -Body (@{ title='t'; body='b' } | ConvertTo-Json)
```

## 7. 步骤五（可选）：QwenPaw 微信上下文 MCP（端口 18766）

让 QwenPaw 在分析前查询 Fairy 本地维护的联系人/公司/项目上下文（仅脱敏名称与标签，不复制数据库、不发聊天正文）。

前置：QwenPaw 服务器能访问 Fairy 本机 TCP 18766；QwenPaw 侧装有 Python 3。QwenPaw 与 Fairy 同机时，默认使用 `127.0.0.1`，无需防火墙或 URL ACL。

1. **生成一个两端一致的随机 Token**（不要用占位值）。
2. **Fairy 侧**：同机部署时把 `Docs/mcp/fairy-side/wechat-context.local.json.example` 复制为**主程序目录**下的 `wechat-context.local.json`，填 `host: 127.0.0.1`、`port: 18766` 和该 Token；重启 Fairy 生效。QwenPaw 在其它设备时，`host` 改为 Fairy 实际可达的地址。
3. **QwenPaw 侧**：把 `Docs/mcp/qwenpaw-side/server.py` 和 `Docs/mcp/qwenpaw-side/wechat-context.local.json.example`（改名为 `wechat-context.local.json`，填**相同的 host/port/token**）放到 QwenPaw 的持久化目录；在 QwenPaw 的 **Agent > MCP** 中注册一个 **stdio** MCP，命令使用 `python`，参数指向该 `server.py`。
4. **Skill 侧**：把 `Docs/user/qwenpaw/skills/wechat-analysis-context/SKILL.md` 复制到 QwenPaw 的 `%USERPROFILE%\\.qwenpaw\\customized_skills\\wechat-analysis-context\\SKILL.md`，在 **Agent > Skills** 启用或重新加载该 Skill。
5. **审批策略**：将 `WeChatAnalysist` 的 MCP 驱动 `lumine_fairy_wechat_context` 的 `policy.default_effect` 设为 `allow`，否则分析时会停在审批等待。
4. **防火墙**：放行本机 18766（建议限 QwenPaw 来源 IP）。

【校验】
```powershell
Invoke-RestMethod -Uri 'http://127.0.0.1:18766/health' -Headers @{ Authorization = 'Bearer <token>' }
```
并在 QwenPaw MCP 管理器确认工具列表含 `resolve_wechat_context`。`matched: false` 属正常（无唯一匹配），非故障。
详见 `Docs/user/qwenpaw-wechat-context-mcp.md`。

## 8. 步骤六：功能验证

1. **联系人管理 > 微信联系人**：点同步，确认可列出联系人/群聊（只读元数据，不含正文）。
2. **微信分析**：进入“微信分析”页，确认 Agent 已连接，点“分析最近会话”，观察阶段进度（读取账号→会话→消息→发送 QwenPaw→分析→落库）。
3. **通知中心**：确认分析完成/TODO 候选等通知正常生成。
4. **Todo / 日报、联系人管理**：主窗口左侧导航，或托盘“更多 App”打开。

## 9. 端口速查

| 端口 | 组件 | 位置/说明 |
| --- | --- | --- |
| `10492` | 微信解析后端 | 本机 `127.0.0.1`，由 Fairy 自动拉起 |
| `18765` | 第三方通知 HTTP 接口 | 默认监听所有 IPv4，Bearer Token 保护 |
| `18766` | 微信上下文 MCP（可选） | 默认监听所有 IPv4，Bearer Token 保护 |
| `8088` | QwenPaw Console | 本机默认 `127.0.0.1`；远程或容器部署按实际地址调整 |

## 10. 数据与配置位置

- 应用数据根：`%LOCALAPPDATA%\Lumine.Fairy\`
  - `settings.json`（时区、分析 Agent 等）
  - `qwenpaw.token`（DPAPI 加密的登录令牌）
  - `qwenpaw.credential`（DPAPI 加密的 QwenPaw 服务器地址、用户名和密码，用于自动登录）
  - `todos.db`、`notifications.db`、`AppData\`（联系人域 SQLite、日志等）
- 微信数据：`<数据根目录>\output\account_keys.json`、`<数据根目录>\output\databases\`
  - 数据根目录 = 环境变量 `WECHAT_TOOL_DATA_DIR`（未设时为主程序所在目录）

## 11. 常见问题排查

| 现象 | 处理 |
| --- | --- |
| “缺少 account_keys.json” | 回到步骤二；确认 `WECHAT_TOOL_DATA_DIR` 指向包含 `output` 的目录，且文件 >0 字节；退出托盘后重启 |
| 10492 被占用 / 数据目录不匹配 | 退出旧的 WeChatDataAnalysis / Remote PC 实例；不自动终止未知进程 |
| 通知接口连接被拒 | 依次确认：Fairy 在运行、设置页显示已启动；跨设备调用时再检查 URL ACL、防火墙和 Fairy 实际可达 IP |
| QwenPaw 登录失败 | 本机先确认 `http://127.0.0.1:8088/` 可打开，再检查 ServerUrl、账号密码；密码勿打印 |
| QwenPaw 自动登录失败 | 服务器地址或密码已变更；在 **设置 > 微信分析** 改正后重新登录，新信息会覆盖已保存的登录信息 |
| MCP `401` | 两端 Token 不一致；`connection refused` 先查 Fairy 是否启动，再查跨设备场景的 IP/防火墙/端口 |
| 重复启动无反应 | 单实例策略，从系统托盘打开已有窗口 |
| 找不到 Todo/联系人独立应用 | 确认 `tools/todolist`、`tools/contact-management` 目录完整随包分发 |

## 12. 卸载

1. 完全退出 Fairy 与所有 WeChatDataAnalysis 后端进程。
2. 删除主程序安装目录。
3. 删除 `%LOCALAPPDATA%\Lumine.Fairy\`（含设置、令牌、登录信息、本地数据库）。
4. （可选）移除开机启动项、防火墙规则、URL ACL（`netsh http delete urlacl url=http://+:18765/`）、以及 QwenPaw 侧的 MCP 注册与文件。
