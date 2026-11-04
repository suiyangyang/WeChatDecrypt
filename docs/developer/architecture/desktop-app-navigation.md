# 桌面 App 导航

`Lumine.Fairy.Desktop` 的主页面左侧导航包含通知中心、工作、微信分析、联系人管理、App、远程设备和设置，不提供“关于”入口。

进入设置后，设置壳层覆盖主页面：左侧替换为“返回主页面”和“常规”“工作设置”“微信分析”“通知”四个分类，右侧只呈现当前分类。返回操作恢复进入设置前的主页面；微信分析执行页的“分析设置”直接进入“微信分析”分类。

导航行的图标与文字之间保持 8 px 间距。主页面设置入口使用 `Assets/icons/nav-settings.svg` 定义的 SVG 几何，并由 Avalonia `Path` 在运行时绘制。

## App 页面

App 页面以移动端桌面式图标入口展示两个独立子应用。每个入口只显示程序图标和名称，点击整个图标块即可启动对应程序：

- `Lumine.Fairy.ContactManagement.App`：联系人、公司、项目和关系管理；
- `Lumine.Fairy.TodoList.App`：Todo、客户问题、日报和随手记管理。

主桌面项目通过 `BuiltInAppLauncher` 启动子应用。构建桌面项目时，两个子应用的输出会分别复制到桌面输出目录下的 `tools/contact-management` 和 `tools/todolist`，开发环境也会回退查找仓库中的项目输出目录。

## 图标资源

App 页面使用 Avalonia 原生 `PathIcon` 渲染联系人管理和 Todo / 日报的矢量图标。桌面窗口、EXE 和托盘使用 `Fairy.OS` 原有的 `fairy-shortcut.ico`、`fairy-tray.ico` 和 Fairy PNG 资源。

两个子应用的 EXE 图标与窗口图标分别统一使用各自的设计图标：

- 联系人管理：`Lumine.Fairy.ContactManagement.App/Assets/contact-management.ico`；
- Todo / 日报：`Lumine.Fairy.TodoList.App/Assets/todo-list.ico`。
