# QwenPaw 远程 PC 控制与 Avalonia Local Agent 开发计划

## 0. 文档用途

本文件是给小模型或低上下文开发 Agent 执行的分阶段开发计划。每次只执行一个任务，完成验收后再进入下一个任务。

本计划不修改 QwenPaw 核心，不重新实现 LLM、Agent、Memory 或 Skills。QwenPaw 作为服务器端个人助手；本项目实现服务器侧 PC 控制桥、本地 Avalonia 客户端、Local Agent、微信数据适配和通知系统。

本阶段对 Codex 和 OpenCode 只实现领域模型、占位数据、任务卡片和前端交互样式，不实现真实进程启动、命令发送、任务读取、ACP/MCP 对接或业务控制。真实开发 Agent 集成列为后续阶段，不能阻塞本阶段的通知、设备连接和微信功能。

当前阶段范围补充：微信分析、QwenPaw Agent 选择、通知中心、工作页面和本机第三方通知接口属于当前范围；“远程设备”页面只保留可查看的导航占位，所有远程设备列表、连接、控制和设备管理功能暂缓，不得实现。

## 0.1 当前实现状态（2026-08-21）

当前阶段实现位于 `Lumine.Fairy/`：

- Avalonia Local Agent 已可启动，窗口关闭后隐藏到 Windows 托盘，通知通过 Windows 桌面通知区域（托盘气泡）和历史记录显示。
- 当前本机默认通过 `http://127.0.0.1:8088` 登录 QwenPaw，读取 Agent 列表，并使用配置的分析 Agent 完成实际对话。
- 已接入本机 `WeChatDataAnalysisRelease` API，使用实时数据源读取本地账号和会话最近消息，优先使用 `senderDisplayName` 识别联系人，并在必要时附带 `senderUsername`。
- 通知中心按截图风格改为左侧固定导航和右侧工作区，导航项统一宽度并使用 Stretch；左侧导航包含“通知中心”“工作”“微信分析”“联系人管理”“App”“远程设备”和“设置”，通知中心显示未读数量，内部支持全部通知/未读/已读 Tab、右上角搜索与图标化圆角 Popup 更多菜单。联系人管理直接嵌入主 App，内部保留原页面的左侧 Tab；App 页面保留联系人管理快捷入口并提供 Todo / 日报独立应用入口。托盘右键菜单使用与 Popup 一致的白底圆角阴影样式，支持打开通知中心、暂停/恢复桌面通知、全部标记已读和退出。通知列表按时间倒序分页，首次加载 100 条，滚动到底部追加下一页并使用虚拟化列表释放不可见元素，列表选中和 Hover 不改变背景。
- 已加入可配置的增量轮询调度器，默认每五分钟运行；进程内按账号/会话游标检测最新消息，发现变化后调用 QwenPaw 分析。结构化分析失败时不推进游标。
- 已完成微信工作流：分析结果中的 TODO 先写入本地候选表并生成确认通知；用户确认后转为真实 TODO，工作页从 TODO SQLite 数据库加载并显示标题、摘要、状态、优先级、负责人和截止时间。
- 微信后端启动时显式绑定工作区数据目录并校验聊天账号就绪状态；桌面客户端使用全局单实例互斥量，检测到旧后端占用端口但数据目录不匹配时提示退出旧实例后重试，不自动终止未知进程。
- Codex/OpenCode 仅保留占位/样式；远程设备仅保留占位页面，不实现真实远程控制。

当前验收截图和接口测试产物位于 `Lumine.Fairy/artifacts/`。

## 1. 最终目标

用户在外部通过服务器上的 QwenPaw Web/Channel 操作家中 PC：

- 查询 PC 在线状态；
- 查看和控制 Codex、OpenCode 等开发 Agent；
- 读取允许访问的项目文件和日志；
- 执行受限的开发任务；
- 调用本地 WeChatDataAnalysis 解析个人微信聊天；
- 经过用户确认后上传选定聊天数据给服务器上的 LLM；
- 将任务完成、失败和重要信息推送到本地 Avalonia 通知中心。

服务器端保存个人助手的会话、记忆和分析结果；本地 PC 保留微信数据库、密钥和未授权的原始数据。

## 2. 固定架构

```text
外部浏览器 / 手机
        |
        v
QwenPaw Server
  - Agent / LLM / Memory
  - Skills / MCP
  - QwenPawPcBridge MCP Server
        |
        | TLS WebSocket，PC 主动连接
        v
Avalonia Desktop
  - 托盘与 Windows Toast
  - Local Agent
  - 本地权限确认
  - 本地任务状态
        |
        +--> WeChatDataAnalysis 本地 API
        +--> Codex / OpenCode CLI、ACP 或 MCP
        +--> 允许访问的项目目录
```

### 2.1 组件职责

