# Lumine.Fairy 运行维护

## 微信后端数据目录

桌面客户端启动 `WeChatDataAnalysisRelease` 发布版后端时，会优先使用环境变量 `WECHAT_TOOL_DATA_DIR`；未配置时，使用当前运行程序目录作为数据根目录。后端使用的数据文件固定放在数据根目录的 `output` 子目录中。

运行目录下必须存在 `output/account_keys.json`。如果缺少该文件，客户端会在微信增量分析通知中显示获取密钥、完成解密和重新启动 Fairy 的具体步骤；不会启动一个没有数据的后端继续重试。

后端启动环境会同时设置：

- `WECHAT_TOOL_DATA_DIR`：数据根目录；
- `WECHAT_TOOL_OUTPUT_DIR`：数据根目录下的 `output` 目录；
- `WECHAT_TOOL_PORT`：本地端口 `10492`。

解析后端附带的只读 WCDB native 组件有构建有效期。若数据文件存在而启动诊断为 `NativeCorePolicyError`，检查 `resources/backend/native/wechatdb_native_build.json` 的 `buildExpiresAtUnix`；组件过期后重启不能恢复，需要从上游正式发行包更新整个 `resources/backend` 目录，并核验发行包 SHA-256。

客户端不会仅凭 `/api/health` 判断后端可用，还会检查聊天账号列表和当前数据目录对应的日志位置。启动后端后最多等待 60 秒；超时提示会报告最近一次未通过的就绪检查、密钥文件和解密数据库是否存在。若后端进程提前退出，提示会显示退出码和日志目录，并从标准输出中提取固定错误类别或常见异常类型；原始输出不会写入通知或 Fairy 日志，以免泄露本地聊天数据。

后端连接失败时，客户端会读取 `wechat-backend.exe` 的文件版本和 `native/wechatdb_native_build.json` 中的 WCDB 构建有效期，并向上游 GitHub 正式发行版接口查询最新版本。若发现上游版本较新或 WCDB 构建已过期，失败提示会建议更新并给出发行页；网络不可用、版本号无法解析或元数据缺失时会说明无法判断，并保留后端日志诊断。版本检查只在后端连接失败后触发，不会自动下载或替换后端文件。

## 第三方通知接口

桌面客户端启动时会自动启动第三方通知 HTTP 接口，默认监听 `http://+:18765/`，即本机所有 IPv4 网卡。同机调用可使用 `http://127.0.0.1:18765/api/v1/notifications`；跨设备调用时，设置页显示可供第三方程序访问的实际局域网地址。

如需只绑定某一个局域网地址，可设置用户级环境变量后重启 Fairy：

```powershell
[Environment]::SetEnvironmentVariable('LumineFairyNotificationBindAddress', '127.0.0.1', 'User')
```

非回环监听通常需要管理员注册 URL ACL，并在 Windows 防火墙中只放行受信任第三方程序所在的 IP。完整的端点、认证和防火墙配置见[第三方通知 HTTP 接口开发文档](third-party-notification-api.md)。不要为 `18765` 配置公网端口转发。

## 单实例策略

`Lumine.Fairy.Desktop` 使用全局 Windows 互斥量保证同一用户会话只有一个桌面实例。重复启动时会尝试唤起已有的 Fairy 主窗口；如果已有实例尚未创建窗口，则显示提示，用户可从系统托盘打开窗口。

微信后端遵循以下策略：

1. 已有后端健康、聊天账号已加载且数据目录正确时，客户端复用该实例。
2. 当前客户端已启动的后端未完成数据初始化时，客户端提示缺少的 `account_keys.json` 位置和获取、解密、重启步骤；未知后端占用端口且数据目录不匹配时，客户端不强制终止进程，而是显示后端日志位置和期望数据目录。
3. 没有可用后端时，客户端只在自身实例内串行启动一个后端，并在启动完成前验证聊天账号是否可用。

后端退出由启动它的客户端实例负责；未知来源的后端进程需要用户确认后再退出。

## 托盘与桌面通知

- 桌面新通知通过 Windows 托盘气泡展示；托盘菜单的“暂停通知”只停止气泡，通知仍写入通知中心。
- 点击托盘气泡会显示并激活主窗口，与双击托盘图标一致，主窗口隐藏或最小化时同样会被唤出。
- 通知动作的执行结果不再推送托盘气泡，只在主窗口顶部 Toast 和通知卡片上呈现。

## 运行日志与异常捕获

`Lumine.Fairy.Desktop` 通过 `LifecycleLog`（`Services/LifecycleLog.cs`）把进程生命周期写入 `%LOCALAPPDATA%\Lumine.Fairy\logs`，用于事后判定进程属于闪退、正常退出还是被外部结束：

- `runtime-yyyyMMdd.log`：按天分文件的运行日志，依次记录 `START`（配置、版本、是否附带控制台、可执行文件路径、工作目录、命令行）、`INFO`、`WARN`、`FATAL` 与 `EXIT`（退出原因、退出码）。
- `heartbeat.txt`：主窗口创建后每分钟原子重写一次的心跳，内含 PID 和时间戳。进程被外部强杀时不会留下 `EXIT` 行，此时用心跳文件的最后修改时间定位停止时刻。
- `crash-yyyyMMdd-HHmmss-fff-<pid>.log`：未处理异常的完整堆栈，附带线程信息和最后心跳。采集来源包括 `AppDomain.UnhandledException`、`TaskScheduler.UnobservedTaskException` 以及桌面主循环抛出到 `Main` 的异常。

启动诊断写入 `%LOCALAPPDATA%\Lumine.Fairy\logs\runtime-yyyyMMdd.log`，记录各启动阶段的开始、完成和耗时，以及数据根目录与已加载数量。失败时记录失败阶段、异常类型、`HResult`、可用的 Win32 错误码和调用栈；不记录异常消息，以免其中包含凭据或聊天内容。

本地设置缺失、无效或读取失败而回退默认配置时单独写入 `WARN`；QwenPaw 登录恢复记录连接结果，微信数据访问检查记录授权或连接状态。阶段完成表示调用已返回，需结合这些结果判断服务是否可用。

判定规则：

| 日志现象 | 结论 |
| --- | --- |
| 出现 `FATAL` 行和对应 `crash-*.log`，随后的 `EXIT` 行写明未处理异常 | 闪退（未处理异常终止进程） |
| 存在 `EXIT` 行，退出原因为托盘退出、`ShutdownRequested` 或桌面主循环正常返回 | 正常退出 |
| 没有 `EXIT` 行，只有心跳停在某个时刻 | 进程被外部结束（关闭启动它的终端、停止调试、`taskkill`、注销或断电） |

未观察的 `Task` 异常在写入 crash 日志后调用 `SetObserved()`，不再于 GC 终结阶段终止进程。日志写入失败（IO 异常、目录权限）一律忽略，不影响主流程；日志只记录进程与路径信息，不记录密码、Token 或聊天正文。判断本次实例是否由终端启动，可看 `START` 行的“控制台”字段。
