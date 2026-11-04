# Lumine.Fairy Windows 发布顺序

本文是 Lumine.Fairy Windows x64 分发包的当前发布流程。每次发布都按本文件的顺序执行，不直接复用旧的 `bin`、`artifacts` 或 `dist` 目录。

## 发布目标

- 平台：Windows 10/11 x64。
- 主程序：`Lumine.Fairy.Desktop`，`.NET 10` 自包含发布。
- 随包子应用：`Lumine.Fairy.TodoList.App`、`Lumine.Fairy.ContactManagement.App`，均为自包含发布。
- 随包工具：仓库中的 `WeChatDataAnalysisRelease`。
- 交付物：`dist/Lumine.Fairy-win-x64.zip`；解压目录同时保留在 `dist/Lumine.Fairy-win-x64/` 供验收。

## 固定顺序

### 1. 发布前检查

在仓库根目录执行：

```powershell
git status --short
dotnet --info
```

确认没有把微信数据库、`account_keys.json`、真实 `wechat-context.local.json`、QwenPaw 令牌或聊天正文纳入待发布文件。源码中的本地配置只允许存在于被 `.gitignore` 排除的位置，不能手工复制到发布目录。

### 2. 测试

```powershell
dotnet test Lumine.Fairy.slnx -c Release
```

测试失败时停止发布，先修复失败原因。

### 3. 创建全新的构建目录

```powershell
$repoRoot = (Get-Location).Path
$releaseWork = Join-Path $repoRoot 'artifacts/release'
$desktopOut = Join-Path $releaseWork 'desktop'
$todoOut = Join-Path $releaseWork 'todolist'
$contactOut = Join-Path $releaseWork 'contact-management'
$packageRoot = Join-Path $releaseWork 'Lumine.Fairy-win-x64'
$distRoot = Join-Path $repoRoot 'dist'
$packageDist = Join-Path $distRoot 'Lumine.Fairy-win-x64'
$archivePath = Join-Path $distRoot 'Lumine.Fairy-win-x64.zip'

New-Item -ItemType Directory -Force $releaseWork, $distRoot | Out-Null
foreach ($path in @($desktopOut, $todoOut, $contactOut, $packageRoot, $packageDist)) {
    if (Test-Path -LiteralPath $path) {
        Remove-Item -LiteralPath $path -Recurse -Force
    }
}
if (Test-Path -LiteralPath $archivePath) {
    Remove-Item -LiteralPath $archivePath -Force
}
```

### 4. 按主程序、Todo、联系人管理的顺序分别发布

```powershell
dotnet publish 'src/Lumine.Fairy.Desktop/Lumine.Fairy.Desktop.csproj' `
    -c Release -r win-x64 --self-contained true -o $desktopOut

dotnet publish 'Lumine.Fairy.TodoList.App/Lumine.Fairy.TodoList.App.csproj' `
    -c Release -r win-x64 --self-contained true -o $todoOut

dotnet publish 'Lumine.Fairy.ContactManagement.App/Lumine.Fairy.ContactManagement.App.csproj' `
    -c Release -r win-x64 --self-contained true -o $contactOut
```

### 5. 组装分发目录

```powershell
New-Item -ItemType Directory -Force $packageRoot, `
    (Join-Path $packageRoot 'tools/todolist'), `
    (Join-Path $packageRoot 'tools/contact-management'), `
    (Join-Path $packageRoot 'Docs/user'), `
    (Join-Path $packageRoot 'Docs/developer/operations'), `
    (Join-Path $packageRoot 'Docs/mcp/fairy-side'), `
    (Join-Path $packageRoot 'Docs/mcp/qwenpaw-side') | Out-Null

Copy-Item -Path (Join-Path $desktopOut '*') -Destination $packageRoot -Recurse -Force
Copy-Item -Path (Join-Path $todoOut '*') -Destination (Join-Path $packageRoot 'tools/todolist') -Recurse -Force
Copy-Item -Path (Join-Path $contactOut '*') -Destination (Join-Path $packageRoot 'tools/contact-management') -Recurse -Force
Copy-Item -Path 'WeChatDataAnalysisRelease' -Destination $packageRoot -Recurse -Force

