#requires -Version 7.0
<#
.SYNOPSIS
    根据运行包目录生成 Windows 安装包（MSI，WiX v7 命令行），并写入注册表信息 / 卸载信息 / 右键菜单。

.EXAMPLE
    pwsh tools\build-installer.ps1 -SourceDir releases\EasyHexo-1.4.0-release-win-x64 -Version 1.4.0
#>
[CmdletBinding()]
param(
    [string]$SourceDir = '',
    [string]$Version = '',
    [string]$OutputPath = '',
    [string]$IconPath = ''
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot

if ([string]::IsNullOrWhiteSpace($Version)) {
    $csproj = Get-Content (Join-Path $repoRoot 'EasyHexo\EasyHexo.csproj') -Raw
    $match = [regex]::Match($csproj, '<Version>([^<]+)</Version>')
    $Version = if ($match.Success) { $match.Groups[1].Value.Trim() } else { '1.0.0' }
}

if ([string]::IsNullOrWhiteSpace($SourceDir)) {
    $SourceDir = Join-Path $repoRoot "EasyHexo\bin\x64\Release\net8.0-windows10.0.19041.0\win-x64"
}
$SourceDir = (Resolve-Path $SourceDir).Path
if (-not (Test-Path (Join-Path $SourceDir 'EasyHexo.exe'))) {
    throw "SourceDir 下没有 EasyHexo.exe：$SourceDir"
}

if ([string]::IsNullOrWhiteSpace($IconPath)) {
    $IconPath = Join-Path $repoRoot 'EasyHexo\Assets\EasyHexo.ico'
}
$IconPath = (Resolve-Path $IconPath).Path

if ([string]::IsNullOrWhiteSpace($OutputPath)) {
    $releases = Join-Path $repoRoot 'releases'
    New-Item -ItemType Directory -Force -Path $releases | Out-Null
    $OutputPath = Join-Path $releases "EasyHexo-$Version-win-x64.msi"
}

$wix = Get-Command wix -ErrorAction SilentlyContinue
if (-not $wix) {
    throw '没有找到 wix 命令。请安装：dotnet tool install --global wix'
}

Write-Host "== 生成 MSI 安装包 ==" -ForegroundColor Cyan
Write-Host "版本=$Version`n源目录=$SourceDir`n输出=$OutputPath" -ForegroundColor DarkGray

function ConvertTo-Xml([string]$text) {
    return [System.Security.SecurityElement]::Escape($text)
}

$md5 = [System.Security.Cryptography.MD5]::Create()
function New-Id([string]$prefix, [string]$value) {
    $hash = $md5.ComputeHash([System.Text.Encoding]::UTF8.GetBytes($value))
    return $prefix + ([BitConverter]::ToString($hash) -replace '-', '').Substring(0, 12)
}

function Get-DirId([string]$relativePath) {
    if ([string]::IsNullOrEmpty($relativePath)) { return 'INSTALLFOLDER' }
    return New-Id 'd_' $relativePath
}

$root = $SourceDir
$script:lines = New-Object System.Collections.Generic.List[string]
$script:componentIds = New-Object System.Collections.Generic.List[string]

function Add-Line([string]$text, [int]$indent = 4) {
    $script:lines.Add((' ' * $indent) + $text)
}

function Write-Directory([string]$fullPath, [string]$relativePath, [int]$indent) {
    $isRoot = [string]::IsNullOrEmpty($relativePath)
    $contentIndent = $indent
    if (-not $isRoot) {
        $dirId = Get-DirId $relativePath
        Add-Line ("<Directory Id=`"$dirId`" Name=`"" + (ConvertTo-Xml (Split-Path $fullPath -Leaf)) + "`">") $indent
        $contentIndent = $indent + 2
    }

    # 当前目录下的文件
    foreach ($file in (Get-ChildItem $fullPath -File | Sort-Object Name)) {
        $rel = if ($relativePath) { "$relativePath/" + $file.Name } else { $file.Name }
        $compId = New-Id 'c_' $rel
        $fileId = New-Id 'f_' $rel
        $script:componentIds.Add($compId)
        Add-Line ("<Component Id=`"$compId`" Guid=`"*`">") $contentIndent
        $shortcut = ''
        if ($file.Name -eq 'EasyHexo.exe') {
            $shortcut = "`n" + (' ' * ($contentIndent + 2)) + '<Shortcut Id="StartMenuShortcut" Directory="ProgramMenuFolder" Name="Hexo 配置易" Target="[INSTALLFOLDER]EasyHexo.exe" WorkingDirectory="INSTALLFOLDER" Icon="AppIcon" Advertise="no" />'
        }

        Add-Line ("<File Id=`"$fileId`" Source=`"" + (ConvertTo-Xml $file.FullName) + "`" KeyPath=`"yes`" />$shortcut") ($contentIndent + 2)
        Add-Line "</Component>" $contentIndent
    }

    # 子目录
    foreach ($dir in (Get-ChildItem $fullPath -Directory | Sort-Object Name)) {
        $childRel = if ($relativePath) { "$relativePath/" + $dir.Name } else { $dir.Name }
        Write-Directory $dir.FullName $childRel $contentIndent
    }

    if (-not $isRoot) {
        Add-Line "</Directory>" $indent
    }
}

$packageHeader = @"
<Wix xmlns="http://wixtoolset.org/schemas/v4/wxs"
     xmlns:ui="http://wixtoolset.org/schemas/v4/wxs/ui">
  <Package Name="Hexo 配置易 (Easy Hexo)" Manufacturer="monishguy" Version="$Version"
           UpgradeCode="{B7D3E58C-4F2A-4C7E-9F3B-2D6A1E8C5F90}" Scope="perUser"
           Language="1033" Codepage="936" InstallerVersion="500" Compressed="yes">
    <MajorUpgrade DowngradeErrorMessage="已经安装了更新版本的 Hexo 配置易。" />
    <MediaTemplate EmbedCab="yes" />
    <Icon Id="AppIcon" SourceFile="$(ConvertTo-Xml $IconPath)" />
    <Property Id="ARPPRODUCTICON" Value="AppIcon" />
    <Property Id="ARPHELPLINK" Value="https://github.com/monishguy/easy-hexo-by-monish" />
    <Property Id="ARPCOMMENTS" Value="Hexo 配置易（Easy Hexo）" />
    <Property Id="ARPNOREPAIR" Value="1" />
    <Property Id="DISABLEADVTSHORTCUTS" Value="1" />
    <WixVariable Id="WixUILicenseRtf" Value="$(ConvertTo-Xml (Join-Path $repoRoot 'tools\installer\license.rtf'))" />
    <ui:WixUI Id="WixUI_InstallDir" InstallDirectory="INSTALLFOLDER" />

    <StandardDirectory Id="LocalAppDataFolder">
      <Directory Id="ProgramsFolder" Name="Programs">
        <Directory Id="INSTALLFOLDER" Name="EasyHexo">
"@

$packageFooter = @"
        </Directory>
      </Directory>
    </StandardDirectory>

    {REGISTRY_GROUP}

    <Feature Id="MainFeature" Title="Hexo 配置易" Level="1">
      {COMPONENT_REFS}
      <ComponentGroupRef Id="RegistryGroup" />
    </Feature>
  </Package>
</Wix>
"@

$script:lines.Clear()
Add-Line $packageHeader 0
Write-Directory $root '' 10

# 注册表组件：App Paths + .md/.txt 右键菜单
$registryGroup = @'
        <ComponentGroup Id="RegistryGroup">
          <Component Id="RegistryEntries" Guid="{4C6B8F1A-2D3E-4A5B-9C7D-8E1F2A3B4C5D}">
            <RegistryKey Root="HKCU" Key="Software\Microsoft\Windows\CurrentVersion\App Paths\EasyHexo.exe">
              <RegistryValue Type="string" Value="[INSTALLFOLDER]EasyHexo.exe" KeyPath="yes" />
            </RegistryKey>
            <RegistryKey Root="HKCU" Key="Software\Classes\SystemFileAssociations\.md\shell\EasyHexoPublish">
              <RegistryValue Type="string" Value="发布到 Hexo 博客" />
              <RegistryValue Name="Icon" Type="string" Value="&quot;[INSTALLFOLDER]EasyHexo.exe&quot;" />
              <RegistryValue Name="MultiSelectModel" Type="string" Value="Single" />
              <RegistryKey Key="command">
                <RegistryValue Type="string" Value="&quot;[INSTALLFOLDER]EasyHexo.exe&quot; --publish &quot;%1&quot;" />
              </RegistryKey>
            </RegistryKey>
            <RegistryKey Root="HKCU" Key="Software\Classes\SystemFileAssociations\.txt\shell\EasyHexoPublish">
              <RegistryValue Type="string" Value="发布到 Hexo 博客" />
              <RegistryValue Name="Icon" Type="string" Value="&quot;[INSTALLFOLDER]EasyHexo.exe&quot;" />
              <RegistryValue Name="MultiSelectModel" Type="string" Value="Single" />
              <RegistryKey Key="command">
                <RegistryValue Type="string" Value="&quot;[INSTALLFOLDER]EasyHexo.exe&quot; --publish &quot;%1&quot;" />
              </RegistryKey>
            </RegistryKey>
          </Component>
        </ComponentGroup>
'@
$refs = ($script:componentIds | ForEach-Object { "      <ComponentRef Id=`"$_`" />" }) -join [Environment]::NewLine
Add-Line ($packageFooter.Replace("{COMPONENT_REFS}", $refs).Replace("{REGISTRY_GROUP}", $registryGroup)) 0

$wxsPath = Join-Path $env:TEMP "EasyHexo-$Version.wxs"
[System.IO.File]::WriteAllText($wxsPath, ($script:lines -join [Environment]::NewLine), (New-Object System.Text.UTF8Encoding($false)))
Write-Host "  WXS: $wxsPath" -ForegroundColor DarkGray

Remove-Item $OutputPath -Force -ErrorAction SilentlyContinue
& wix build $wxsPath -o $OutputPath -arch x64 -ext WixToolset.UI.wixext 2>&1 | ForEach-Object { Write-Host $_ }
if (-not (Test-Path $OutputPath)) {
    throw "MSI 生成失败。"
}

$msi = Get-Item $OutputPath
$hash = (Get-FileHash $OutputPath -Algorithm SHA256).Hash
Write-Host ("  MSI: " + $msi.FullName) -ForegroundColor Green
Write-Host ("  大小: " + [math]::Round($msi.Length / 1MB, 1) + " MB") -ForegroundColor Green
Write-Host ("  SHA256: " + $hash.ToLower()) -ForegroundColor Green

[pscustomobject]@{
    Version  = $Version
    MsiPath  = $msi.FullName
    MsiName  = $msi.Name
    SizeMB   = [math]::Round($msi.Length / 1MB, 1)
    Sha256   = $hash.ToLower()
}
