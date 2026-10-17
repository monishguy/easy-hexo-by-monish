# Hexo 配置易（Easy Hexo）

面向 **Windows 10 / 11** 的 WinUI 3 桌面应用，用来可视化维护 Hexo 站点的 `_config.yml`，并以表单方式发布 / 重发 Markdown 文章、自动整理文章里的本机图片与附件。

> 仓库地址：<https://github.com/monishguy/easy-hexo-by-monish>
> 许可证：MIT（见仓库根目录 [LICENSE](LICENSE)）　|　技术栈：**C# + WinUI 3（Windows App SDK 1.7）+ .NET 8**

![配置编辑](docs/screenshots/config-editor.png)

| 发布文章 | 设置 |
| --- | --- |
| ![发布文章](docs/screenshots/publish.png) | ![设置](docs/screenshots/settings.png) |

---

## 1. 语言选型答疑（为什么是 C# + WinUI 3）

**结论：C# + WinUI 3（Windows App SDK）+ .NET 8，用 Visual Studio 2022 编译。**

| 方案 | 是否满足需求 | 评价 |
| --- | --- | --- |
| **C# + WinUI 3（本项目）** | ✅ | WinUI 3 官方一等语言，项目模板、XAML 热重载、调试、NuGet 生态最完整；本项目的 YAML 解析、文件复制、正则改写等逻辑用 .NET 类库最省事；产物是原生 Win32 桌面进程（`WinExe`），只是运行时用 .NET。 |
| C++/WinRT + WinUI 3 | ✅ | 同样能做，但 UI 与业务都要写 C++/COM，开发与后期维护成本高；本项目没有性能敏感点，不划算。 |
| WPF / WinForms | ❌ 外观不符 | 只能做出 WinUI 2 之前的观感，不满足“UI 为 WinUI 3 标准”。 |
| WinUI 2（UWP） | ❌ 部署受限 | 必须打包为 MSIX/UWP，无法像普通 Win32 程序一样直接 `F5` 运行 exe。 |
| Electron / Tauri 等 | ❌ 非 WinUI | 不是 WinUI 3 界面，部署体积也大。 |

> 注意：C# 只是“编译期语言”，最终产物 `EasyHexo.exe` 仍是普通 Win32 桌面程序，不依赖浏览器或 WebView，界面由 WinUI 3（`Microsoft.ui.xaml.dll`）渲染。

## 2. 前置条件与环境

### 运行环境
- Windows 10 1809（17763）及以上，或 Windows 11（推荐 Win11，可获得完整的 Fluent 风格与 Mica 相关能力）
- 无需安装 Node.js / Hexo —— 本工具只读写 Hexo 目录里的文件，不执行 `hexo` 命令

### 开发环境（编译必需）
1. **Visual Studio 2022**（17.8 及以上；Windows App SDK 1.7 建议 17.12+）
2. VS Installer 中勾选工作负载：
   - **.NET 桌面开发**
   - **Windows 应用开发**（Windows application development，含 WinUI / Windows App SDK 的 C# 模板与工具）
3. 单个组件里确认有：
   - **.NET 8 SDK**（或运行时）
   - **Windows 10 SDK 10.0.19041.0 或更高**（推荐 10.0.22621 / 10.0.26100）
4. 首次打开解决方案需要联网还原 NuGet 包：
   - `Microsoft.WindowsAppSDK` **1.7.260224002**
   - `Microsoft.Windows.SDK.BuildTools` **10.0.22621.756**

> 项目已启用 `WindowsAppSDKSelfContained=true`，会把 Windows App SDK 运行时复制到输出目录，**不需要**再单独安装 “Windows App Runtime”。
> 如果发布后在没装 .NET 的机器上运行，再加 `<SelfContained>true</SelfContained>` 或让对方安装 .NET 8 Desktop Runtime。

## 3. 编译与运行

### Visual Studio（推荐）
1. 打开 `EasyHexo.sln`
2. 右上角平台选 **x64**（或 x86 / ARM64），配置选 **Debug**
3. 直接 `F5` 运行；`Ctrl+Shift+B` 编译
4. 首次启动会提示选择 **Hexo 根目录**（目录下必须有 `_config.yml` 和 `source` 文件夹）

### 命令行（可选）
WinUI 3 的 PRI / 打包任务由 **Visual Studio 的 MSBuild** 提供，因此请使用 *Developer PowerShell for VS 2022* 里的 `msbuild`，而不是 .NET SDK 自带的 `dotnet build`（后者会报 `MSB4062: Microsoft.Build.AppxPackage.dll 找不到`，这是 WinUI 3 的已知限制）：