Copy-Item -Path 'docs/user/*' -Destination (Join-Path $packageRoot 'Docs/user') -Recurse -Force
Copy-Item -Path 'docs/developer/operations/*' -Destination (Join-Path $packageRoot 'Docs/developer/operations') -Recurse -Force
Copy-Item -LiteralPath 'tools/lumine-fairy-wechat-context-mcp/server.py' `
    -Destination (Join-Path $packageRoot 'Docs/mcp/qwenpaw-side/server.py') -Force
Copy-Item -LiteralPath 'tools/lumine-fairy-wechat-context-mcp/wechat-context.local.json.example' `
    -Destination (Join-Path $packageRoot 'Docs/mcp/qwenpaw-side/wechat-context.local.json.example') -Force
Copy-Item -LiteralPath 'src/Lumine.Fairy.Desktop/wechat-context.local.json.example' `
    -Destination (Join-Path $packageRoot 'Docs/mcp/fairy-side/wechat-context.local.json.example') -Force
Copy-Item -LiteralPath 'docs/agent/release-agent-setup.md' `
    -Destination (Join-Path $packageRoot 'AGENT-SETUP.md') -Force
Copy-Item -LiteralPath 'docs/user/release-readme.md' `
    -Destination (Join-Path $packageRoot 'README.md') -Force
```

### 6. 清理发布目录中的本地配置

主程序项目允许本地 `wechat-context.local.json` 参与本机运行，因此发布后必须显式删除它；示例文件可以保留：

```powershell
$localConfig = Join-Path $packageRoot 'wechat-context.local.json'
if (Test-Path -LiteralPath $localConfig) {
    Remove-Item -LiteralPath $localConfig -Force
}
```

### 7. 扫描敏感文件并检查关键文件

```powershell
$forbidden = Get-ChildItem -LiteralPath $packageRoot -Recurse -File |
    Where-Object { $_.Name -in @('account_keys.json', 'wechat-context.local.json', 'qwenpaw.token', 'qwenpaw.credential') }
if ($forbidden) {
    throw "发现禁止发布的敏感文件：$($forbidden.FullName -join ', ')"
}

$required = @(
    'Lumine.Fairy.Desktop.exe',
    'tools/todolist/Lumine.Fairy.TodoList.App.exe',
    'tools/contact-management/Lumine.Fairy.ContactManagement.App.exe',
    'WeChatDataAnalysisRelease/WeChatDataAnalysis/WeChatDataAnalysis.exe',
    'WeChatDataAnalysisRelease/WeChatDataAnalysis/resources/backend/wechat-backend.exe',
    'AGENT-SETUP.md',
    'README.md',
    'Docs/user/release-user-configuration.md',
    'Docs/mcp/qwenpaw-side/server.py'
)
foreach ($relativePath in $required) {
    if (-not (Test-Path -LiteralPath (Join-Path $packageRoot $relativePath))) {
        throw "缺少发布文件：$relativePath"
    }
}
```

### 8. 生成 ZIP

```powershell
New-Item -ItemType Directory -Force $packageDist | Out-Null
Copy-Item -Path (Join-Path $packageRoot '*') -Destination $packageDist -Recurse -Force

Push-Location $distRoot
try {
    tar -a -c -f 'Lumine.Fairy-win-x64.zip' 'Lumine.Fairy-win-x64'
}
finally {
    Pop-Location
}
```

### 9. 校验 ZIP 内容

```powershell
$entries = tar -tf $archivePath
foreach ($relativePath in $required) {
    $archiveEntry = "Lumine.Fairy-win-x64/$relativePath"
    if ($entries -notcontains $archiveEntry) {
        throw "ZIP 缺少发布文件：$relativePath"
    }
}

$archiveForbidden = $entries | Where-Object {
    $_ -match '(account_keys\.json|wechat-context\.local\.json|qwenpaw\.token)$'
}
if ($archiveForbidden) {
    throw "ZIP 包含禁止发布的敏感文件：$($archiveForbidden -join ', ')"
}

$archiveFile = Get-Item -LiteralPath $archivePath
Write-Output "发布包：$($archiveFile.FullName)"
Write-Output ("大小：{0:N2} MB" -f ($archiveFile.Length / 1MB))
Write-Output "条目数：$($entries.Count)"
```

## 交付与验收

- 发送 `dist/Lumine.Fairy-win-x64.zip`，不要发送接收方不需要的源码、`artifacts/release` 或本机数据目录。
- 接收方解压到固定、简短、无中文/空格的目录后，按包内 `AGENT-SETUP.md` 配置。
- 接收方必须在自己的 Windows 用户和本机微信环境中重新生成 `account_keys.json`，并重新登录 QwenPaw；不得复制发布者的密钥或令牌。
- 首次验收至少确认主程序启动、托盘图标出现、Todo/联系人子应用可打开、微信工具可启动，且 ZIP 内无敏感文件。
