#requires -Version 7.0
<#
.SYNOPSIS
    生成全中文 Windows 安装包（MSI，WiX v7），支持覆盖升级旧版本。

.EXAMPLE
    pwsh tools\build-installer.ps1 -SourceDir releases\EasyHexo-1.5.0-release-win-x64 -Version 1.5.0
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

    foreach ($file in (Get-ChildItem $fullPath -File | Sort-Object Name)) {
        $rel = if ($relativePath) { "$relativePath/" + $file.Name } else { $file.Name }
        $compId = New-Id 'c_' $rel
        $fileId = New-Id 'f_' $rel
        $script:componentIds.Add($compId)
        Add-Line ("<Component Id=`"$compId`" Guid=`"*`">") $contentIndent
        Add-Line ("<File Id=`"$fileId`" Source=`"" + (ConvertTo-Xml $file.FullName) + "`" KeyPath=`"yes`" />") ($contentIndent + 2)
        Add-Line "</Component>" $contentIndent
    }

    foreach ($dir in (Get-ChildItem $fullPath -Directory | Sort-Object Name)) {
        $childRel = if ($relativePath) { "$relativePath/" + $dir.Name } else { $dir.Name }
        Write-Directory $dir.FullName $childRel $contentIndent
    }

    if (-not $isRoot) {
        Add-Line "</Directory>" $indent
    }
}

$header = @"
<Wix xmlns="http://wixtoolset.org/schemas/v4/wxs">
  <Package Name="Hexo 配置易（Easy Hexo）" Manufacturer="monishguy" Version="$Version"
           UpgradeCode="{B7D3E58C-4F2A-4C7E-9F3B-2D6A1E8C5F90}" Scope="perUser"
           Language="1033" Codepage="936" InstallerVersion="500" Compressed="yes">

    <MajorUpgrade AllowSameVersionUpgrades="yes" Schedule="afterInstallInitialize"
                  DowngradeErrorMessage="已经安装了更新版本的 Hexo 配置易，请先卸载后再安装。" />
    <MediaTemplate EmbedCab="yes" />
    <Icon Id="AppIcon" SourceFile="$(ConvertTo-Xml $IconPath)" />
    <Property Id="ARPPRODUCTICON" Value="AppIcon" />
    <Property Id="ARPHELPLINK" Value="https://github.com/monishguy/easy-hexo-by-monish" />
    <Property Id="ARPCOMMENTS" Value="Hexo 配置易（Easy Hexo）" />
    <Property Id="DESKTOP_SHORTCUT" Value="1" />
    <Property Id="STARTMENU_SHORTCUT" Value="1" />
    <Property Id="ACCEPT_LICENSE" Value="0" />

    <StandardDirectory Id="LocalAppDataFolder">
      <Directory Id="ProgramsFolder" Name="Programs">
        <Directory Id="INSTALLFOLDER" Name="EasyHexo">
"@