```powershell
msbuild EasyHexo.sln /p:Platform=x64 /p:Configuration=Debug
msbuild EasyHexo.sln /p:Platform=x64 /p:Configuration=Release
```

发布（输出可直接拷走的 exe）：

```powershell
msbuild EasyHexo\EasyHexo.csproj /p:Configuration=Release /p:Platform=x64 `
        /p:WindowsPackageType=None /p:RuntimeIdentifier=win-x64
# 产物：EasyHexo\bin\x64\Release\net8.0-windows10.0.19041.0\win-x64\EasyHexo.exe
```

**没有安装 Visual Studio 时**，可以用仓库自带脚本构建可直接运行的 zip（内部会手工合并 `resources.pri`）：

```powershell
pwsh tools\build-unpackaged.ps1                        # Debug / x64 → releases\EasyHexo-<版本>-debug-win-x64.zip
pwsh tools\build-unpackaged.ps1 -Configuration Release  # Release / x64
pwsh tools\build-unpackaged.ps1 -FrameworkDependent     # 不打包 .NET 运行时（体积更小）
```

默认把 **.NET 8 运行时 + Windows App SDK 运行时** 一起打进包里，解压即可运行；`releases\` 已在 `.gitignore` 中，不会进入版本库。

### 本仓库的验证情况
编写过程中在没有安装 Visual Studio 的环境里做了两轮验证：

1. **编译验证**：用 .NET SDK 编译，XAML（`.xbf`）与 C# 全部通过，0 error（仅最后打包步骤因缺少 VS 的 Appx 任务而中止，与代码无关）。
2. **运行验证**：手工合并 `resources.pri` 后真实启动并通过 UI Automation 跑通端到端流程：
   - 配置编辑 → 修改 `title` → 应用到配置：**只有该行变化**，生成 `_config.yml.easyhexo.bak`；
   - 修改带行尾注释的项：`trailing_index: false # 注释` 注释保留；
   - 修改含 `:` 和 `#` 的值：自动写为 `'含冒号: 与井号 # 的值'`；
   - 清空带引号的值：写回 `timezone: ''`（引号风格保留）；
   - 发布已发布文章：本地图片复制到 `source\你好 Hexo\photo.png`，链接改写为 `/你好%20Hexo/photo.png`，`date`/`tags`/`categories` 保留；
   - 发布没有 front-matter 的 `.md`：自动在文件开头补 front matter，日期取文件修改时间，附件复制到 `source\no-front-matter\doc.zip`，网页链接原样跳过；
   - 中英文切换立即生效，窗口尺寸 / 自绘标题栏正常；
   - 简体中文键名翻译：配置列表显示「站点标题 / 去掉链接中的 index.html」等中文名，灰色小字显示原英文键名；应用到配置后文件里仍是 `title: …`、`pretty_urls:`、`trailing_index:` 等英文键。

未在本机执行：VS 一键 `F5` 与 MSIX 打包（环境无 VS），但工程结构、包版本、构建配置均为官方标准模板写法。

## 4. 目录结构

```
EasyHexo.sln
EasyHexo/
  EasyHexo.csproj             # net8.0-windows10.0.19041.0 / WinUI 3 / unpackaged
  app.manifest                # PerMonitorV2 DPI、长路径、Win10/11 兼容声明
  App.xaml(.cs)               # 应用入口：读设置、套用语言、创建主窗口
  MainWindow.xaml(.cs)        # NavigationView 三栏 + 自绘标题栏
  Views/
    ConfigEditorPage.xaml(.cs)   # 配置编辑
    PublishPage.xaml(.cs)        # 发布文章
    SettingsPage.xaml(.cs)       # 设置
  Models/
    ConfigEntry 相关          -> Services/ConfigYamlService.cs
    ConfigRow.cs               # 配置列表行的 ViewModel
    ArticleInfo.cs             # 已发布文章
    FrontMatter.cs             # front-matter 数据
    TagItem.cs                 # 标签块
  Services/
    AppPaths.cs                # 设置/日志路径 + AppSettings + SettingsService + AppLog
    AppState.cs                # 全局状态（根目录、语言、设置持久化）
    HexoProject.cs             # 根目录校验、路径/文件名工具
    ConfigYamlService.cs       # ★ _config.yml 行级解析 + 只改改动行的写回
    FrontMatterService.cs      # ★ front-matter 识别 / 解析 / 生成
    ArticleService.cs          # 扫描 source/_posts（按修改时间倒序）
    AssetService.cs            # ★ 本机绝对路径图片/附件复制与链接改写
    ArticlePublisher.cs        # ★ 发布流程：资源处理 → front-matter → 写 _posts
    TempFileService.cs         # .txt → 临时目录 .md
    TextFileService.cs         # 保留 BOM / 换行的文本读写
  Localization/                # 运行时中英文（无需重启，立即切换）
    Lang.cs / StringsZhCn.cs / StringsEnUs.cs / Trans.cs
    ConfigKeyTranslator.cs     # 配置项英文键名 → 中文显示名（仅显示，不改文件）
  Helpers/WindowHelper.cs      # HWND、文件夹/文件选择器、对话框、打开目录
docs/screenshots/              # README 截图
```

