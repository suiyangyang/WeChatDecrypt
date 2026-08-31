# 第三方通知 HTTP 接口开发文档

## 1. 文档范围

本文档描述 Lumine.Fairy 桌面客户端当前提供的本机第三方通知接口，供同一台 Windows 计算机上的其他程序发送通知、读取通知状态，以及触发通知动作或提交问题处理结果。

接口属于 Fairy 桌面进程的一部分。客户端启动后自动启动接口，客户端退出时接口停止。

当前实现默认监听本机所有 IPv4 网卡，允许局域网内的第三方程序访问；也可以通过环境变量收窄到指定 IPv4 地址。接口不提供任意回调 URL。

**当前支持状态：外部程序可以通过 Fairy 所在 Windows 计算机的局域网 IP 访问通知 HTTP 接口。** 访问前需要完成 URL ACL、Windows 入站防火墙和 Bearer Token 配置。

## 2. 服务地址与认证

默认监听端口为 `18765`。当使用默认的全网卡监听时，服务地址使用 Fairy 所在 Windows 计算机的局域网 IPv4 地址：

```text
http://<Fairy-IP>:18765
```

通知发布地址会在 Fairy 设置页显示：

```text
http://<Fairy-IP>:18765/api/v1/notifications
```

设置页显示实际解析出的服务地址。`127.0.0.1:18765` 仍可用于 Fairy 所在计算机上的本地调用。

所有请求都必须携带 Bearer Token，包括 GET、动作和问题响应请求；只有带 JSON 请求体的请求需要声明 `Content-Type: application/json`：

```http
Authorization: Bearer <notification-token>
Content-Type: application/json
```

当前桌面客户端使用的 Bearer Token 为 `local-dev-notification-token`，设置页会以密码样式隐藏该值。点击 Bearer Token 右侧的眼睛按钮即可显示或隐藏；设置页显示的值就是接口实际校验的值，外部程序应直接使用它发起请求。显示状态不会改变 Token，也不需要重启 Fairy。不要把 Token 写入日志、源码仓库或不受信任的对外渠道。

接口没有独立的权限 scope：同一个 Token 可以访问本文档列出的所有端点。服务对外监听后，Token 必须视为局域网服务凭据，只允许发送给受信任的第三方程序。

### 外部访问配置

默认绑定地址为 `+`，表示监听本机所有 IPv4 网卡。可以通过用户级环境变量 `LumineFairyNotificationBindAddress` 修改：

```powershell
# 默认值：所有 IPv4 网卡
[Environment]::SetEnvironmentVariable('LumineFairyNotificationBindAddress', '+', 'User')

# 更安全的选择：只绑定 Fairy 所在计算机的指定局域网地址
[Environment]::SetEnvironmentVariable('LumineFairyNotificationBindAddress', '192.168.1.20', 'User')
```

修改环境变量后必须重启 Fairy。允许的值包括 `+`、`*`、`0.0.0.0`（所有 IPv4 网卡），或一个具体的 IPv4 地址/主机名。默认全网卡监听时，设置页会显示检测到的第一个非回环 IPv4 地址；多网卡环境应优先配置具体地址。

Windows `HttpListener` 使用非回环地址时，通常需要管理员注册 URL ACL。只需注册一次：

```powershell
netsh http add urlacl url=http://+:18765/ user="$env:USERDOMAIN\$env:USERNAME"
```

如果改为具体绑定地址，应将 URL ACL 中的地址同步改为该地址。使用以下命令检查现有注册：

```powershell
netsh http show urlacl
```

还需要在 Fairy 所在 Windows 计算机的防火墙中放行 TCP `18765`。建议只允许实际第三方程序所在的固定 IP，例如 QwenPaw/NAS 地址为 `192.168.1.100` 时：

```powershell
New-NetFirewallRule `
  -DisplayName 'Lumine Fairy Notification API' `
  -Direction Inbound `
  -Protocol TCP `
  -LocalPort 18765 `
  -Action Allow `
  -Profile Private `
  -RemoteAddress 192.168.1.100