$footer = @"
        </Directory>
      </Directory>
    </StandardDirectory>
    <StandardDirectory Id="DesktopFolder" />
    <StandardDirectory Id="ProgramMenuFolder" />

    {REGISTRY_GROUP}

    <DirectoryRef Id="DesktopFolder">
      <Component Id="DesktopShortcutComponent" Guid="{D4A1B2C3-E5F6-4A7B-8C9D-0E1F2A3B4C5D}" Condition="DESKTOP_SHORTCUT=1">
        <Shortcut Id="DesktopShortcut" Directory="DesktopFolder" Name="Hexo 配置易" Target="[INSTALLFOLDER]EasyHexo.exe" WorkingDirectory="INSTALLFOLDER" Icon="AppIcon" Advertise="no" />
      </Component>
    </DirectoryRef>
    <DirectoryRef Id="ProgramMenuFolder">
      <Component Id="StartMenuShortcutComponent" Guid="{E5B2C3D4-F6A7-4B8C-9D0E-1F2A3B4C5D6E}" Condition="STARTMENU_SHORTCUT=1">
        <Shortcut Id="StartMenuShortcut" Directory="ProgramMenuFolder" Name="Hexo 配置易" Target="[INSTALLFOLDER]EasyHexo.exe" WorkingDirectory="INSTALLFOLDER" Icon="AppIcon" Advertise="no" />
      </Component>
    </DirectoryRef>

    <UI>
      <Dialog Id="WelcomeDlg" Width="370" Height="270" Title="Hexo 配置易 安装向导" NoMinimize="yes">
        <Control Id="Title" Type="Text" X="15" Y="10" Width="300" Height="20" Transparent="yes" NoPrefix="yes" Text="欢迎使用 Hexo 配置易" />
        <Control Id="Text" Type="Text" X="25" Y="70" Width="320" Height="80" Transparent="yes" NoPrefix="yes" Text="本向导将引导您完成 Hexo 配置易的安装。&#xD;&#xA;点击“下一步”继续，点击“取消”退出安装。" />
        <Control Id="Next" Type="PushButton" X="236" Y="243" Width="56" Height="17" Default="yes" Text="下一步">
          <Publish Event="NewDialog" Value="LicenseDlg" />
        </Control>
        <Control Id="Cancel" Type="PushButton" X="304" Y="243" Width="56" Height="17" Cancel="yes" Text="取消">
          <Publish Event="EndDialog" Value="Exit" />
        </Control>
      </Dialog>

      <Dialog Id="LicenseDlg" Width="370" Height="270" Title="Hexo 配置易 安装向导" NoMinimize="yes">
        <Control Id="Title" Type="Text" X="15" Y="10" Width="300" Height="20" Transparent="yes" NoPrefix="yes" Text="许可与软件说明" />
        <Control Id="Line1" Type="Text" X="18" Y="40" Width="334" Height="16" Transparent="yes" NoPrefix="yes" Text="本软件采用 MIT 许可证（MIT License）。" />
        <Control Id="Line2" Type="Text" X="18" Y="62" Width="334" Height="30" Transparent="yes" NoPrefix="yes" Text="Hexo 配置易是一款用于维护 Hexo 站点的桌面小工具。" />
        <Control Id="Line3" Type="Text" X="18" Y="94" Width="334" Height="46" Transparent="yes" NoPrefix="yes" Text="主要功能：可视化编辑站点与主题配置、文章分类与标签管理、内置文本编辑器、运行 Hexo 命令、发布文章。" />
        <Control Id="Line4" Type="Text" X="18" Y="142" Width="334" Height="30" Transparent="yes" NoPrefix="yes" Text="安装会把程序放入当前用户目录，并创建开始菜单与桌面快捷方式（可在下一步取消勾选）。" />
        <Control Id="Accept" Type="CheckBox" X="18" Y="184" Width="300" Height="17" Property="ACCEPT_LICENSE" CheckBoxValue="1" Text="我已阅读并同意以上说明" />
        <Control Id="Back" Type="PushButton" X="168" Y="243" Width="56" Height="17" Text="上一步">
          <Publish Event="NewDialog" Value="WelcomeDlg" />
        </Control>
        <Control Id="Next" Type="PushButton" X="236" Y="243" Width="56" Height="17" Default="yes" Text="下一步">
          <Publish Event="NewDialog" Value="OptionsDlg" />
        </Control>
        <Control Id="Cancel" Type="PushButton" X="304" Y="243" Width="56" Height="17" Cancel="yes" Text="取消">
          <Publish Event="EndDialog" Value="Exit" />
        </Control>
      </Dialog>

      <Dialog Id="OptionsDlg" Width="370" Height="270" Title="Hexo 配置易 安装向导" NoMinimize="yes">
        <Control Id="Title" Type="Text" X="15" Y="10" Width="300" Height="20" Transparent="yes" NoPrefix="yes" Text="选择额外选项" />
        <Control Id="Text" Type="Text" X="25" Y="50" Width="320" Height="40" Transparent="yes" NoPrefix="yes" Text="请选择安装完成后要创建的快捷方式：" />
        <Control Id="Desktop" Type="CheckBox" X="35" Y="100" Width="300" Height="17" Property="DESKTOP_SHORTCUT" CheckBoxValue="1" Text="创建桌面快捷方式" />
        <Control Id="StartMenu" Type="CheckBox" X="35" Y="126" Width="300" Height="17" Property="STARTMENU_SHORTCUT" CheckBoxValue="1" Text="创建开始菜单快捷方式" />
        <Control Id="Back" Type="PushButton" X="168" Y="243" Width="56" Height="17" Text="上一步">
          <Publish Event="NewDialog" Value="LicenseDlg" />
        </Control>
        <Control Id="Next" Type="PushButton" X="236" Y="243" Width="56" Height="17" Default="yes" Text="下一步">
          <Publish Event="NewDialog" Value="VerifyDlg" />
        </Control>
        <Control Id="Cancel" Type="PushButton" X="304" Y="243" Width="56" Height="17" Cancel="yes" Text="取消">
          <Publish Event="EndDialog" Value="Exit" />
        </Control>
      </Dialog>

      <Dialog Id="VerifyDlg" Width="370" Height="270" Title="Hexo 配置易 安装向导" NoMinimize="yes">
        <Control Id="Title" Type="Text" X="15" Y="10" Width="300" Height="20" Transparent="yes" NoPrefix="yes" Text="准备安装" />
        <Control Id="Text" Type="Text" X="25" Y="70" Width="320" Height="60" Transparent="yes" NoPrefix="yes" Text="点击“安装”开始复制文件。&#xD;&#xA;安装过程需要几秒钟，请稍候。" />
        <Control Id="Back" Type="PushButton" X="168" Y="243" Width="56" Height="17" Text="上一步">
          <Publish Event="NewDialog" Value="OptionsDlg" />
        </Control>
        <Control Id="Install" Type="PushButton" X="236" Y="243" Width="56" Height="17" Default="yes" Text="安装">
          <Publish Event="EndDialog" Value="Return" />
        </Control>
        <Control Id="Cancel" Type="PushButton" X="304" Y="243" Width="56" Height="17" Cancel="yes" Text="取消">
          <Publish Event="EndDialog" Value="Exit" />
        </Control>
      </Dialog>

      <Dialog Id="FinishDlg" Width="370" Height="270" Title="Hexo 配置易 安装向导" NoMinimize="yes">
        <Control Id="Title" Type="Text" X="15" Y="10" Width="300" Height="20" Transparent="yes" NoPrefix="yes" Text="安装完成" />
        <Control Id="Text" Type="Text" X="25" Y="70" Width="320" Height="60" Transparent="yes" NoPrefix="yes" Text="Hexo 配置易已安装完成。&#xD;&#xA;点击“完成”关闭安装向导。" />
        <Control Id="Finish" Type="PushButton" X="236" Y="243" Width="56" Height="17" Default="yes" Text="完成">
          <Publish Event="EndDialog" Value="Return" />
        </Control>
        <Control Id="Cancel" Type="PushButton" X="304" Y="243" Width="56" Height="17" Cancel="yes" Text="取消">
          <Publish Event="EndDialog" Value="Exit" />
        </Control>
      </Dialog>
    </UI>

    <InstallUISequence>
      <Show Dialog="WelcomeDlg" Before="ExecuteAction" Condition="NOT Installed" />
      <Show Dialog="FinishDlg" After="ExecuteAction" Condition="NOT Installed" />
    </InstallUISequence>

    <Feature Id="MainFeature" Title="Hexo 配置易" Level="1">
      {COMPONENT_REFS}
      <ComponentGroupRef Id="RegistryGroup" />
      <ComponentRef Id="DesktopShortcutComponent" />
      <ComponentRef Id="StartMenuShortcutComponent" />
    </Feature>
  </Package>
