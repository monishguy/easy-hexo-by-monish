#requires -Version 7.0
<#
.SYNOPSIS
    在没有安装 Visual Studio 的机器上构建 Easy Hexo 的 unpackaged 运行包（含 resources.pri，可选打 zip）。

.DESCRIPTION
    WinUI 3 的 PRI 生成 / MSIX 打包任务由 Visual Studio 的 MSBuild 提供，纯 .NET SDK 的
    `dotnet build` 会在最后一步报 MSB4062。本脚本的处理方式：

      1. 用 .NET SDK 编译 C# 与 XAML（这一步是完整可用的，产物在 bin 目录）；
      2. 用 Microsoft.Windows.SDK.BuildTools 包里的 makepri.exe 手工合并出 resources.pri
         （WinUI 3 运行期需要它来解析 XAML / 主题资源，缺失会报
          “Cannot locate resource from 'ms-appx:///Microsoft.UI.Xaml/Themes/themeresources.xaml'”）；
      3. 默认以 .NET 自包含 + Windows App SDK 自包含的方式输出，解压即可在
         Windows 10 1809+ / Windows 11 x64 上直接运行；
      4. 打成 releases\EasyHexo-<版本>-<配置>-<RID>.zip。

    正式发布仍推荐用 Visual Studio 的“发布”功能；本脚本用于无 VS 环境 / 本地验证 / CI。

.PARAMETER Configuration
    Debug 或 Release，默认 Debug（即“release-debug”产物）。

.PARAMETER Platform
    x64 / x86 / ARM64，默认 x64。

.PARAMETER FrameworkDependent
    不把 .NET 运行时打进包里（体积小，但目标机器需要安装 .NET 8 Desktop Runtime）。

.PARAMETER NoZip
    只构建并生成 resources.pri，不打 zip。

.PARAMETER Version
    产物命名用的版本号，默认取 EasyHexo.csproj 里的 <Version>。

.EXAMPLE
    pwsh tools\build-unpackaged.ps1

.EXAMPLE
    pwsh tools\build-unpackaged.ps1 -Configuration Release -Platform x64 -NoZip
#>
[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Debug',

    [ValidateSet('x64', 'x86', 'ARM64')]
    [string]$Platform = 'x64',

    [switch]$FrameworkDependent,

    [switch]$NoZip,

    [string]$Version = ''
)

$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$projectPath = Join-Path $repoRoot 'EasyHexo\EasyHexo.csproj'

$rid = switch ($Platform) {
    'x64' { 'win-x64' }
    'x86' { 'win-x86' }
    'ARM64' { 'win-arm64' }
    default { throw "不支持的平台：$Platform" }
}

$tfmLiteral = 'net8.0-windows10.0.19041.0'
$binDir = Join-Path $repoRoot "EasyHexo\bin\$Platform\$Configuration\$tfmLiteral\$rid"
$objDir = Join-Path $repoRoot "EasyHexo\obj\$Platform\$Configuration\$tfmLiteral\$rid"

if ([string]::IsNullOrWhiteSpace($Version)) {
    $csproj = Get-Content $projectPath -Raw
    $match = [regex]::Match($csproj, '<Version>([^<]+)</Version>')
    $Version = if ($match.Success) { $match.Groups[1].Value.Trim() } else { '1.0.0' }
}

Write-Host "== Easy Hexo 构建 ==" -ForegroundColor Cyan
Write-Host "配置=$Configuration  平台=$Platform  RID=$rid  版本=$Version  自包含=$(-not $FrameworkDependent)"

# ── 1. 编译（忽略最后 VS 专有打包任务的报错，只要产物存在即可） ──────────────
$selfContained = if ($FrameworkDependent) { 'false' } else { 'true' }
$buildArgs = @(
    'build', $projectPath,
    '-c', $Configuration,
    "-p:Platform=$Platform",
    '-p:AppxGeneratePriEnabled=false',
    "-p:SelfContained=$selfContained",
    '-v:m'
)

Write-Host "`n[1/4] dotnet build ..." -ForegroundColor Yellow
$buildOutput = & dotnet @buildArgs 2>&1 | Out-String
$buildExit = $LASTEXITCODE
$buildLog = Join-Path $objDir "build-$Configuration-$Platform.log"
New-Item -ItemType Directory -Force -Path $objDir | Out-Null
Set-Content -Path $buildLog -Value $buildOutput -Encoding utf8