## 5. 功能与实现对照

| 需求 | 实现 |
| --- | --- |
| 选择 Hexo 根目录，校验 `_config.yml` + `source` | `HexoProject.TryCreate` + 首启/空状态/设置页三处入口 |
| 依据缩进把 `_config.yml` 转为多层可展开列表 | `ConfigYamlService.Parse` → `ConfigRow` + `ListView`（展开/收起重建可见行） |
| 简体中文下把固定配置项英文键名翻译成中文（仅界面显示） | `Localization/ConfigKeyTranslator.cs`：按“完整键路径”匹配，中文名为主标签、英文原名作为灰色提示；**写回文件时始终用英文键名** |
| 可编辑项带输入框、自动填入原值 | DataTemplate 中 `TextBox`，`EditingValue` 保存在 `ConfigEntry` |
| 注释转换为不可改的描述 | 上方整行注释 + 行尾注释都进入 `ConfigEntry.Comment`，只读显示 |
| 顶部“应用到配置”写回文件 | `ConfigYamlService.Apply`：只重建被修改的行，写前备份 |
| 定位 `source/_posts`，按修改日期新→旧 | `ArticleService.LoadPosts`（`OrderByDescending(Modified)`） |
| 浏览 / 拖入一个或多个 `.md` `.txt` | `AllowDrop` + `FileOpenPicker`；多文件进入 `Queue` 逐篇发布 |
| `.txt` 复制到临时目录并改后缀 `.md` | `TempFileService.ImportAsMarkdownAsync` |
| 识别开头 `---` … `---` 中的 title/date/tags | `FrontMatterService.Parse`（支持行内数组与块序列占位） |
| 标题/日期必填、日期用日期选择器 | `DatePicker` + `TimePicker`，写回固定 `yyyy-MM-dd HH:mm:ss` |
| 标签输入后 Enter 锁定，可多个 | `TagInput.KeyDown` → `ObservableCollection<TagItem>` 标签块 |
| 无 front-matter 时先填值、发布时写入文件开头 | `FrontMatterService.Compose` 生成后写回 |
| 本地绝对路径图片/附件复制到 `source\标题` 并改写链接 | `AssetService.Process`（`http(s)`、相对路径跳过） |
| 已发布文章可重新编辑发布 | 左侧列表选择 → 载入表单 → 写回原路径 |
| 设置：语言 / 关于 / 根目录切换 | `SettingsPage` + `AppState` + `%LOCALAPPDATA%\EasyHexo\settings.json` |

## 6. `_config.yml` 写回规则

> **键名显示与写回是分离的**：简体中文界面下，`title`、`pretty_urls.trailing_index` 等固定配置项会显示为「站点标题」「去掉链接中的 index.html」等中文名（英文原名以灰色小字 / 鼠标悬停提示展示）；写回文件时仍然使用**原始英文键名**，不会把中文键写入 `_config.yml`。未收录的键（自定义项、主题私有项）直接显示英文原名。

- **逐行处理**，不做完整 YAML 重新序列化，因此顺序、空行、注释、被注释掉的选项都原样保留；
- 只重写被修改的那一行：`缩进 + key: ` + 新值 + 原行尾注释；
- 值格式：
  - 原来是单/双引号 → 保留原引号类型（单引号内的 `'` 转义为 `''`）；
  - `true` / `false` / 数字 → 不加引号；
  - 含 `:`、`#`、以特殊符号开头、首尾有空格等 → 自动加单引号；
  - 行内集合 `[a, b]` / `{a: b}` 原样写回；
  - 清空带引号的值 → 写回 `''`；