</Wix>
"@

$registryGroup = @'
    <ComponentGroup Id="RegistryGroup">
      <Component Id="RegistryEntries" Guid="{4C6B8F1A-2D3E-4A5B-9C7D-8E1F2A3B4C5D}">
        <RegistryKey Root="HKCU" Key="Software\Microsoft\Windows\CurrentVersion\App Paths\EasyHexo.exe">
          <RegistryValue Type="string" Value="[INSTALLFOLDER]EasyHexo.exe" KeyPath="yes" />
        </RegistryKey>
        <RegistryKey Root="HKCU" Key="Software\Classes\SystemFileAssociations\.md\shell\EasyHexoPublish">
          <RegistryValue Type="string" Value="发布到 Hexo 博客" />
          <RegistryValue Name="Icon" Type="string" Value="&quot;[INSTALLFOLDER]EasyHexo.exe&quot;" />
          <RegistryValue Name="MultiSelectModel" Type="string" Value="Player" />
          <RegistryKey Key="command">
            <RegistryValue Type="string" Value="&quot;[INSTALLFOLDER]EasyHexo.exe&quot; --publish &quot;%1&quot;" />
          </RegistryKey>
        </RegistryKey>
        <RegistryKey Root="HKCU" Key="Software\Classes\SystemFileAssociations\.txt\shell\EasyHexoPublish">
          <RegistryValue Type="string" Value="发布到 Hexo 博客" />
          <RegistryValue Name="Icon" Type="string" Value="&quot;[INSTALLFOLDER]EasyHexo.exe&quot;" />
          <RegistryValue Name="MultiSelectModel" Type="string" Value="Player" />
          <RegistryKey Key="command">
            <RegistryValue Type="string" Value="&quot;[INSTALLFOLDER]EasyHexo.exe&quot; --publish &quot;%1&quot;" />
          </RegistryKey>
        </RegistryKey>
      </Component>
    </ComponentGroup>
'@

$script:lines.Clear()
Add-Line $header 0
Write-Directory $SourceDir '' 10
$refs = ($script:componentIds | ForEach-Object { "      <ComponentRef Id=`"$_`" />" }) -join [Environment]::NewLine
$footerText = $footer.Replace('{COMPONENT_REFS}', $refs).Replace('{REGISTRY_GROUP}', $registryGroup)
Add-Line $footerText 0

$wxsPath = Join-Path $env:TEMP "EasyHexo-$Version.wxs"
[System.IO.File]::WriteAllText($wxsPath, ($script:lines -join [Environment]::NewLine), (New-Object System.Text.UTF8Encoding($false)))
Write-Host "  WXS: $wxsPath" -ForegroundColor DarkGray

Remove-Item $OutputPath -Force -ErrorAction SilentlyContinue
& wix build $wxsPath -o $OutputPath -arch x64 2>&1 | ForEach-Object { Write-Host $_ }
if (-not (Test-Path $OutputPath)) {
    throw "MSI 生成失败。"
}

$msi = Get-Item $OutputPath
$hash = (Get-FileHash $OutputPath -Algorithm SHA256).Hash
Write-Host ("  MSI: " + $msi.FullName) -ForegroundColor Green
Write-Host ("  大小: " + [math]::Round($msi.Length / 1MB, 1) + " MB") -ForegroundColor Green
Write-Host ("  SHA256: " + $hash.ToLower()) -ForegroundColor Green

[pscustomobject]@{
    Version = $Version
    MsiPath = $msi.FullName
    MsiName = $msi.Name
    SizeMB  = [math]::Round($msi.Length / 1MB, 1)
    Sha256  = $hash.ToLower()
}