```

不要配置公网端口转发；外部访问应限定在受信任的局域网和防火墙来源地址内。

## 3. 通用约定

- 请求和响应使用 UTF-8 JSON。
- JSON 字段使用 camelCase，例如 `notificationId`、`createdAt`。
- 时间使用 ISO 8601 格式，服务端生成的通知时间以 UTC 为准。
- 通知 ID 由服务端生成，为不带连字符的 32 位 GUID 字符串。
- 所有端点均位于 `/api/v1` 下；第三方程序不得依赖未版本化路径。
- Fairy 当前不会自动过期或删除通知记录；`durationSeconds` 只作为通知展示时长数据保存。

## 4. 端点总览

| 方法 | 路径 | 用途 |
| --- | --- | --- |
| `POST` | `/api/v1/notifications` | 创建通知或按幂等键返回已有通知 |
| `GET` | `/api/v1/notifications/{notificationId}` | 查询通知详情和处理状态 |
| `POST` | `/api/v1/notifications/{notificationId}/actions/{actionId}` | 执行通知中声明的一个动作 |
| `POST` | `/api/v1/notifications/{notificationId}/responses` | 提交问题通知的解决方案或自定义指示 |

## 5. 创建通知

### 请求

```http
POST /api/v1/notifications HTTP/1.1
Host: <Fairy-IP>:18765
Authorization: Bearer <notification-token>
Content-Type: application/json
```

请求体：

```json
{
  "title": "分析完成",
  "body": "发现 3 个待办事项",
  "source": "my-third-party-app",
  "category": "work",
  "severity": "info",
  "dismissPolicy": "timeout",
  "durationSeconds": 8,
  "deduplicationKey": "my-third-party-app:analysis:2026-08-31",
  "notificationType": "normal",
  "actions": [
    {
      "id": "open",
      "label": "查看详情",
      "kind": "open"
    }
  ],
  "payload": {
    "recordId": "analysis-20260831-001",
    "url": "https://example.invalid/records/analysis-20260831-001"
  }
}
```

字段说明：

| 字段 | 类型 | 必填 | 默认值 | 说明 |
| --- | --- | --- | --- | --- |
| `title` | string | 是 | — | 通知标题；不能为空或全为空白字符。 |
| `body` | string | 是 | — | 通知正文；建议始终传字符串，可传空字符串。 |
| `source` | string | 否 | `local` | 来源标识，用于通知中心筛选。 |
| `category` | string | 否 | `other` | 分类标识，例如 `work`、`wechat`、`system`、`remote`。 |
| `severity` | string | 否 | `info` | 级别标识，例如 `info`、`success`、`warning`、`error`、`critical`。当前服务保存并传递该值，不限制枚举。 |
| `dismissPolicy` | string | 否 | `timeout` | 展示策略标识。当前服务保存并传递该值。 |
| `durationSeconds` | integer | 否 | `5` | 展示时长数据，服务端限制在 `1` 到 `86400` 秒之间，超出范围时截断到边界。 |
| `deduplicationKey` | string 或 null | 否 | `null` | 非空时启用幂等；同一 Key 再次发送会返回最近的一条已有记录。 |
| `notificationType` | string | 否 | `normal` | 通知类型标识；问题通知通常使用 `question`，但问题处理由 `question` 对象是否存在决定。 |
| `actions` | array | 否 | `[]` | 操作按钮列表。每个元素包含 `id`、`label` 和可选的 `kind`。 |
| `question` | object 或 null | 否 | `null` | 问题通知的结构化问题对象，见第 8 节。 |
| `payload` | object 或 null | 否 | `{}` | 供动作处理或第三方保存上下文的结构化数据，不直接拼接到通知正文。 |

`actions` 元素格式：

```json
{
  "id": "confirm",
  "label": "确认",
  "kind": "confirm-todo"
}
```

`id` 用于构造动作 URL，调用时按不区分大小写匹配。`kind` 当前由 Fairy 内部动作处理器识别：`confirm-todo` 和 `reject-todo` 用于处理 TODO 候选，`question-solution` 用于提交问题方案，其他值会被视为普通处理动作并将通知标记为已解决。第三方自定义动作不会触发第三方回调。

### 响应

成功时返回 `201 Created`：

```json
{
  "notificationId": "7f7b4e4f6ce44f68a6c8a4dd88dd4f1e",
  "status": "accepted",
  "createdAt": "2026-08-31T08:30:00.0000000+00:00"
}
```

如果提交了非空 `deduplicationKey` 且该 Key 已存在，服务端不会新建记录，而是返回已有通知的 ID 和原创建时间，HTTP 状态仍为 `201`。当前实现不会比较两次请求的标题、正文或其他字段，也不会返回 `409`。

## 6. 查询通知

### 请求

```http
GET /api/v1/notifications/7f7b4e4f6ce44f68a6c8a4dd88dd4f1e HTTP/1.1
Host: <Fairy-IP>:18765
Authorization: Bearer <notification-token>
```

### 响应

找到通知时返回 `200 OK`：

```json
{
  "id": "7f7b4e4f6ce44f68a6c8a4dd88dd4f1e",
  "title": "分析完成",
  "body": "发现 3 个待办事项",
  "source": "my-third-party-app",
  "category": "work",
  "severity": "info",
  "dismissPolicy": "timeout",
  "durationSeconds": 8,
  "notificationType": "normal",
  "createdAt": "2026-08-31T08:30:00+00:00",
  "isRead": false,
  "isResolved": false,
  "deduplicationKey": "my-third-party-app:analysis:2026-08-31",
  "actions": [
    {
      "id": "open",
      "label": "查看详情",
      "kind": "open"
    }
  ],
  "question": null,
  "payload": {
    "recordId": "analysis-20260831-001",
    "url": "https://example.invalid/records/analysis-20260831-001"
  },
  "relatedTaskId": null
}
```

`isRead` 表示用户是否已读，`isResolved` 表示通知动作或问题处理是否已完成。第三方接口当前只提供查询这两个状态的能力，不提供单独的“标记已读”端点。

## 7. 执行通知动作

动作必须先出现在通知的 `actions` 数组中，且通知尚未解决。

### 请求

```http
POST /api/v1/notifications/{notificationId}/actions/{actionId} HTTP/1.1
Host: <Fairy-IP>:18765
Authorization: Bearer <notification-token>
```

动作端点不需要请求体。示例：

```text
POST http://<Fairy-IP>:18765/api/v1/notifications/7f7b4e4f6ce44f68a6c8a4dd88dd4f1e/actions/open
```

成功时返回 `200 OK`：

```json
{
  "status": "completed",
  "notificationId": "7f7b4e4f6ce44f68a6c8a4dd4f1",
  "message": "通知已处理。"
}
```

处理规则：

- `confirm-todo`：从 `payload.todoId` 读取 TODO 候选并确认，成功后解决原通知。
- `reject-todo`：从 `payload.todoId` 读取 TODO 候选并拒绝，成功后解决原通知。
- `question-solution`：等价于提交 `solutionId = actionId` 的问题响应。
- 其他 `kind`：直接解决通知并返回“通知已处理”。

动作成功后，桌面端会刷新通知中心。对已解决通知重复执行动作、执行不存在的动作，或 TODO 标识缺失时，当前服务会返回 `500`，调用方不得依赖错误消息文本判断业务状态。

## 8. 提交问题通知响应

当查询结果中的 `question` 不为 `null` 时，可以提交解决方案或自定义处理指示。

### 问题对象

```json
{
  "problem": "客户要求提前交付，但当前排期可能无法满足，应该如何处理？",
  "source": {
    "conversationId": "conversation-id",
    "participants": ["联系人 A", "联系人 B"],
    "from": "联系人 A",
    "to": "联系人 B",
    "messageRefs": ["message-id-1", "message-id-2"]
  },
  "solutions": [
    {
      "id": "s1",
      "label": "确认延期",
      "instruction": "向对方说明当前排期并提出新日期",
      "recommended": true
    },
    {
      "id": "s2",
      "label": "调整范围",
      "instruction": "先交付核心功能，延期非核心内容",
      "recommended": false
    }
  ],
  "allowCustomInput": true,
  "inputPlaceholder": "不使用以上解决方案时，请输入你的处理要求"
}
```

字段 `source` 的实际结构为 `conversationId`、`participants`、`from`、`to` 和 `messageRefs`，当前没有 `timeRange` 字段。`recommended` 用于客户端展示推荐方案。

### 请求

```http
POST /api/v1/notifications/{notificationId}/responses HTTP/1.1
Host: <Fairy-IP>:18765
Authorization: Bearer <notification-token>
Content-Type: application/json
```

选择方案：

```json
{
  "solutionId": "s1",
  "customInput": null
}
```

使用自定义指示：

```json
{
  "solutionId": null,
  "customInput": "先向客户确认可接受的最晚交付日期，再更新项目排期。"
}
```

服务端优先使用有效 `solutionId` 对应方案的 `instruction`；没有匹配方案时使用非空 `customInput`。两者都为空时请求无效。

成功时返回 `200 OK`：

```json
{
  "status": "submitted",
  "responseLength": 128
}
```

问题处理会调用 Fairy 当前配置的 QwenPaw Agent。QwenPaw 返回后，原问题通知被标记为已解决，并新增一条“微信疑问已提交”通知。若分析服务未配置或不可用，返回 `503`；在分析调用失败时，原通知不会在该调用路径中被标记为已解决。

## 9. 错误响应与状态码

错误响应使用 JSON：

```json
{
  "error": "not_found"
}
```

部分服务端异常还会附带 `message`：

```json
{
  "error": "InvalidOperationException",
  "message": "通知操作不存在或已失效。"
}
```

| HTTP 状态码 | `error` 示例 | 含义 |
| --- | --- | --- |
| `201` | — | 通知已接受并持久化；幂等命中时也使用此状态。 |
| `200` | — | 查询或动作/问题响应处理成功。 |
| `400` | `title and body are required` | 通知标题为空、正文缺失，JSON 无法解析，或问题响应请求体为 `null`。 |
| `401` | `unauthorized` | 缺少或不匹配的 `Authorization: Bearer ...`。 |
| `404` | `not_found` | 通知 ID 不存在。 |
| `500` | 异常类型名 | 业务动作失败或服务端未处理异常。 |
| `503` | `analysis_unavailable` | 动作或问题响应需要分析服务，但当前未注入分析组件。 |

当前实现没有统一的 `409 Conflict`、`429 Too Many Requests`、请求体大小限制或发送频率限制。调用方应设置自己的超时、重试和速率策略；对 `POST /notifications` 进行重试时应使用稳定的 `deduplicationKey`。

## 10. 集成示例

### PowerShell：发送通知并查询状态

```powershell
$baseUrl = 'http://<Fairy-IP>:18765'
$token = '<从 Fairy 设置页读取的 Token>'
$headers = @{ Authorization = "Bearer $token" }