$exePath = Join-Path $binDir 'EasyHexo.exe'
$dllPath = Join-Path $binDir 'EasyHexo.dll'
if (-not (Test-Path $exePath) -or -not (Test-Path $dllPath)) {
    Write-Host $buildOutput -ForegroundColor DarkGray
    throw "编译失败：$binDir 下没有找到 EasyHexo.exe / EasyHexo.dll（完整日志：$buildLog）"
}

if ($buildExit -ne 0) {
    Write-Host "  注意：dotnet build 退出码为 $buildExit（通常是缺少 VS 的 AppxPackage 打包任务），但编译产物已生成，继续。" -ForegroundColor DarkYellow
}
else {
    Write-Host "  编译完成" -ForegroundColor Green
}

# ── 2. 生成 resources.pri（makepri） ──────────────────────────────────────
Write-Host "`n[2/4] 生成 resources.pri ..." -ForegroundColor Yellow

$makepri = Get-ChildItem (Join-Path $env:USERPROFILE '.nuget\packages\microsoft.windows.sdk.buildtools') `
    -Recurse -Filter 'makepri.exe' -ErrorAction SilentlyContinue |
    Where-Object { $_.FullName -match '\\x64\\makepri\.exe$' } |
    Sort-Object FullName -Descending |
    Select-Object -First 1

if ($null -eq $makepri) {
    throw "没有找到 makepri.exe。请先还原 NuGet 包（Microsoft.Windows.SDK.BuildTools），或直接使用 Visual Studio 构建。"
}

Write-Host ("  makepri: " + $makepri.FullName) -ForegroundColor DarkGray

$stage = Join-Path $objDir 'pri-stage'
Remove-Item $stage -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force -Path (Join-Path $stage 'Views') | Out-Null

# 应用自身的文件 + Windows App SDK 的 PRI（后者提供 WinUI 主题资源索引）
$stageFiles = @(
    'EasyHexo.exe', 'EasyHexo.dll', 'EasyHexo.deps.json', 'EasyHexo.runtimeconfig.json',
    'App.xbf', 'MainWindow.xbf', 'Microsoft.UI.pri', 'Microsoft.UI.Xaml.Controls.pri'
)
foreach ($file in $stageFiles) {
    $source = Join-Path $binDir $file
    if (Test-Path $source) {
        Copy-Item $source $stage -Force
    }
    else {
        Write-Warning "  缺少 $file（若为 PRI 文件，说明本机未生成 Windows App SDK 自包含产物）"
    }
}

# 页面级 XBF 在 obj 的 Views 目录下
$viewsXbf = Join-Path $objDir 'Views\*.xbf'
if (Test-Path (Join-Path $objDir 'Views')) {
    Copy-Item $viewsXbf (Join-Path $stage 'Views') -Force
}
else {
    throw "没有找到页面 XBF（$objDir\Views）。请确认上面编译步骤成功。"
}

$priconfig = Join-Path $stage 'priconfig.xml'
& $makepri.FullName createconfig /cf $priconfig /dq 'lang-zh-CN' /o | Out-Null

Push-Location $stage
try {
    $priOutput = & $makepri.FullName new /pr $stage /cf $priconfig /of (Join-Path $stage 'resources.pri') /o 2>&1 | Out-String
}
finally {
    Pop-Location
}

$priPath = Join-Path $stage 'resources.pri'
if (-not (Test-Path $priPath)) {
    Write-Host $priOutput -ForegroundColor DarkGray
    throw "resources.pri 生成失败。"
}

Copy-Item $priPath (Join-Path $binDir 'resources.pri') -Force
Write-Host ("  resources.pri: " + [math]::Round((Get-Item $priPath).Length / 1KB, 1) + " KB") -ForegroundColor Green

# ── 3. 校验产物 ─────────────────────────────────────────────────────────
Write-Host "`n[3/4] 校验产物 ..." -ForegroundColor Yellow
$required = @('EasyHexo.exe', 'EasyHexo.dll', 'resources.pri')
if (-not $FrameworkDependent) {
    $required += 'coreclr.dll', 'System.Private.CoreLib.dll'
}
$required += 'Microsoft.ui.xaml.dll', 'Microsoft.WindowsAppRuntime.Bootstrap.dll'

$missing = @()
foreach ($file in $required) {
    if (-not (Test-Path (Join-Path $binDir $file))) { $missing += $file }
}
if ($missing.Count -gt 0) {
    throw ("产物缺少必要文件： " + ($missing -join ', '))
}