| 组件 | 职责 | 不负责的内容 |
|---|---|---|
| QwenPaw Server | LLM、Agent、Memory、任务规划、服务器文件 | 直接读取本地 PC 文件 |
| QwenPawPcBridge | 将 `pc.*` 工具注册到 QwenPaw，并路由到设备 | 直接执行本地命令 |
| Avalonia Desktop | 托盘、通知、设置、权限确认、Local Agent 宿主 | LLM 推理 |
| Local Agent | 设备连接、本地命令校验、任务执行、结果回传 | 决定是否允许越权操作 |
| WeChatDataAnalysis | 微信数据库解密、聊天读取和导出 | 服务器端 LLM 分析 |
| Codex/OpenCode Adapter | 启动、查询、停止开发 Agent | 代替 Codex/OpenCode 推理 |

### 2.2 MVP 进程策略

第一版将 Local Agent 嵌入 Avalonia 进程，Avalonia 关闭窗口时隐藏到托盘，进程继续运行。暂不拆分 Windows Service。

为后续拆分保留接口：

```text
Avalonia UI <-> ILocalAgentRuntime <-> Local Agent implementation
```

当需要在用户完全退出 Avalonia 后仍接受远程命令时，再将实现移动到独立 `QwenPaw.LocalAgent.exe` 或 Windows Service。

## 3. 项目目录目标

在当前工作区新增独立解决方案目录，不修改已有微信解析仓库的业务代码：

```text
Lumine.Fairy/
  Lumine.Fairy.sln
  src/
    Lumine.Fairy.Desktop/       # Avalonia UI、托盘、Toast、设置
    Lumine.Fairy.Application/   # 用例、任务编排、权限策略
    Lumine.Fairy.Domain/        # 领域模型和接口
    Lumine.Fairy.Infrastructure/# SQLite、配置、日志、进程
    Lumine.Fairy.Integrations.WeChat/
    Lumine.Fairy.Integrations.DevAgents/
    Lumine.Fairy.Protocol/      # 命令和事件 DTO
  server/
    qwenpaw-pc-bridge/             # QwenPaw MCP/设备桥，独立 Python 服务
  tests/
    Lumine.Fairy.Domain.Tests/
    Lumine.Fairy.Application.Tests/
    Lumine.Fairy.Protocol.Tests/
  docs/
```

如果仓库根目录已有用户改动，先保留现状，不移动、不删除、不重命名已有微信工具目录。

## 4. 执行规则

小模型执行时必须遵循：

1. 先读取本任务引用的文件，再修改代码。
2. 一个任务只解决一个目标。
3. 每个任务完成后运行该任务的验收命令。
4. 测试失败时先修复当前任务，不跨阶段添加功能。
5. 不把密钥、聊天正文、Cookie、完整路径写入日志。
6. 不使用任意 Shell 作为第一版远程控制 API。
7. 不修改 QwenPaw 核心源码；通过 MCP、Plugin 或外部 Bridge 集成。
8. 不在没有用户确认的情况下上传微信聊天正文。
9. 不把本地微信原始数据库同步到服务器。
10. 每次完成一个阶段后，输出“完成项、测试结果、未完成项、下一任务”。

## 5. 阶段 0：环境和边界确认

### P0-001 建立项目目录

任务：

- 创建 `Lumine.Fairy/` 和上述目录结构；
- 创建空解决方案；
- 不创建业务实现；
- 添加根目录 `README.md`，说明项目用途和禁止事项。

验收：

- `dotnet sln Lumine.Fairy.sln list` 能正常执行；
- 目录结构存在；
- 原有微信工具目录没有文件变更。

### P0-002 确认运行时版本

任务：

- 记录可用 .NET SDK 版本；
- 选择 Avalonia 版本；
- 记录 Python、QwenPaw 和 WeChatDataAnalysis 的运行方式；
- 不在此阶段安装服务器软件。

验收：

- `dotnet --info` 有输出；
- `python --version` 有输出；
- 文档中明确开发机、服务器和本地 PC 的运行时要求。

## 6. 阶段 1：领域模型和协议

### P1-001 定义设备模型

创建 `DeviceId`、`DeviceInfo`、`DeviceCapability`、`DeviceConnectionState`。

最低字段：

```text
DeviceInfo
  device_id
  display_name
  platform
  agent_version
  capabilities[]
  last_seen_at
  connection_state
```

验收：

- 所有模型有序列化测试；
- `device_id` 不使用用户名、微信号或机器名作为唯一身份；
- 空值、过长名称、非法版本号有测试覆盖。

### P1-002 定义命令和事件协议

命令统一结构：

```json
{
  "protocolVersion": 1,
  "commandId": "uuid",
  "deviceId": "device-id",
  "commandType": "pc.status",
  "requiresApproval": false,
  "issuedAt": "ISO-8601",
  "expiresAt": "ISO-8601",
  "payload": {}
}
```

事件统一结构：

```json
{
  "protocolVersion": 1,
  "commandId": "uuid",
  "eventType": "accepted|progress|result|error|cancelled",
  "occurredAt": "ISO-8601",
  "progress": 0,
  "payload": {},
  "errorCode": null,
  "errorMessage": null
}
```

第一版允许的命令：

```text
pc.status
pc.notify
pc.task.status
pc.task.cancel
pc.wechat.accounts
pc.wechat.sessions
pc.wechat.export
pc.devagent.list
pc.devagent.start
pc.devagent.status
pc.devagent.cancel
```

验收：

