# 微信分析处理流程

## 1. 文档目的

本文记录当前微信分析链路的真实实现，作为开发、排障和后续扩展的基准。微信原始数据库、密钥和聊天正文保留在本机；QwenPaw 只接收经过本地分析流程提交的消息内容。

相关细节：

- TODO 生命周期和确认通知：见 [微信消息到 TODO 的处理链路](wechat-todo-flow.md)；
- 微信后端运行和数据目录：见 [QwenPaw Remote PC 运行维护](../operations/lumine-fairy-runtime.md)。

## 2. 组件职责

| 组件 | 当前职责 |
| --- | --- |
| `WeChatDataAnalysisRelease` | 解密/读取本机微信数据，提供本地 HTTP API |
| `WeChatDataAnalysisClient` | 启动或复用微信后端，读取账号、会话和消息 |
| `WeChatAnalysisCoordinator` | 编排读取、QwenPaw 分析、结果解析、TODO/通知保存 |
| `QwenPawClient` | 登录 QwenPaw、读取 Agent、发送对话并解析 SSE 响应 |
| `IncrementalAnalysisScheduler` | 每五分钟检查所有账号和会话的新增消息 |
| `SqliteTodoStore` | 持久化 TODO 候选和 TODO 状态 |
| `SqliteNotificationStore` | 持久化通知、操作、问题和结构化 payload |
| Avalonia Desktop | 提供分析按钮、阶段进度、错误、日志和确认操作 |
| `Lumine.Fairy.Diagnostics` | 不依赖桌面 UI 的接口和分析链路诊断 |

## 3. 数据读取链路

```text
Avalonia Desktop / Diagnostics CLI
              |
              v
WeChatDataAnalysisClient
              |
              v
127.0.0.1:10492
              |
      WeChatDataAnalysisRelease
              |
              v
账号 -> 会话 -> 最近消息
```

### 3.1 后端启动

`WeChatDataAnalysisClient` 使用以下规则：

- 后端程序来自根目录 `WeChatDataAnalysisRelease`；
- 端口固定为 `10492`；
- 优先使用 `WECHAT_TOOL_DATA_DIR`；
- 未配置时，从当前目录和程序目录向上查找包含 `output/account_keys.json` 或 `output/databases` 的工作区；
- 复用已有后端前检查健康状态、实时账号列表和日志位置；
- 如果端口被未知后端占用且数据目录不匹配，不自动终止进程，而是返回明确错误；
- 后端检查使用 `_backendGate` 串行化，所有成功、失败和取消路径都会释放该信号量。

### 3.2 账号读取

账号来源是：

```text
GET /api/chat/accounts
```

客户端使用 `accounts` 和 `accountInfos` 中的规范账号 ID，跳过明确标记为不可用的实时账号。不会使用检测接口返回的备份目录账号直接作为会话查询参数。

### 3.3 会话读取

```text
GET /api/chat/sessions
    ?account={accountId}
    &limit=20
    &preview=latest
    &source=realtime
```

会话显示名称依次使用 `displayName`、`display_name`、`name` 和 `username`。

### 3.4 消息读取

```text
GET /api/chat/messages
    ?account={accountId}
    &username={conversationId}
    &limit={1..50}
    &order=desc
    &source=realtime
```

后端返回倒序消息，客户端反转为时间正序后交给分析器。消息发送者优先使用显示名称，必要时附带微信用户名；消息 ID 优先使用 `serverId`，其次使用 `localId`。

## 4. 手动分析流程

用户在“微信分析”页点击“分析最近会话”后：

