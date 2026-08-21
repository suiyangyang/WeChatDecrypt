# Lumine.Fairy.ContactManagement.App

这是联系人/公司/项目/关系管理子应用的当前 MVP。

`Todo / 客户问题 / 日报 / 随手记` 工作区已拆分为独立的 `Lumine.Fairy.TodoList.App`。

## 当前能力

- 基于独立 `SQLite` 数据库文件存储全部联系人域数据
- 支持联系人、公司、项目、关系四类实体的基础 CRUD
- 导航改为左侧菜单，默认进入“主页”查看当前数据汇总
- 联系人、公司、项目、关系页统一为“顶部工具栏 + 下方表格”布局
- 新建/编辑联系人、公司、项目、关系统一使用弹窗
- 删除联系人/公司/项目时自动清理相关关系引用
- 默认在“主页”菜单展示实时统计汇总
- 支持 JSON / Excel 导入预检、提交导入、导出与示例模板生成

## 数据库位置

默认数据库文件位于：

`%LocalAppData%\Lumine.Fairy\AppData\contact-management\contact-management.db`

如果当前平台无法提供 `LocalApplicationData`，则会退回到用户目录下的 `\.lumine-fairy\AppData\contact-management\contact-management.db`。

## 本地验证

```powershell
dotnet test "Lumine.Fairy.ContactManagement.Tests\Lumine.Fairy.ContactManagement.Tests.csproj"
dotnet build "Lumine.Fairy.ContactManagement.App\Lumine.Fairy.ContactManagement.App.csproj"
```

## 相关应用

- 联系人域应用：`Lumine.Fairy.ContactManagement.App`
- Todo / 客户问题 / 日报应用：`Lumine.Fairy.TodoList.App`

## 导入导出说明

- 在“导入导出”页签可直接选择 `JSON / Excel` 文件并执行预检
- 预检通过后可提交到当前 SQLite 数据库
- 可导出当前数据快照，也可生成一个可编辑的示例导入模板（支持 `json` / `xlsx`）
- 预检结果会按“错误 / 警告 / 提示”分组展示，便于人工确认后再提交
- 当前联系人应用只接受联系人域数据：`联系人 / 公司 / 项目 / 关系`
- 如果导入文件包含 `Todo / 客户问题 / 日报 / 随手记` 相关字段或工作表，预检会直接提示不受支持

## Excel 工作表字段

当前 `xlsx` 导入导出仅支持以下 `4` 张工作表：

### 1. `Contacts`

- `Id`：联系人主键，留空时导入会自动生成
- `FullName`：联系人姓名，必填
- `Nicknames`：昵称列表，使用 `;` 分隔
- `Phones`：手机号列表，使用 `;` 分隔
- `Emails`：邮箱列表，使用 `;` 分隔
- `RelationshipType`：联系人关系类型枚举
- `Notes`：备注
- `IsActive`：是否启用，布尔值

### 2. `Companies`

- `Id`：公司主键，留空时导入会自动生成
- `Name`：公司名称，必填
- `Addresses`：地址列表，使用 `;` 分隔
- `Departments`：部门列表，使用 `;` 分隔
- `Notes`：备注
- `IsActive`：是否启用，布尔值

### 3. `Projects`

- `Id`：项目主键，留空时导入会自动生成
- `Name`：项目名称，必填
- `CompanyId`：所属公司 ID，可为空
- `CompanyName`：所属公司名称，可用来辅助解析公司引用
- `OwnerContactId`：负责人联系人 ID，可为空
- `OwnerFullName`：负责人姓名，可用来辅助解析联系人引用
- `Tags`：标签列表，使用 `;` 分隔
- `Notes`：备注
- `IsActive`：是否启用，布尔值

### 4. `RelationshipLinks`

- `Id`：关系主键，留空时导入会自动生成
- `SourceKind` / `TargetKind`：实体类型枚举，支持 `Contact`、`Company`、`Project`
- `SourceId` / `TargetId`：源/目标实体 ID
- `SourceName` / `TargetName`：源/目标实体名称，导入时可辅助解析引用
- `LinkType`：关系类型枚举
- `DepartmentName`：部门名，可为空
- `Title`：职位名，可为空
- `IsPrimary`：是否主关系，布尔值
- `Notes`：备注

### 填写建议

- 优先保留导出模板中的列名与工作表名，不要改名
- 多值字段统一使用英文分号 `;` 分隔
- 日期优先使用 `yyyy-MM-dd`，时间优先使用 ISO 8601
- Todo / 客户问题 / 日报 / 随手记 数据请改用 `Lumine.Fairy.TodoList.App` 管理

## 下一步建议

- 增加搜索、筛选与排序
- 将关系编辑从 ID 选择扩展为更友好的名称展示与反向引用
- 把联系人域查询服务接入主程序与相关 Skill