- 协议 DTO 可以被 .NET 和 Python 双方解析；
- 缺失 `commandId`、版本不支持、命令已过期时拒绝；
- 所有命令类型都有风险等级映射。

### P1-003 定义权限策略

风险等级：

```text
Automatic  低风险，自动执行
Confirm    需要 Avalonia 本地确认
Blocked    第一版禁止
```

默认策略：

| 命令 | 默认等级 |
|---|---|
| `pc.status` | Automatic |
| `pc.notify` | Automatic |
| `pc.task.status` | Automatic |
| `pc.wechat.accounts` | Automatic |
| `pc.wechat.sessions` | Confirm |
| `pc.wechat.export` | Confirm |
| `pc.devagent.list` | Automatic |
| `pc.devagent.start` | Confirm |
| 任意 Shell | Blocked |
| 任意文件读取 | Blocked |
| 密钥、Cookie、凭据读取 | Blocked |

验收：

- 每个命令都能得到确定的策略结果；
- 没有匹配策略时默认拒绝；
- 策略判断单元测试覆盖允许、确认和拒绝。

## 7. 阶段 2：Avalonia 本地客户端

### P2-001 创建 Avalonia 壳

任务：

- 创建主窗口；
- 创建设置页、连接状态页、通知中心占位页；
- 创建 App 页面，提供联系人管理快捷入口和 Todo / 日报应用入口；联系人管理主页面直接集成在左侧导航中；
- 实现关闭窗口时隐藏到托盘；
- 托盘菜单包含：打开通知中心、暂停/恢复通知、全部标记已读、退出。

验收：

- 双击启动后显示窗口；
- 点击关闭后进程仍在、托盘图标存在；
- 托盘“打开”可以恢复窗口；
- 托盘“退出”可以真正结束进程；
- 页面导航包含“远程设备”占位项，但该页面显示“暂未开放”，不提供真实操作。

### P2-002 实现本地通知中心

使用 SQLite 保存通知，不使用内存列表作为唯一数据源。

通知字段：

```text
Notification
  id
  title
  body
  source
  category
  severity
  created_at
  is_read
  is_archived
  payload_json
  related_task_id
```

### P2-002a 第三方通知 HTTP 接口

第三方程序通过 HTTP 接口发送通知。当前版本默认监听 Fairy 所在 Windows 计算机的所有 IPv4 网卡，允许受控的局域网访问；可通过 `LumineFairyNotificationBindAddress` 收窄到指定地址，不对公网开放；远程 QwenPaw 通知暂不通过远程设备通道实现。非回环监听需要配置 Windows URL ACL 和防火墙规则。

接口：

```text
POST /api/v1/notifications
GET  /api/v1/notifications/{notificationId}
POST /api/v1/notifications/{notificationId}/actions/{actionId}
POST /api/v1/notifications/{notificationId}/responses
```

请求示例：

```json
{
  "title": "分析完成",
  "body": "发现 3 个待办事项",
  "source": "my-third-party-app",
  "category": "work",
  "severity": "info",
  "deduplicationKey": "my-third-party-app:analysis:2026-08-21",
  "dismissPolicy": "timeout",
  "durationSeconds": 5,
  "actions": [
    { "id": "open", "label": "查看详情", "kind": "open" },
    { "id": "confirm", "label": "确认", "kind": "confirm" }
  ],
  "payload": {}
}
```

字段规则：

- `title`：必填，长度限制 128 个字符；
- `body`：可选，长度限制 4096 个字符；
- `source`：必填，用于来源筛选；
- `category`：可选，例如 `work`、`wechat`、`system`、`other`；
- `severity`：`info`、`success`、`warning`、`error`、`critical`；
- `deduplicationKey`：可选，相同值在有效期内不重复弹出；
- `payload`：可选结构化数据，不直接渲染为正文；
- `actions`：可选操作按钮，第一版只支持 `open`、`confirm`、`dismiss` 三类；
- `durationSeconds`：范围 1-86400，未填写时使用设置中的默认值；
- `dismissPolicy`：通知生命周期策略。

疑问通知使用 `notificationType: "question"` 和额外的 `question` 对象：

```json
{
  "notificationType": "question",
  "dismissPolicy": "close_or_confirm",
  "question": {
    "problem": "客户要求提前交付，但当前排期可能无法满足，应该如何处理？",
    "source": {
      "conversationId": "conversation-id",
      "participants": ["联系人 A", "联系人 B"],
      "timeRange": { "from": "ISO-8601", "to": "ISO-8601" },
      "messageRefs": ["message-id-1", "message-id-2"]
    },
    "solutions": [
      { "id": "s1", "label": "确认延期", "instruction": "向对方说明当前排期并提出新日期", "recommended": true },
      { "id": "s2", "label": "调整范围", "instruction": "先交付核心功能，延期非核心内容", "recommended": false }
    ],
    "allowCustomInput": true,
    "inputPlaceholder": "不使用推荐方式时，请输入你的处理要求"
  }
}
```

疑问通知必须包含：

- 问题是什么；
- 问题来源，即谁和谁的聊天记录；
- 来源时间范围；
- 可定位的消息引用；
- 两个或以上可选解决方案；
- 其中一个明确标记为推荐方案；
- 手动输入入口作为额外选项，不属于推荐方案列表。

