# 时间处理

## 责任划分

- `Lumine.Fairy.Domain.TimeZoneContext` 是统一的时间转换入口。
- `Lumine.Fairy.Infrastructure.AppSettingsStore` 只负责保存和读取时区 ID，不保存密码、Token 或聊天正文。
- Desktop ViewModel 使用 `TimeZoneContext` 将通知记录转换为显示文本；时区变更时刷新已加载项目。
- 通知数据库的 `created_at` 仍保存 UTC `DateTimeOffset`，Unix 时间仍按 Unix epoch 传输。

## 约定

- 内部时间使用 UTC 或 Unix 时间，不使用机器本地时间作为持久化值。
- 所有新增的用户可见时间必须通过 `TimeZoneContext` 转换后再格式化。
- 将 Unix 秒或毫秒转换为用户可见时间时使用 `TimeZoneContext.FromUnixTime`。
- 时区 ID 使用系统时区 ID；Windows 默认 ID 为 `China Standard Time`，非 Windows 环境支持 `Asia/Shanghai` 回退。
