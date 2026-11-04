# 用户准备与联系人配置

本文是交给最终用户或配置 Agent 的清单。请先准备微信本机访问权限，再配置 QwenPaw，最后维护联系人、公司和项目归属。

## 一、需要提供给配置 Agent 的信息

只提供“配置所需的非敏感信息”，不要把密码、微信数据库密钥或聊天正文粘贴到聊天中。

| 信息 | 是否必需 | 提供方式 |
| --- | --- | --- |
| 发布包或解压目录 | 是 | 提供本机上的目录路径；不要把整个微信数据目录上传给 Agent |
| Windows 版本与架构 | 是 | Windows 10/11 x64；如果不是 x64，先确认是否有对应发布包 |
| 微信账号 | 是 | 用户本人在本机登录的账号；不要提供微信密码 |
| 微信数据库输出目录 | 是 | `output` 所在目录路径，或是否使用发布包目录下的 `output` |
| QwenPaw 地址 | 是 | 本机默认为 `http://127.0.0.1:8088/`；Docker、NAS 或远程部署时提供实际可达地址 |
| QwenPaw 登录信息 | 是 | 由用户在 Fairy 设置页或 QwenPaw 本地配置界面输入；不要发给 Agent。Fairy 会在本机记住，下次启动自动登录 |
| 分析 Agent 名称 | 是 | 默认 `WeChatAnalysist`；如使用其它 Agent，请提供名称 |
| 联系人/公司/项目资料 | 按需 | 用户可以直接在 Fairy 中录入，也可以提供整理好的表格或 JSON 草稿供 Agent 协助导入 |

QwenPaw 使用云模型时，模型供应商 API Key 由用户在 QwenPaw 的模型配置中输入；使用本地模型时通常不需要云 API Key。这个 Key 不属于 Fairy 配置，也不要写入本文件或发布包。

## 二、先获取微信数据库访问授权

没有 `output\account_keys.json` 和解密后的数据库，Fairy 无法访问微信数据，也无法执行微信分析。这个权限不是向配置 Agent 索取一个通用 Key，而是由微信账号所有者在**自己的 Windows 账号和本机微信环境**中完成一次检测、取密钥和解密。

1. 用准备分析的微信账号登录本机微信，并保持微信运行。不要使用他人导出的 `account_keys.json`，也不要把微信密码交给 Agent。
2. 打开发布包中的 `WeChatDataAnalysisRelease\WeChatDataAnalysis\WeChatDataAnalysis.exe`。
3. 在工具的解密页面按界面顺序执行：
   - 微信检测：确认检测到当前登录的微信账号；
   - 获取数据库密钥：只在本机工具界面完成，不复制密钥文本；
   - 选择该账号对应的 `db_storage` 路径并执行数据库解密。
4. 如果 Windows 弹出文件访问、防火墙或管理员权限确认，只在确认程序路径确实来自本发布包时由用户本人确认。Agent 不应代替用户输入 Windows 密码，也不应绕过系统安全提示。
5. 完成后确认存在以下结果：

   ```text
   <数据根目录>\output\account_keys.json
   <数据根目录>\output\databases\
   ```

   `account_keys.json` 必须是非空文件，`databases` 中应有解密后的数据库文件。密钥文件和数据库只保留在本机，不上传给 QwenPaw 或其它第三方。

6. 如果 `output` 不在 Fairy 主程序目录下，打开 **设置 > 微信分析 > 微信数据访问授权**，点击“选择数据目录”，选择包含 `output` 子目录的数据根目录。完全退出 Fairy（包括托盘实例）并重新启动，再点击“重新检测”。

   无法使用图形界面时，也可以设置用户环境变量并重启 Fairy：

   ```powershell
   [Environment]::SetEnvironmentVariable('WECHAT_TOOL_DATA_DIR', 'D:\WeChatData', 'User')
   ```

   将 `D:\WeChatData` 替换为实际包含 `output` 子目录的目录。完全退出 Fairy（包括托盘实例）后重新启动。

## 三、配置 QwenPaw 与 Fairy

### 1. QwenPaw

1. 启动 QwenPaw，确认本机 Console 地址为 `http://127.0.0.1:8088/`，并在 QwenPaw 中完成模型配置。
2. 将 `Docs\user\qwenpaw\skills\wechat-analysis-context\SKILL.md` 复制到 QwenPaw 自定义 Skill 目录：

   ```text
   %USERPROFILE%\.qwenpaw\customized_skills\wechat-analysis-context\SKILL.md
   ```

3. 在 QwenPaw 的 **Agent > Skills** 中启用或重新加载 `wechat-analysis-context`。
4. 在 **Agent > MCP** 中注册 `lumine-fairy-wechat-context`：
   - 类型：`stdio`；
   - 命令：`python`；
   - 参数：解压目录中 `Docs\mcp\qwenpaw-side\server.py` 的绝对路径；
   - `wechat-context.local.json` 与 `server.py` 放在同一目录；
   - 同机默认连接 `127.0.0.1:18766`。
5. 将 `Docs\mcp\qwenpaw-side\wechat-context.local.json.example` 复制为同目录的 `wechat-context.local.json`，生成一个随机 Token，并把相同 Token 写入 Fairy 主程序目录下的 `wechat-context.local.json`。Token 只在本机文件中配置，不要发送给 Agent 或写入日志。
6. 将 `WeChatAnalysist` 的 MCP 驱动 `lumine_fairy_wechat_context` 的 `policy.default_effect` 设为 `allow`，否则分析时会停在审批等待。