用户关闭疑问通知只代表隐藏当前 Toast，不代表问题已解决；问题仍以 `pending` 状态保留在通知中心和“工作”页面。通知界面按以下顺序展示：

```text
解决方案 1（推荐）
解决方案 2
解决方案 3 ...
手动输入（不使用以上解决方案）
```

用户选择某个解决方案或提交手动输入后，客户端调用：

```text
POST /api/v1/notifications/{notificationId}/responses
Body: {
  "solutionId": "s1",
  "customInput": null
}
```

如果用户选择手动输入，则 `solutionId` 为 `null`，`customInput` 必须非空。客户端随后把原始问题、来源、全部解决方案、推荐标记和用户选择一起发送给选定的 QwenPaw Agent。QwenPaw 根据用户指示自行决定后续动作，并返回新的 TODO、通知、追问或无需操作结果。

生命周期策略：

| 值 | 行为 |
|---|---|
| `timeout` | 默认显示 N 秒后自动消失，用户也可以提前关闭 |
| `close` | 不自动消失，用户点击关闭或在通知中心删除后消失 |
| `confirm` | 不自动消失，用户点击 `confirm` 操作后消失 |
| `close_or_confirm` | 用户点击关闭或 `confirm` 操作后消失 |

接口返回：

```json
{
  "notificationId": "uuid",
  "status": "accepted",
  "createdAt": "ISO-8601",
  "expiresAt": null
}
```

HTTP 状态码：

```text
201  通知已接受并持久化
400  请求字段无效
401  Token 无效或已撤销
404  通知不存在或已过期
409  幂等键冲突且内容不一致
429  超过发送频率限制
503  本地通知服务不可用
```

接口必须在 `/api/v1` 下版本化。后续字段只能向后兼容添加，不能改变现有字段语义。

第三方通过查询接口获知通知是否仍存在、是否已确认或是否已关闭。第一版不支持任意回调 URL，避免产生 SSRF 和外部回调安全问题。

认证和限制：

- 使用 `Authorization: Bearer <notification-token>`；
- Avalonia 设置页显示当前接口实际使用的 Bearer Token，并支持密码样式的显示/隐藏；
- 当前没有独立权限 scope，本文档列出的通知端点共用同一个 Token；
- 默认监听所有 IPv4 网卡，也可通过 `LumineFairyNotificationBindAddress` 指定绑定地址；
- 限制请求频率、正文长度、操作按钮数量和 payload 大小；
- Token、请求正文和通知 payload 不写入普通日志。

功能：

- Windows 桌面通知区域托盘气泡；
- 通知中心分为“全部信息”和“未读”两个 Tab；
- 未读数量和“全部已读”操作；
- 按时间倒序；
- 已读/未读使用不同卡片样式，已读通知的“标记已读”按钮禁用；
- 关键词搜索；
- 来源和优先级筛选；
- 归档和删除；
- 点击通知跳转任务详情。

验收：

- 关闭并重启客户端后通知仍存在；
- 同一个 `notification.id` 不重复插入；
- Toast 点击可以定位历史通知；
- 第三方可以通过 Bearer Token 发送通知；
- 默认通知按设置的 N 秒自动消失；
- `close`、`confirm`、`close_or_confirm` 策略按预期保持或关闭通知；
- 重复 `deduplicationKey` 不重复弹出；
- 单元测试覆盖新增、读取、标记已读和筛选。

### P2-003 设计“工作”页面

“工作”菜单用于聚合工作相关的通知和 TODO，不是远程设备控制页，也不是完整的项目管理系统。

第一版展示：

- LLM 从微信聊天中识别出的 TODO；
- TODO 标题、摘要、优先级和状态；
- 可选截止时间；
- 来源联系人、群聊和消息时间；
- 关联的分析任务和通知；
- 工作类通知，例如分析完成、待跟进、提醒和失败；
- 按未处理、处理中、已完成、已归档筛选；
- 按时间、关键词和优先级筛选。

第一版不实现：

- 外部项目管理系统同步；
- 复杂项目、里程碑和团队协作；
- 向量语义搜索；
- 远程设备控制；
- 独立的 LLM Provider 选择。

验收：

- 左侧“工作”菜单可以打开页面；
- 页面能展示 Mock TODO 和工作通知；
- 点击 TODO 可以查看来源摘要和关联通知；
- 页面筛选不会影响通知中心的原始记录。

### P2-004 实现本地确认窗口

确认窗口必须显示：

- 请求来源；
- 操作类型；
- 目标设备；
- 数据范围；
- 是否上传数据；
- 允许一次、拒绝、记住策略三个选项。

验收：

- `Confirm` 命令不会在用户未操作时执行；
- 拒绝后服务器收到明确的拒绝事件；
- 窗口关闭等同于拒绝或保持等待，不能默认允许。

## 8. 阶段 3：Local Agent 本地运行时（远程设备通道暂缓）

当前版本保留 Local Agent 作为本地微信适配、通知和任务运行时，但不实现服务器主动控制 PC 的设备通道。P3-002 TLS WebSocket 客户端和 P3-003 远程命令路由暂缓，不得执行；P3-001 只实现 QwenPaw 用户名密码登录。