- 多行块标量（`key: |` / `key: >`）无法安全单行替换 → 标记为**只读**；
- 写文件前自动备份为 **`_config.yml.easyhexo.bak`**（每次应用时覆盖上一次备份）；
- 保留原文件的 UTF-8 BOM 状态与换行符（CRLF / LF）。

## 7. 发布规则

1. 文章内容来自所选 `.md` 文件；`.txt` 会先复制到临时目录再改名成 `.md`；
2. 正文里形如 `![alt](E:\images\a.png)`、`[text](D:\files\a.zip)` 的**本机绝对路径**：
   - 复制到 `Hexo根目录\source\<标题>\`；
   - 链接改写为 `/<标题>/<文件名>`（空格等特殊字符做 URL 转义）；
   - `http(s)://`、`//`、相对路径、`/站点路径` 会被跳过；
   - 同名不同源文件自动加 `-2`、`-3` 后缀，避免互相覆盖；
3. 生成 front-matter：`title` / `date` / `tags`，原文件里其它字段（如 `categories`、`updated`）原样保留；
4. 写入 `Hexo根目录\source\_posts\<标题>.md`（标题中的 Windows 非法字符替换为 `_`）；
5. 如果编辑的是**已发布文章**，写回原文件路径；如果编辑的是**外部 `.md` 文件**，还会把 front-matter 写回该文件开头（符合需求中“填入 md 文件开头首字处”）；
6. 目标已存在且不是同一文件时，会先弹窗确认覆盖；
7. 多个文件会排队，每发布成功一篇自动载入下一篇。

## 8. 本机数据位置

| 内容 | 位置 |
| --- | --- |
| 设置（根目录、语言） | `%LOCALAPPDATA%\EasyHexo\settings.json` |
| 错误日志 | `%LOCALAPPDATA%\EasyHexo\logs\easyhexo.log` |
| 拖入 `.txt` 的临时 md | `%TEMP%\EasyHexo\<时间戳>\` |
| `_config.yml` 备份 | `<Hexo 根目录>\_config.yml.easyhexo.bak` |

## 9. 已知限制 / 可扩展点

- `_config.yml` 是“按缩进的行级解析”，不是完整 YAML 1.2 实现：锚点/别名、流式嵌套映射、复杂多行结构不支持（这些行会保持原样或只读）；键必须是 `key: value` 且冒号后带空格。
- 目前只能**修改**已有配置项的值，不支持新增 / 删除配置项（Hexo 的配置项基本固定，够用；如需要可在 `ConfigYamlService` 上扩展）。
- 中文键名翻译表覆盖 Hexo 默认 `_config.yml` 的固定项；用户自定义键、主题私有键没有译名时显示英文原名（不影响使用）。
- 文件中用作“分节标题”的注释（例如 `# Site`）会显示为紧随其后的第一个配置项的描述——这是忠实于文件顺序的展示方式。
- front-matter 只映射 `title` / `date` / `tags`（`tags:` 的块序列写法也能识别），其它键原样保留；日期秒精度受界面时间选择器限制（没有单独秒输入时按 `00` 秒写入）。
- 资源链接只处理 Markdown 的 `![alt](url)` / `[text](url)`；HTML 的 `<img src>` 不处理；路径中带右括号 `)` 的链接不解析。
- 主题配置（`_config.landscape.yml` 等）、文章正文富文本编辑、单实例限制未实现，均可按现有服务层继续扩展。

## 10. 常见问题

**Q：双击 `EasyHexo.exe` 报缺少 `Microsoft.WindowsAppRuntime.dll`？**
A：确认是用 VS/MSBuild 生成的完整输出目录；本项目 `WindowsAppSDKSelfContained=true`，正常会把运行时复制到 exe 旁边。若自行改了配置，请安装 [Windows App Runtime 1.7](https://learn.microsoft.com/windows/apps/windows-app-sdk/downloads) 或保持自包含。

**Q：`dotnet build` 报 `MSB4062 ... Microsoft.Build.AppxPackage.dll`？**
A：这是 WinUI 3 的 PRI/打包任务只随 Visual Studio 提供的已知限制，请用 VS 或 “Developer PowerShell for VS” 里的 `msbuild`（见第 3 节）。

**Q：拖入文件没反应？**
A：本程序以普通权限运行；如果以管理员身份运行，Windows 会禁止从资源管理器拖放（系统限制），请用“浏览…”按钮。
