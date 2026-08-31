# Fairy 设置

在左侧打开“设置”可以配置 QwenPaw、微信分析和本地通知相关选项。

## 开机启动

勾选“开机启动 Fairy”后，Fairy 会加入当前 Windows 用户的启动项，并在设置区域显示成功提示。关闭选项后会移除该启动项。

## 外部通知接入

设置页的“第三方通知接口”区域会显示接口地址和 Bearer Token。Token 默认以密码样式隐藏，点击右侧眼睛按钮可以立即显示或隐藏；显示的 Bearer Token 就是接口实际使用的认证凭据，外部程序应将其放入 `Authorization: Bearer <token>` 请求头。如果外部程序运行在另一台电脑上，请按[第三方通知 HTTP 接口开发文档](../developer/operations/third-party-notification-api.md)完成网络访问配置。