1. 页面切换到微信分析页；
2. 检查 QwenPaw 登录状态和当前 Agent；
3. 读取实时可用账号；
4. 按账号的数据库数量选择分析账号；
5. 读取最近会话并按联系人管理数据筛选符合白名单的会话；启用白名单时分析全部匹配会话，关闭白名单时保留单次手动分析一个最近会话的行为；
6. 读取最近 50 条消息，并按微信分析设置的自动分析模式筛选；
7. 构造只包含所选分析内容的提示词并提交给选定 Agent；
8. 等待 QwenPaw 返回结构化分析结果；
9. 校验 `AnalysisResult`；
10. 仅保存所选的 TODO 候选、客户提问和分析完成通知；
11. 刷新通知中心和工作页。

手动分析和自动分析都会在执行前从共享的联系人管理 SQLite 数据重新构建白名单目录。联系人管理中的微信 ID 使用精确匹配；姓名、昵称、公司、项目和群聊名称只作为备用匹配。联系人、公司和群聊筛选在本地完成，不绕过本地数据边界直接读取数据库。

## 5. 增量分析流程

`IncrementalAnalysisScheduler` 在 QwenPaw 登录成功后启动，默认每五分钟运行一次：

```text
读取全部实时账号
    |
    +-- 遍历账号
          |
          +-- 读取全部会话
                |
                            +-- 刷新联系人/公司/项目匹配目录
                                  |
                                  +-- 读取最近 50 条消息
                      |
                      +-- 比较账号/会话游标
                            |
                            +-- 无新增：跳过 QwenPaw
                            +-- 有新增：分析新增消息
```

游标文件：

```text
%LOCALAPPDATA%/Lumine.Fairy/analysis-cursors.json
```

游标键为 `accountId:conversationId`，值由最新消息 ID 和时间戳组成。选择“未分析过的消息”模式时，调度器按游标只提交上次分析之后的消息；首次发现会话时只建立游标，不自动分析全部历史。其他模式按设置的时间范围读取最近 50 条消息。QwenPaw 或本地保存失败时不推进游标，下一轮继续重试。

白名单、分析内容和自动分析模式由微信分析设置页维护。白名单启用后，调度器只处理与选中联系人、公司名称或其别名匹配的联系人和群聊；公司匹配会同时纳入其关联联系人、项目名称和项目群聊。关闭后处理全部会话。提示词只要求 QwenPaw 返回 ToDo 和/或客户提问，客户提问需满足客户 `@我` 且 10 分钟内未得到回复。

联系人管理中的微信 ID 存储在联系人实体中，并随 SQLite schemaVersion 5 一起迁移；联系人 JSON / Excel 导入导出也保留该字段。自动调度器每次轮询前重新加载联系人数据库，避免联系人管理应用在独立进程中修改数据后出现长期旧缓存。

调度器使用当前选择的 QwenPaw Agent；用户切换 Agent 后，后续轮询同步使用新 Agent。

## 6. QwenPaw 分析协议

提示词要求 Agent 返回可解析的 `AnalysisResult` JSON，主要字段如下：

```text
AnalysisResult
  decision
  todos[]
  intents[]
  questions[]
  sourceRefs[]
```

TODO 候选至少包含：

```text
TodoCandidate
  id
  title
  summary
  priority
  dueAt
  assignee
  confidence
  sourceRefs[]
```

QwenPaw 返回通过 SSE 接收。客户端提取最终文本并解析结构化 JSON；没有有效 `AnalysisResult` 时分析失败，不创建完成通知，也不推进增量游标。

## 7. 实时阶段状态

微信分析运行会发布 `AnalysisProgressSnapshot`，当前阶段包括：

1. 读取微信账号；
2. 读取微信会话；
3. 读取微信消息；
4. 发送给 QwenPaw；
5. QwenPaw 分析；
6. 校验分析结果；
7. 保存结果和通知；
8. 完成。

每个阶段有以下状态：

```text
pending -> running -> completed
                    \-> error
```

桌面端显示：

- 当前阶段名称；
- 当前阶段详情；
- 当前阶段耗时；
- 本次运行总耗时；
- 失败阶段和错误信息；
- 可展开的阶段日志摘要。

日志只记录阶段、接口状态和错误摘要，不记录完整聊天正文。

