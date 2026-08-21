# Lumine.Fairy.TodoList.App

这是从联系人管理应用中拆分出来的独立 `TodoList / 日报` 子应用。

## 当前能力

- 维护独立的 `Todo / 日报` 工作区界面
- 支持按日期管理随手记、结构化 Todo、客户问题与日报预览
- 支持 `F8` 在原始输入 / 格式化结果 / 左右对照三种视图间切换
- 支持 `Ctrl+Enter` 立即解析归置，`Ctrl+S` 保存，`Ctrl+Shift+C` 复制日报
- 支持配置 ReportMind 服务地址、用户名和密码；密码以明文保存在 TodoList 配置文件中
- 上传日报时先调用 ReportMind 登录接口获取 token，再携带 Bearer token 调用上传接口
- 与联系人域共享同一份 `SQLite` 数据库

## ReportMind 上传设置

在 TodoList 设置的“上传设置”中填写 ReportMind 服务地址、用户名和密码。密码输入框提供显示/隐藏按钮，“测试连接”会使用当前凭据调用登录接口。日报上传使用登录接口返回的用户名作为上传员工名。

设置默认保存在：

`%LocalAppData%\Lumine.Fairy\AppData\todolist\settings.json`

## 数据库位置

默认数据库文件位于：

`%LocalAppData%\Lumine.Fairy\AppData\contact-management\contact-management.db`

如果当前平台无法提供 `LocalApplicationData`，则会退回到用户目录下的 `\.lumine-fairy\AppData\contact-management\contact-management.db`。

## 本地验证

```powershell
dotnet build "Lumine.Fairy.TodoList.App\Lumine.Fairy.TodoList.App.csproj"
```

## 关系说明

- `Lumine.Fairy.ContactManagement.App`：联系人 / 公司 / 项目 / 关系管理
- `Lumine.Fairy.TodoList.App`：Todo / 日报 / 随手记工作区
- 两者共享联系人域存储层与同一份业务数据
