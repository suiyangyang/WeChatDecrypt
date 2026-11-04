# AGENTS.md

## 项目说明

本工作区用于微信聊天记录解析、Avalonia 本地通知客户端及 QwenPaw 集成开发。

## 发布流程

- Windows x64 发布必须按 [发布顺序文档](docs/developer/operations/release-publish-order.md) 执行；该文档是后续重新发布的固定入口。
- 发布包内 Agent 配置指引的源文件是 [release-agent-setup.md](docs/agent/release-agent-setup.md)，组装时复制为包根目录的 `AGENT-SETUP.md`。

## Shell 与中文编码

- 本环境 shell 控制台默认 GBK，经管道输出的中文会乱码或报 UnicodeEncodeError。搜索、读取文本优先使用 `rg`、`git diff`（输出原始 UTF-8，不受代码页影响）。
- 不要通过 `python -c` 内联参数或命令行参数传递含中文的内容（shell 参数编码会损坏字节）；`apply_patch` 的 heredoc 已验证可保真传递 UTF-8，文件的检查与编辑优先走 `apply_patch` / `edit-file` skill 工具通道，含中文的上下文可直接匹配，无需降级为脚本替换。
- 必须用 Python/PowerShell 输出中文时先切换 UTF-8：Python 用 `PYTHONIOENCODING=utf-8` 或 `sys.stdout.buffer.write`；PowerShell 用 `[Console]::OutputEncoding = [Text.Encoding]::UTF8`。
- 命令输出出现中文乱码时，禁止依据乱码内容做判断或构造替换文本，先换 UTF-8 安全方式重新读取确认。

## QwenPaw 连接信息

- 服务器地址：`http://127.0.0.1:8088/`（QwenPaw 本地默认地址）
- 用户名环境变量：`QwenPawUser`
- 密码环境变量：`QwenPawPwd`
- 密码值不得写入文档、源码、日志、测试输出或提交记录。
- 调试时优先使用环境变量读取凭据，不要要求用户在聊天中粘贴密码。

## 数据安全

- 微信数据库、解密密钥和聊天正文仅用于用户本人授权的数据处理。
- 未经用户确认不得向 QwenPaw 或其他第三方上传聊天正文。
- 日志中不得记录密码、Token、微信数据库密钥或完整聊天内容。
