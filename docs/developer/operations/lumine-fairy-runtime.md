# Lumine.Fairy 运行维护

## 微信后端数据目录

桌面客户端启动 `WeChatDataAnalysisRelease` 发布版后端时，会优先使用环境变量 `WECHAT_TOOL_DATA_DIR`；未配置时，从应用当前目录和程序目录向上查找包含 `output/account_keys.json` 或 `output/databases` 的工作区目录。

后端启动环境会同时设置：

- `WECHAT_TOOL_DATA_DIR`：工作区数据根目录；
- `WECHAT_TOOL_OUTPUT_DIR`：工作区下的 `output` 目录；
- `WECHAT_TOOL_PORT`：本地端口 `10492`。

客户端不会仅凭 `/api/health` 判断后端可用，还会检查聊天账号列表和当前数据目录对应的日志位置。

## 单实例策略

`Lumine.Fairy.Desktop` 使用全局 Windows 互斥量保证同一用户会话只有一个桌面实例。重复启动时显示提示并退出，用户应从已有实例的系统托盘打开窗口。

微信后端遵循以下策略：

1. 已有后端健康、聊天账号已加载且数据目录正确时，客户端复用该实例。
2. 端口被后端占用但聊天账号未加载或数据目录不匹配时，客户端不强制终止未知进程，显示后端日志位置、期望数据目录以及“退出旧 WeChatDataAnalysis/Remote PC 实例后重试”的处理策略。
3. 没有可用后端时，客户端只在自身实例内串行启动一个后端，并在启动完成前验证聊天账号是否可用。

后端退出由启动它的客户端实例负责；未知来源的后端进程需要用户确认后再退出。