QwenPaw 与 Fairy 不在同一台计算机时，不能继续使用 `127.0.0.1`：MCP 配置中的 `host` 必须改为 Fairy 所在计算机的实际可达地址，并按运维文档配置 URL ACL 和防火墙。

### 2. Fairy

1. 打开 **设置 > 微信分析 > QwenPaw 登录**。
2. 本机默认填写 `http://127.0.0.1:8088/`。
3. 由用户本人输入 QwenPaw 用户名和密码并点击登录；不要让 Agent 读取、回显或保存密码。
4. 选择分析 Agent，默认选择 `WeChatAnalysist`。

   登录成功后，Fairy 会在本机记住服务器地址、用户名、密码和分析 Agent，下次打开客户端自动登录 QwenPaw 并继续使用该 Agent；QwenPaw 地址或密码变更时，重新在本页填写并登录即可覆盖。
5. 如需启用上下文 MCP，在主程序目录放置 `wechat-context.local.json`，同机配置示例为：

   ```json
   {
     "host": "127.0.0.1",
     "port": 18766,
     "token": "仅本机填写的随机 Token",
     "timeoutSeconds": 10
   }
   ```

6. 重启 Fairy，并用 `http://127.0.0.1:18766/health` 验证上下文服务；收到 `401` 时只检查两端 Token 是否一致，不要打印 Token。

## 四、联系人、公司和项目怎么配置

在 Fairy 主窗口打开 **联系人管理**。建议先建立业务主数据，再同步微信联系人。

1. **公司**：新建公司，至少填写公司名称；可补充部门、地址和备注。
2. **项目**：新建项目，填写项目名称；按需关联公司，补充交付日期、标签和项目备注。
3. **联系人**：新建或编辑联系人，填写姓名/显示名、昵称、关系，并关联一个或多个公司/项目。
4. **微信 ID**：在联系人资料中维护稳定的微信 ID。能取得准确微信 ID 时优先填写；分析会优先按微信 ID 精确匹配，再使用姓名、昵称、公司、项目和群聊名称匹配。
5. **微信联系人**：进入 **联系人管理 > 微信联系人**，点击“刷新联系人”，确认本机同步到的联系人和群聊。
6. 为每个微信联系人或群聊选择所属公司，点击该行保存图标；也可以点击“全部保存”。没有归属时明确选择“未分配”。
7. 联系人较多时可点击“自动分配公司”。它只处理尚未分配的活跃条目，按公司中文名称或完整拼音连续匹配，不会覆盖已有归属；自动分配后仍应人工抽查。
8. 群聊可在项目资料中维护群聊名称，使项目上下文更容易匹配。名称相似但不能唯一确认时，不要强行归属。
9. 回到 **设置 > 微信分析**，按需启用联系人/公司白名单。修改联系人、公司或项目后，下一次分析会自动读取最新资料。

## 五、交给 QwenPaw 配置 Agent 的任务说明

可以将下面的指令连同本文件和发布包路径交给 QwenPaw：

```text
你负责在本机配置 Lumine.Fairy 的微信分析协作环境。请先读取用户准备文档和 AGENT-SETUP.md。

1. 检查 QwenPaw 本机地址 http://127.0.0.1:8088/ 和模型是否已配置；模型 API Key 由用户自己在 QwenPaw 中输入，不要索取或回显。
2. 将 wechat-analysis-context Skill 安装到 %USERPROFILE%\.qwenpaw\customized_skills\wechat-analysis-context\SKILL.md，并在 Agent > Skills 启用。
3. 在 Agent > MCP 注册 stdio MCP：命令为 python，参数为 server.py 的绝对路径；确保配置文件与 server.py 同目录，默认连接 127.0.0.1:18766。
4. 只协助用户在本机 WeChatDataAnalysis 中完成微信检测、数据库密钥获取和数据库解密。不要索取微信密码、数据库密钥、account_keys.json 内容或聊天正文，也不要上传这些数据。
5. 检查 output\account_keys.json 和 output\databases 是否存在；不存在时暂停并请用户本人完成微信权限确认。
6. 将 MCP 驱动 lumine_fairy_wechat_context 的 policy.default_effect 设为 allow，验证 initialize、tools/list 和一次 tools/call；不要打印 Token。
7. 引导用户在 Fairy 的联系人管理中配置公司、项目、联系人、微信 ID 和微信联系人归属；对不确定的归属保留为未分配并请用户确认。
```

## 六、安全边界

- 微信数据库、`account_keys.json`、数据库密钥、微信密码、QwenPaw 密码、MCP Token 和模型 API Key 不应写入聊天、文档、日志、Git 或发布包。
- 配置 Agent 只处理路径、开关、非敏感联系人主数据和验证结果；涉及登录、Windows 权限确认、微信检测和密钥获取时必须由用户本人操作。
- 微信聊天正文仅在用户授权的本机分析流程中按需发送给已配置的 QwenPaw Agent；上下文 MCP 只返回必要的名称、项目和标签，不读取微信原始数据库。

相关说明：`AGENT-SETUP.md`、`Docs/user/qwenpaw-wechat-context-mcp.md`、`Docs/developer/operations/wechat-context-mcp.md`。