当前微信分析只通过用户配置的 QwenPaw HTTPS API 获取 Agent 列表、提交分析任务和查询任务状态，不建立通用远程设备连接。

### P3-001 实现 QwenPaw 用户名密码登录

当前版本不使用配对码。Local Agent 在 Avalonia 设置页提供：

- QwenPaw Server 地址；
- 用户名；
- 密码；
- 登录、退出登录和重新登录。

登录流程使用 QwenPaw 的认证 API：

```text
POST /api/auth/login
Body: { "username": "...", "password": "..." }
Result: { "token": "...", "username": "..." }
```

登录成功后：

1. 只在内存中使用用户输入的密码；
2. 保存服务器返回的 Token，不保存明文密码；
3. Token 使用 Windows Credential Manager 或等效安全存储；
4. 后续 Agent 列表、分析任务和通知 API 使用 `Authorization: Bearer <token>`；
5. Token 失效时提示用户重新登录；
6. 用户退出登录时删除本地 Token。

安全要求：

- 服务器地址默认必须使用 HTTPS；
- HTTP 只允许用户明确开启的本地开发地址；
- 密码输入框必须隐藏内容；
- 密码、Token 和完整登录响应不得写入日志；
- 不提供“把密码保存到普通配置文件”的选项；
- 登录失败只显示通用错误，不泄露服务器内部信息。

验收：

- 正确用户名密码可以登录 QwenPaw；
- 错误密码不会保存任何凭据；
- 客户端重启后可以使用安全保存的 Token；
- Token 失效后可以重新登录；
- 退出登录后本地 Token 被删除；
- 全仓库日志中不存在用户名密码和 Token。

### P3-002 实现 TLS WebSocket 客户端（暂缓）

Local Agent 主动连接服务器：

```text
wss://server.example.com/device-gateway
```

必须实现：

- TLS 证书校验；
- 设备认证；
- 心跳；
- 断线重连；
- 命令超时；
- 命令去重；
- 事件顺序号；
- 取消任务；
- 连接状态通知给 Avalonia。

验收：

- 服务器停止后客户端进入离线状态；
- 服务器恢复后自动重连；
- 同一命令重复到达不会执行两次；
- 过期命令被拒绝；
- 网络断开期间的本地通知不丢失。

### P3-003 实现本地命令路由（暂缓）

命令路由只允许注册过的 Handler：

```text
ICommandHandler<TCommand>
ICommandPolicy
IApprovalService
ICommandExecutionContext
```

禁止提供通用的：

```text
Run(string command)
Read(string arbitraryPath)
Delete(string arbitraryPath)
```

验收：

- 未注册的命令返回 `COMMAND_NOT_SUPPORTED`；
- 被策略禁止的命令返回 `COMMAND_BLOCKED`；
- 需要确认的命令在用户确认前没有副作用。

## 9. 阶段 4：服务器 QwenPawPcBridge（暂缓）

本阶段暂缓，当前版本不实现任何远程设备功能。P4-001、P4-002、P4-003 仅保留为后续设计记录，小模型不得执行这些任务，也不得创建真实设备列表、设备连接、远程控制命令或 Device Gateway。

当前版本只允许为微信分析和通知所需的 QwenPaw Agent API 保留最小适配接口；该适配不显示在“远程设备”页面，也不实现通用 PC 远程控制。

### P4-001 实现 Device Gateway

创建 `server/qwenpaw-pc-bridge/`，职责：

- 接受 Local Agent 的 WSS 连接；
- 维护在线设备表；
- 保存设备配对信息；
- 转发命令和事件；
- 校验设备身份；
- 记录最小审计信息；
- 不保存聊天正文和密钥。

第一版只支持单用户、单设备，但协议中保留 `deviceId`。

验收：

- 一个设备可以注册、上线、下线；
- QwenPaw Bridge 可以按 `deviceId` 发送命令；
- 服务器重启后设备可以重新连接；
- 设备离线时命令返回可重试错误。

### P4-002 实现 QwenPaw MCP 工具

向 QwenPaw 注册以下工具：

```text
pc_status
pc_notify
pc_task_status
pc_task_cancel
pc_wechat_accounts
pc_wechat_sessions
pc_wechat_export
pc_devagent_list
pc_devagent_start
pc_devagent_status
pc_devagent_cancel
```

工具返回结构化 JSON，不返回本地密钥，不在工具描述中承诺未实现的权限。

验收：

- QwenPaw 能看到工具列表；
- QwenPaw 调用 `pc_status` 能得到本地设备状态；
- 调用 `pc_notify` 后本地出现 Toast；
- QwenPaw 能看到任务的 accepted/progress/result/error 状态。

### P4-003 服务器与本地 PC 的数据边界

默认只允许：

- 设备状态；
- 任务状态；
- 通知内容；
- 用户明确选择的聊天导出结果。

默认禁止：

- 原始微信数据库；
- 数据库密钥；
- 浏览器凭据；
- 任意磁盘扫描；
- 完整用户目录同步。

## 10. 阶段 5：WeChatDataAnalysis 集成

### P5-001 实现本地微信适配器

