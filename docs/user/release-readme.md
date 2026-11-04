# Lumine.Fairy 桌面客户端（分发版）

微信聊天记录分析 + 联系人管理 + Todo/日报 的 Windows 桌面客户端，集成 QwenPaw 智能体。

## 这是什么

- 主程序：`Lumine.Fairy.Desktop.exe`（系统托盘常驻，单实例）。
- 能力：微信消息分析生成 TODO、联系人/公司管理、通知中心、第三方通知 HTTP 接口、QwenPaw 微信上下文 MCP（可选）。
- 已**自包含 .NET 运行时**，接收方无需安装 .NET。

## 快速开始

1. 解压到固定、简短、无中文/空格的目录，例如 `D:\Lumine.Fairy`。
2. 用 `WeChatDataAnalysisRelease` 完成**微信检测 → 获取数据库密钥 → 数据库解密**，产出 `output/account_keys.json`（详见 `AGENT-SETUP.md` 步骤二）。
3. 双击 `Lumine.Fairy.Desktop.exe` 启动，在 **设置 > 微信分析** 登录 QwenPaw 并选择分析 Agent（默认 `WeChatAnalysist`）。登录信息会被记住，下次启动自动登录。
4. 在“微信分析”页发起分析，观察阶段进度与通知中心。

## 让 AI Agent 帮你配置

如果你使用 Codex / Claude / Cursor 等 Agent：把本目录交给它，让它**阅读并执行 `AGENT-SETUP.md`**，它会逐步带你完成微信数据准备、QwenPaw 登录，以及可选的第三方通知接口与 MCP 网络配置。敏感信息（密码、Token）请由你本人确认填写。

## 安全提醒

- 微信数据库密钥、聊天正文、QwenPaw 账号密码、各类 Token **不要**外传，**不要**写入日志、文档或聊天。
- 端口 `18765`（通知接口）/ `18766`（MCP）仅限受信任局域网访问，**禁止**公网端口转发。
- 接收方需**用自己的微信账号在本机登录**并生成密钥，不要复用他人机器上的 `account_keys.json`。

## 文档

- `AGENT-SETUP.md`：给 Agent 的逐步配置指引（含命令、校验、排障、端口速查）。
- `Docs/user/release-user-configuration.md`：给用户的准备清单，含微信访问授权、QwenPaw MCP/Skill 和联系人配置步骤。
- `Docs/user/`：功能说明（微信分析、设置、Todo、QwenPaw MCP 等）。
- `Docs/developer/operations/`：运行时、第三方通知 HTTP 接口、MCP 运维文档。

## 版本与构建

- 目标平台：Windows 10/11 x64。
- 运行时：.NET 10（自包含发布）。
- 构建日期：2026-09-01。