$request = @{
  title = '构建完成'
  body = '订单服务已完成构建。'
  source = 'order-service'
  category = 'work'
  severity = 'success'
  durationSeconds = 8
  deduplicationKey = "order-service:build:$([DateTime]::UtcNow.ToString('yyyyMMddHHmm'))"
  payload = @{ buildId = 'build-001' }
} | ConvertTo-Json -Depth 5

$created = Invoke-RestMethod `
  -Method Post `
  -Uri "$baseUrl/api/v1/notifications" `
  -Headers $headers `
  -ContentType 'application/json' `
  -Body $request

$notification = Invoke-RestMethod `
  -Method Get `
  -Uri "$baseUrl/api/v1/notifications/$($created.notificationId)" `
  -Headers $headers

$notification | ConvertTo-Json -Depth 10
```

### curl：执行动作

```bash
curl -X POST \
  'http://<Fairy-IP>:18765/api/v1/notifications/<notificationId>/actions/<actionId>' \
  -H 'Authorization: Bearer <notification-token>'
```

## 11. 启动与验证

1. 启动 Lumine.Fairy 桌面客户端并等待初始化完成。
2. 在设置页确认“第三方通知接口”显示为 `http://<Fairy-IP>:18765/api/v1/notifications`，并读取当前 Token。
3. 使用一个最小通知请求调用 `POST /api/v1/notifications`。
4. 使用响应中的 `notificationId` 调用 `GET /api/v1/notifications/{notificationId}`。
5. 在 Fairy 通知中心确认通知已出现；若带有 `actions`，再调用对应动作 URL。

如果连接被拒绝，依次确认 Fairy 进程仍在运行、设置页显示接口已启动、URL ACL 已注册、Windows 防火墙已放行，并且第三方程序访问的是 Fairy 所在计算机的正确局域网 IP。不要使用公网端口转发访问该接口。

## 12. 实现位置

- HTTP 路由、认证和 JSON 响应：`src/Lumine.Fairy.Infrastructure/NotificationHttpServer.cs`
- 通知持久化、幂等查询和状态字段：`src/Lumine.Fairy.Infrastructure/SqliteNotificationStore.cs`
- 通知请求/响应契约：`src/Lumine.Fairy.Protocol/Contracts.cs` 中的 `NotificationRequest`、`NotificationResponse`、`NotificationRecord` 等类型。
- 桌面端启动接口和设置页展示：`src/Lumine.Fairy.Desktop/ViewModels/MainWindowViewModel.cs` 与 `src/Lumine.Fairy.Desktop/Views/MainWindow.axaml`。