Local Agent 不直接操作微信数据库，使用已验证的 WeChatDataAnalysis 本地 API 或发布版进程。

适配器接口：

```text
IWeChatService.GetAccountsAsync()
IWeChatService.GetSessionsAsync(accountId)
IWeChatService.GetMessagesSinceAsync(accountId, conversationId, cursor)
IWeChatService.ExportChatAsync(request)
IWeChatService.GetTaskStatusAsync(taskId)
```

第一版支持：

- 账号列表；
- 会话列表；
- 按游标读取新增消息；
- 联系人/群聊选择；
- 时间范围；
- JSON 导出；
- 文本脱敏；
- 临时文件清理。

验收：

- 不读取密钥内容到日志；
- 用户未确认时不上传聊天正文；
- 指定会话和时间范围可以导出；
- 导出失败不会删除原始微信数据。

### P5-002 实现聊天上传确认

用户必须能看到：

- 联系人或群聊；
- 时间范围；
- 消息数量；
- 是否包含媒体；
- 脱敏规则；
- 目标 QwenPaw Agent；
- Agent 描述和能力；
- 当前 Agent 使用的 Provider/Model 信息仅可只读展示，不提供单独选择；
- 数据保留时间。

只有用户点击允许后，才发送到服务器。

### P5-003 选择 QwenPaw Agent

微信分析页面不维护独立的 LLM Provider 和 Model 配置。用户只选择 QwenPaw 中已经配置好的 Agent，Provider、Model、Memory、Skills 和安全策略由 QwenPaw 服务端决定。

页面功能：

- 从 QwenPaw 获取可用 Agent 列表；
- 展示 Agent 名称；
- 在微信分析设置页配置白名单、分析内容和自动分析模式；
- 白名单可从联系人和公司中多选；
- 分析内容可多选 ToDo、客户提问；
- 自动分析模式支持 1 个月内、本周、最近 50 条和未分析过的消息，默认不是“未分析过的消息”；
- 选择一个 Agent 作为本次分析目标；
- 记住上次选择的 Agent；
- Agent 不可用时显示明确错误；
- Provider/Model 只作为只读信息显示，或者完全隐藏。

接口边界：

```text
IQwenPawAgentCatalog.ListAsync()
IQwenPawAgentCatalog.GetAsync(agentId)
IQwenPawAnalysisClient.StartAsync(agentId, analysisRequest)
IQwenPawAnalysisClient.GetStatusAsync(taskId)
```

验收：

- 微信分析页不出现独立 Provider 卡片；
- 用户可以选择 QwenPaw Agent；
- 分析请求明确携带 `agentId`；
- Agent 的 Provider/Model 不在本地客户端单独配置；
- QwenPaw Agent 列表获取失败时不影响本地通知中心启动。

### P5-004 实现可配置的增量分析调度器

微信分析默认按 5 分钟执行一次，只分析上一次成功分析之后新增的聊天内容。解析间隔在微信分析设置中以分钟配置，默认 5 分钟，最小值为 1 分钟。

监控配置：

```text
MonitoredConversation
  account_id
  conversation_id
  display_name
  enabled
  interval_seconds = 300
  agent_id
  last_success_cursor
  last_success_at
```

执行流程：

1. 读取已启用的联系人或群聊配置；
2. 读取该会话的 `last_success_cursor`；
3. 从 WeChatDataAnalysis 获取新增消息；
4. 没有新增消息时不调用 QwenPaw；
5. 有新增消息时按会话组成一个分析批次；
6. 生成唯一的 `analysisRunId` 和 `deltaHash`；
7. 将新增内容发送给指定的 QwenPaw Agent；
8. 只有 QwenPaw 接受并返回有效结构化结果后，才推进游标；
9. 根据结果创建 TODO、普通通知或疑问通知；
10. 立即显示新增通知。

增量规则：

- 优先使用稳定的 `messageId` 或 `serverId`；
- 不能只依赖消息时间戳去重；
- 每个账号、每个会话独立保存游标；
- 第一次启用默认从当前最新消息开始，不自动分析全部历史；
- 用户可以单独发起一次指定时间范围的历史回溯；
- 网络失败或 LLM 失败时不推进游标，下次继续重试；
- 相同 `deltaHash` 不得重复产生通知。

验收：

- 调度器默认间隔为 5 分钟；
- 没有新增消息时不会产生 LLM 请求；
- 新增消息只被分析一次；
- LLM 失败后重试不会丢失消息；
- 客户端重启后从持久化游标继续；
- 多个会话不会互相覆盖游标。

### P5-005 定义聊天分析结构化结果

QwenPaw Agent 必须返回结构化 JSON，禁止让客户端解析自然语言正文来判断结果。

```text
AnalysisResult
  analysis_run_id
  conversation_id
  decision: no_action|todo|intent|question|mixed
  todos[]
  intents[]
  questions[]
  source_refs[]
```

TODO 最低字段：

```text
TodoCandidate
  id
  title
  summary
  priority
  due_at
  assignee
  confidence
  source_refs[]
```

疑问结果最低字段：

```text
ClarificationQuestion
  id
  problem
  source
  solutions[]
  recommended_solution_id
  allow_custom_input = true
```

