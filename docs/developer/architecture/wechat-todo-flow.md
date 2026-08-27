# 微信消息到 TODO 的处理链路

## 当前流程

```text
WeChatDataAnalysisRelease
        |
        v
读取账号、会话和增量消息
        |
        v
QwenPaw Agent 返回 AnalysisResult JSON
        |
        +--> TODO 候选写入 todos.db，状态 pending_confirmation
        |       |
        |       v
        |   通知中心显示“确认加入 TODO / 忽略”
        |       |
        |       +--> 确认：状态 pending，工作页显示
        |       +--> 忽略：状态 rejected，工作页隐藏
        |
        +--> Intent 生成普通工作通知
        +--> Question 生成带解决方案的确认通知
```

如果消息来自已在联系人管理中标注公司的微信群聊，TODO 候选、通知和工作页会显示公司名称。归属匹配键为 `accountId + conversationId`，不会根据群名或聊天内容自动猜测公司。

## 消息读取

- 桌面客户端只使用根目录 `WeChatDataAnalysisRelease` 中的发布版后端。
- 后端固定监听本机 `127.0.0.1:10492`，数据目录由 `WECHAT_TOOL_DATA_DIR` 指定；未配置时使用当前运行程序目录，`output/account_keys.json` 是账号读取前必须准备的数据文件。
- 手动分析读取当前账号第一个符合白名单的会话；增量调度器默认每 5 分钟检查所有已发现账号和会话，解析间隔可在微信分析设置中按分钟配置，最小为 1 分钟。
- 微信分析设置保存在 `%LOCALAPPDATA%/Lumine.Fairy/settings.json`，支持关闭白名单分析全部会话，或按联系人、公司名称及联系人昵称匹配联系人和群聊。
- 分析内容可选择 ToDo 和客户提问；客户提问要求客户 `@我` 提问且我在 10 分钟内未回复。
- 自动分析模式支持一个月内、本周、最近 50 条和未分析过的消息，默认使用一个月内；未分析模式使用每个账号/会话的最后分析消息游标。
- 微信分析页可以执行“设置最新分析点”，按当前分析范围将各账号/会话游标推进到最新消息，跳过已有历史；该操作不调用 QwenPaw。
- 增量调度器使用当前选择的 QwenPaw Agent，用户切换 Agent 后后续轮询同步更新。
- 增量游标保存到 `%LOCALAPPDATA%/Lumine.Fairy/analysis-cursors.json`。首次检查只建立游标，不回溯全部历史。
- QwenPaw 未返回有效 `AnalysisResult` 时分析失败，游标不推进，下一轮继续重试。

## TODO 生命周期

`Lumine.Fairy.Infrastructure.SqliteTodoStore` 使用 `%LOCALAPPDATA%/Lumine.Fairy/todos.db` 保存 TODO，并保存可选的公司 ID、公司名称、会话显示名称和群聊标记。

状态如下：

- `pending_confirmation`：已从微信分析识别出来，等待用户确认；
- `pending`：用户确认后进入 TODO；
- `in_progress`：预留给后续执行状态；
- `completed`：预留给后续完成操作；
- `rejected`：用户忽略，不在工作页显示。

候选 TODO 使用账号、会话、消息引用和候选内容生成稳定哈希标识，避免模型重复使用简单序号时误合并不同事项。候选写入成功但通知写入失败时，下一次分析会补发缺失通知。

确认通知的 payload 保存 TODO 标识，通知动作通过桌面 UI 或本地 HTTP 接口统一处理；状态转换只允许合法的前置状态，已处理通知不能重放。

## 通知与确认

- TODO 候选通知使用 `confirm-todo` 和 `reject-todo` 两个动作。
- 澄清问题通知使用 `question-solution` 动作，选择方案后继续调用 QwenPaw。
- 通知 payload 和 question 数据均持久化在通知 SQLite 中，应用重启后仍可处理未完成确认。
- 工作页只读取 TODO 数据库，不再使用静态示例数据。

## 微信分析实时状态

微信分析页直接订阅本次分析运行的阶段快照，不使用泛化的全局状态文字。阶段包括：

- 读取微信账号；
- 读取微信会话；
- 读取微信消息；
- 发送给 QwenPaw；
- QwenPaw 分析；
- 校验分析结果；
- 保存结果和通知；
- 完成。

每个阶段保存开始时间、结束时间、当前详情和错误信息。页面实时显示总耗时、当前阶段耗时和阶段错误；详细日志只展示阶段事件和错误摘要，不展示聊天正文。

## CLI 诊断

使用 `Lumine.Fairy.Diagnostics` 检查本地微信后端，不需要启动桌面 UI：

```text
dotnet run --project Lumine.Fairy/tools/Lumine.Fairy.Diagnostics -- --timeout 30
```

诊断程序会依次检查账号、会话和消息接口，输出每个阶段的 HTTP 请求、状态码和耗时。需要继续执行真实 QwenPaw 分析时追加 `--analyze`；默认诊断不会上传聊天内容或创建分析通知。