$totalMb = [math]::Round(((Get-ChildItem $binDir -Recurse -File | Measure-Object Length -Sum).Sum / 1MB), 1)
Write-Host "  产物目录：$binDir（$totalMb MB）" -ForegroundColor Green

# ── 4. 打包 zip ────────────────────────────────────────────────────────
if ($NoZip) {
    Write-Host "`n已按 -NoZip 跳过打包。" -ForegroundColor Cyan
    return
}

Write-Host "`n[4/4] 打包 zip ..." -ForegroundColor Yellow

$folderName = "EasyHexo-$Version-$($Configuration.ToLower())-$rid"
$zipStageRoot = Join-Path $objDir 'zip-stage'
$zipStage = Join-Path $zipStageRoot $folderName
Remove-Item $zipStageRoot -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force -Path $zipStage | Out-Null

Copy-Item (Join-Path $binDir '*') $zipStage -Recurse -Force

# 附上说明文档（README 引用的截图一并带上）
Copy-Item (Join-Path $repoRoot 'README.md') $zipStage -Force
$docsSource = Join-Path $repoRoot 'docs'
if (Test-Path $docsSource) {
    Copy-Item $docsSource (Join-Path $zipStage 'docs') -Recurse -Force
}

$runReadme = @"
Easy Hexo $Version（$Configuration / $rid）
================================================

运行
----
1. 解压本压缩包到任意目录（路径不要含特殊权限限制，例如不要放在 C:\Program Files 下）；
2. 双击 EasyHexo.exe 即可启动，无需安装。
   $(
        if ($FrameworkDependent) { "3. 本包依赖 .NET 8 Desktop Runtime 与 Windows App Runtime，如果提示缺少运行时，请先安装它们。" }
        else { "3. 本包已包含 .NET 8 运行时与 Windows App SDK 运行时，解压即用。" }
   )

系统要求
--------
* Windows 10 1809（17763）或更高版本 / Windows 11，$rid 架构
* 普通用户权限即可；请不要以管理员身份运行（Windows 会禁止从资源管理器拖放文件）

首次使用
--------
* 启动后选择 Hexo 根目录（需包含 _config.yml 与 source 文件夹）；
* 选择结果保存在 %LOCALAPPDATA%\EasyHexo\settings.json；
* 应用日志：%LOCALAPPDATA%\EasyHexo\logs\easyhexo.log；
* 修改 _config.yml 时会自动备份为 _config.yml.easyhexo.bak。

目录说明
--------
* EasyHexo.exe               主程序
* *.dll / *.pri / *.xbf      运行时与资源（请勿删除）
* README.md, docs\           源码说明文档（含开发环境与实现说明）

注意
----
本包是 $Configuration 配置的构建产物，用于测试/试用；
正式对外发布建议用 Visual Studio 打开 EasyHexo.sln 以 Release 配置重新生成。
"@

$runReadmePath = Join-Path $zipStage '运行说明.txt'
[System.IO.File]::WriteAllText($runReadmePath, $runReadme, (New-Object System.Text.UTF8Encoding($true)))

$releasesDir = Join-Path $repoRoot 'releases'
New-Item -ItemType Directory -Force -Path $releasesDir | Out-Null
$zipPath = Join-Path $releasesDir "$folderName.zip"
Remove-Item $zipPath -Force -ErrorAction SilentlyContinue

Add-Type -AssemblyName System.IO.Compression.FileSystem
# includeBaseDirectory = true → zip 内保留 “EasyHexo-<版本>-<配置>-<RID>/” 顶层文件夹
[System.IO.Compression.ZipFile]::CreateFromDirectory(
    $zipStage,
    $zipPath,
    [System.IO.Compression.CompressionLevel]::Optimal,
    $true)

$zipItem = Get-Item $zipPath
$hash = (Get-FileHash $zipPath -Algorithm SHA256).Hash
Write-Host ("  zip: " + $zipItem.FullName) -ForegroundColor Green
Write-Host ("  大小: " + [math]::Round($zipItem.Length / 1MB, 1) + " MB") -ForegroundColor Green
Write-Host ("  SHA256: " + $hash.ToLower()) -ForegroundColor Green

# 供上层汇总 SHA256SUMS
[pscustomobject]@{
    Configuration = $Configuration
    Platform      = $Platform
    Rid           = $rid
    ZipPath       = $zipItem.FullName
    ZipName       = $zipItem.Name
    SizeMB        = [math]::Round($zipItem.Length / 1MB, 1)
    Sha256        = $hash.ToLower()
}