`solutions[]` 至少包含两个方案，MVP 必须且只能有一个 `recommended: true` 的推荐方案。手动输入不是 `solutions[]` 中的一项，而是界面固定提供的额外选项。

其他可直接通知的意向包括：

- 待跟进；
- 截止时间提醒；
- 对方请求；
- 需要做决定；
- 风险或冲突；
- 约定事项；
- 需要用户确认的业务意图。

处理规则：

- 新增 TODO 立即生成工作通知；
- 新增意向立即生成对应分类通知；
- `no_action` 不显示通知，但保留分析运行元数据；
- 低置信度或存在互斥处理方式时生成疑问通知；
- 通知必须带 `analysis_run_id` 和消息来源引用；
- 相同结果不能重复创建 TODO 或通知。

验收：

- 非法 JSON 结果被拒绝并产生分析失败通知；
- TODO、意向和疑问可以分别落库；
- 每个结果都能追溯到会话、时间范围和消息引用；
- 重复分析结果不会重复生成工作项。

### P5-006 实现疑问通知和用户决策回传

当 QwenPaw 对聊天内容存在疑问时，不直接猜测执行，而是创建 `notificationType: "question"` 的持久通知。

疑问通知内容：

1. 问题描述；
2. 问题来源；
3. 参与聊天的双方或群聊名称；
4. 来源时间范围；
5. 相关消息引用；
6. 多个解决方案，其中一个标记为“推荐”；
7. “手动输入（不使用以上解决方案）”入口。

用户可以：

- 点击任意一个解决方案，其中推荐方案有明确视觉标识；
- 选择手动输入并填写自定义处理方式；
- 关闭 Toast，稍后在通知中心或“工作”页面处理。

提交后发送给 QwenPaw 的上下文必须包含：

```text
clarification_id
original_question
source_conversation
source_refs
solutions
recommended_solution_id
selected_solution_id
custom_input
user_decided_at
```

QwenPaw 收到后自行决定后续动作，可能返回：

- 新 TODO；
- 新通知；
- 已完成；
- 需要继续追问；
- 无需操作。

验收：

- 疑问通知不会自动超时消失；
- 关闭 Toast 后仍能在通知中心和“工作”页面找到；
- 选择任意解决方案后通知进入 `answered` 状态；
- 推荐方案只有视觉标记，不会绕过用户确认自动执行；
- 手动输入后完整文本发送给 QwenPaw；
- 重复提交同一个疑问不会重复处理；
- QwenPaw 的后续结果能关联原疑问通知。

## 11. 阶段 6：Codex/OpenCode 占位界面

本阶段只验证产品结构和前端交互，不接入真实 Codex 或 OpenCode。所有状态、进度和任务结果使用明确标记的 Mock 数据，禁止伪装成真实执行结果。

### P6-001 创建开发 Agent 占位模型

创建前端可使用的占位模型：

```text
DevAgent
  id
  display_name
  status: mock|not_configured
  progress
  message
```

第一版不启动任何外部程序，不扫描程序路径，不保存真实可执行文件路径。使用固定的 Mock 配置展示 Codex 和 OpenCode 两张 Agent 卡片。

验收：

- Avalonia 可以展示 Codex 和 OpenCode 占位卡片；
- 卡片展示名称、图标、模拟在线状态和“暂未接入”提示；
- 点击入口不会启动外部程序；
- Mock 数据不会写入真实任务历史。

### P6-002 创建任务卡片占位交互

仅实现前端交互样式：

- 任务标题；
- 任务状态标签；
- 模拟进度条；
- 模拟剩余时间；
- “启动任务”按钮的未接入提示；
- “查看详情”按钮的占位详情页；
- 与通知中心联动的模拟完成通知。

不得实现以下业务：

- 真实 Codex/OpenCode 启动；
- CLI 参数拼接；
- ACP/MCP 通信；
- 标准输入输出读取；
- 进程树回收；
- 真实任务取消；
- 真实日志上传；
- GUI 自动化。

验收：

- 页面可以展示完整的任务卡片布局；
- 点击操作只显示“功能尚未接入”提示；
- 模拟任务可以触发本地通知预览；
- 关闭并重启客户端后不会留下伪造的真实任务记录。

### P6-003 后续真实集成预留

只定义接口，不实现具体业务：

```text
IDevAgentAdapter.ListAsync()
IDevAgentAdapter.StartAsync()
IDevAgentAdapter.GetStatusAsync()
IDevAgentAdapter.CancelAsync()
```

真实 Codex/OpenCode 集成必须在后续独立任务中重新评估协议、权限、工作目录和进程生命周期，不得在本阶段偷偷接入。

## 12. 阶段 7：服务器个人数据与通知

### P7-001 服务器数据模型

服务器保存：

- QwenPaw 会话和 Memory；
- 分析任务元数据；
- TODO 结构化结果；
- 通知摘要；
- 用户确认记录；
- 可选的脱敏聊天数据。

服务器不保存：

- 本地微信数据库；
- 微信解密密钥；
- 未授权聊天正文；
- Local Agent 的长期原始日志。

### P7-002 分析结果通知