超时策略：

- 微信账号、会话、消息读取阶段：30 秒；
- QwenPaw 分析阶段：3 分钟；
- 诊断 CLI 可通过 `--timeout` 设置读取阶段超时时间。

## 8. 分析结果落库

### 8.1 TODO

TODO 候选写入：

```text
%LOCALAPPDATA%/Lumine.Fairy/todos.db
```

候选先保存为 `pending_confirmation`，并生成通知：

```text
confirm-todo
reject-todo
```

只有用户确认后才转为 `pending`，进入工作页。TODO 使用账号、会话、消息引用和候选内容生成稳定哈希，避免模型重复使用简单序号导致不同事项被误合并。

### 8.2 通知

通知写入：

```text
%LOCALAPPDATA%/Lumine.Fairy/notifications.db
```

通知保存标题、正文、来源、分类、动作、问题对象和 payload。TODO 通知 payload 中保存 `todoId`，桌面 UI 和本地 HTTP 接口使用同一套动作处理逻辑。

### 8.3 澄清问题

`questions[]` 会转换为带解决方案按钮的确认通知。用户选择方案后，客户端将用户选择再次提交给 QwenPaw，并关闭原问题通知。

## 9. 错误处理

错误必须停留在具体阶段：

| 错误 | 显示阶段 | 游标行为 |
| --- | --- | --- |
| 没有实时账号 | 读取微信账号 | 不推进 |
| 会话接口 404/超时 | 读取微信会话 | 不推进 |
| 消息接口 422/超时 | 读取微信消息 | 不推进 |
| QwenPaw 登录或对话失败 | QwenPaw 分析 | 不推进 |
| 返回非法 JSON | 校验分析结果 | 不推进 |
| TODO/通知写入失败 | 保存结果和通知 | 不推进 |

阶段失败后，用户可以通过展开日志查看错误摘要；增量调度器下一轮会继续重试。

## 10. CLI 诊断

诊断项目：

```text
Lumine.Fairy/tools/Lumine.Fairy.Diagnostics
```

只检查本地读取链路：

```powershell
dotnet run --project Lumine.Fairy/tools/Lumine.Fairy.Diagnostics -- --timeout 30
```

参数：

```text
--account <id>       指定微信账号
--conversation <id>  指定会话 username
--limit <n>          消息数量，默认 20
--timeout <seconds>  每阶段超时，默认 30
--agent <id>         分析 Agent，默认 default
--analyze            继续执行真实 QwenPaw 分析
--import-company-directories <path>
                     将 path 下一级目录按公司名称幂等导入联系人数据库
```

默认诊断不会调用 QwenPaw，不会创建 TODO 或分析通知；`--analyze` 才会执行完整分析链路。

## 11. 关键代码位置

- 微信 API 客户端：`Lumine.Fairy/src/Lumine.Fairy.Infrastructure/WeChatDataAnalysisClient.cs`；
- 分析编排和阶段状态：`Lumine.Fairy/src/Lumine.Fairy.Application/WeChatAnalysisCoordinator.cs`；
- 联系人/公司匹配与目录导入：`Lumine.Fairy/src/Lumine.Fairy.Desktop/ViewModels/MainWindowViewModel.cs`、`Lumine.Fairy/Lumine.Fairy.ContactManagement.Infrastructure/Persistence/DirectoryCompanyImportService.cs`；
- 增量调度器：`Lumine.Fairy/src/Lumine.Fairy.Application/IncrementalAnalysisScheduler.cs`；
- 协议模型：`Lumine.Fairy/src/Lumine.Fairy.Protocol/Contracts.cs`；
- 桌面状态页：`Lumine.Fairy/src/Lumine.Fairy.Desktop/ViewModels/MainWindowViewModel.cs` 和 `Views/MainWindow.axaml`；
- CLI 诊断：`Lumine.Fairy/tools/Lumine.Fairy.Diagnostics/Program.cs`。