当前版本由本地 QwenPaw HTTPS API 请求直接返回分析结果，本地客户端根据结构化结果写入通知中心；不通过暂缓的远程设备通道。

```text
analysis.completed
analysis.failed
todo.created
intent.created
analysis.question
clarification.answered
devagent.completed
device.warning
```

Local Agent 接收结果后交给本地通知服务，Avalonia 显示 Toast 和历史通知。`analysis.question` 必须创建待处理的疑问通知；用户提交选择后产生 `clarification.answered`，再发送给 QwenPaw Agent 继续处理。

验收：

- QwenPaw 任务完成后本地收到通知；
- 新增 TODO 或意向后立即收到通知；
- QwenPaw 产生疑问时收到持久化问题通知；
- 用户提交任意解决方案或手动输入后可以继续 QwenPaw 分析；
- 通知点击可以打开任务详情；
- 同一事件重复到达不会重复显示。

## 13. 阶段 8：安全和运维

必须实现：

- 权限白名单；
- 用户确认；
- 最小化审计日志；
- 服务器端认证；
- QwenPaw Token 安全存储；
- 第三方通知 Token 撤销和轮换。

服务器部署要求：

- QwenPaw Console 不直接裸露公网；
- 使用反向代理或 VPN；
- 开启 QwenPaw 身份认证；
- 生产环境禁止调试日志记录聊天内容。

## 14. 测试计划

### 单元测试

- 协议序列化；
- 命令过期；
- 命令去重；
- 权限策略；
- 通知筛选；
- 任务状态转换；
- 路径白名单；
- QwenPaw 登录和 Token 生命周期。
- 第三方通知 API 的认证、生命周期和去重。
- 增量分析游标和 `deltaHash` 去重；
- 5 分钟调度器的无新增跳过和失败重试；
- TODO、意向和疑问的结构化结果校验；
- 疑问通知的多方案（其中一个标记为推荐）、手动输入和 QwenPaw 回传。

### 集成测试

- Avalonia 与 Local Agent 本机 IPC；
- QwenPaw Agent 列表和分析 API 适配；
- WeChatDataAnalysis API；
- 每 5 分钟新增消息读取和游标推进；
- QwenPaw 分析结果到通知中心；
- 疑问通知到用户回答再到 QwenPaw 的闭环；
- Codex/OpenCode 占位界面和 Mock 任务交互。

### 验收测试

1. 客户端启动后可以读取 QwenPaw Agent 列表。
2. 微信分析页不出现独立 LLM Provider 选择器。
3. 用户可以选择 QwenPaw Agent 并提交分析任务。
4. 用户拒绝上传时，本地没有副作用。
5. QwenPaw 能列出微信会话，但不会自动上传聊天正文。
6. 用户确认后可以上传选定聊天并得到分析结果。
7. 每 5 分钟只读取已监控会话的新增消息。
8. 新增 TODO 或意向后立即生成通知和工作项。
9. 分析有疑问时生成包含来源、推荐方案和手动输入的持久通知。
10. 用户提交回答后，完整上下文可以回传给 QwenPaw Agent。
11. 第三方可以通过本机通知 API 发送一条自动消失的通知。
12. 第三方可以发送需要关闭或确认后才消失的通知。
13. “工作”页面能展示 Mock TODO 和工作通知。
14. Avalonia 能展示 Codex/OpenCode 占位卡片和任务样式；本阶段不要求真实任务状态。
15. 客户端关闭窗口后仍驻留托盘并接收本地通知。
16. 远程设备页面显示“暂未开放”，不执行任何远程设备操作。

## 15. 阶段顺序和停止条件

建议顺序：

```text
P0 环境
  → P1 协议和权限
  → P2 Avalonia 壳与通知
  → P3 Local Agent 本地运行时与 QwenPaw 登录
  → P5 微信增量分析与疑问闭环
  → P6 Codex/OpenCode 占位界面
  → P7 数据与通知完善
  → P8 安全和发布
  → P4 远程设备控制（后续阶段）
```

任何阶段出现以下情况必须停止扩展功能：

- 用户拒绝后仍产生副作用；
- 微信密钥进入日志或服务器；
- 增量游标在分析失败时被错误推进；
- 同一新增消息重复生成 TODO 或通知；
- 疑问通知缺少聊天来源或用户回答无法回传；
- Local Agent 可以访问未授权路径。

## 16. 小模型执行格式

每次执行任务时只允许输出以下内容：

```text
任务编号：P3-001
修改文件：...
完成内容：...
验证命令：...
验证结果：...
风险或阻塞：...
下一任务：P5-001
```

禁止小模型一次性实现多个阶段，也禁止在没有通过协议和权限测试前接入真实微信数据或真实服务器。

## 17. 第一批实际开发任务

第一轮只执行下面五个任务：

1. `P0-001` 创建解决方案和目录。
2. `P0-002` 确认运行时版本。
3. `P1-001` 创建设备模型。
4. `P1-002` 创建命令/事件 DTO 和序列化测试。
5. `P1-003` 创建权限策略和单元测试。

完成这五项并通过测试后，再开始 Avalonia UI，不要提前连接真实 QwenPaw、微信或服务器。
