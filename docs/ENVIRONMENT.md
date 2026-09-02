# 环境安装、构建与发布

本文说明 Comet 的开发环境、源码运行、便携发布和常见构建问题。产品功能见 [使用指南](USER_GUIDE.md)，验证范围见 [测试指南](TESTING.md)。

## 目录

- [项目基线](#项目基线)
- [准备开发环境](#准备开发环境)
- [验证环境](#验证环境)
- [构建与运行](#构建与运行)
- [发布便携版](#发布便携版)
- [清理中间文件](#清理中间文件)
- [排查常见问题](#排查常见问题)

## 项目基线

以下值来自 `src/Comet/Comet.csproj`、`tests/Comet.Tests/Comet.Tests.csproj` 和发布配置：

| 项目                      | 当前值                                           |
| ----------------------- | --------------------------------------------- |
| 应用目标框架                  | `net10.0-windows10.0.26100.0`                 |
| 最低系统版本                  | Windows 10 1809 / build 17763                 |
| Windows App SDK         | 2.4.0，Self-contained                          |
| Windows SDK Build Tools | 10.0.26100.7705                               |
| 串口库                     | `System.IO.Ports` 10.0.11                     |
| 测试                      | MSTest SDK 4.1.0，Microsoft Testing Platform   |
| 目标架构                    | x86、x64、ARM64                                 |
| 包类型                     | `WindowsPackageType=None`，Unpackaged          |
| .NET 发布                 | Self-contained、非单文件、未裁剪；Release 启用 ReadyToRun |

## 准备开发环境

### 必需软件

开发和调试需要以下环境：

1. Windows 10 1809 或更高版本，推荐 Windows 11。
2. [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)。
3. Windows 11 SDK 10.0.26100 或更高版本。
4. 对应串口设备的 Windows 驱动。

项目依赖通过 NuGet 还原，不要求安装额外的 `dotnet workload`。

### Visual Studio

推荐使用 Visual Studio 2026 18.x，或其他明确支持 .NET 10、WinUI 3 和 Windows App SDK C# 项目的版本。

在 Visual Studio Installer 中确认安装以下能力：

- C# 与 .NET 桌面开发工具。
- Windows 应用和通用 Windows 平台开发工具。
- 使用 C++ 的桌面开发工具。
- Windows 11 SDK 10.0.26100。

工作负载名称会随 Visual Studio 版本和界面语言变化，请以安装器右侧“安装详细信息”中的组件版本为准。

### 仅使用命令行

命令行构建至少需要以下条件：

- .NET 10 SDK。
- Windows SDK 10.0.26100。
- NuGet 网络访问或已经准备好的本地包缓存。

首次还原会下载 Windows App SDK、Windows SDK Build Tools 和 `System.IO.Ports` 包。

## 验证环境

先检查已安装的 .NET SDK 和当前运行环境：

```powershell
dotnet --info
dotnet --list-sdks
```

输出中应包含 `10.0.x` SDK，并且操作系统 RID 应与目标架构匹配。然后在仓库根目录还原解决方案：

```powershell
dotnet restore .\Comet.sln
```

仓库根目录的 `global.json` 只指定测试使用 Microsoft Testing Platform，不锁定 .NET SDK 补丁版本。构建会使用机器上可用的最高兼容 .NET 10 SDK。

## 构建与运行

### 使用命令行

**构建并运行 Debug x64：**

```powershell
dotnet build .\src\Comet\Comet.csproj -c Debug -p:Platform=x64
dotnet run --project .\src\Comet\Comet.csproj -c Debug -p:Platform=x64
```

**构建 Release x64：**

```powershell
dotnet build .\src\Comet\Comet.csproj -c Release -p:Platform=x64
```

**运行核心回归测试：**

```powershell
dotnet test .\tests\Comet.Tests\Comet.Tests.csproj -c Release
```

应用的典型输出目录如下：

```text
src\Comet\bin\x64\Debug\net10.0-windows10.0.26100.0\win-x64\
src\Comet\bin\x64\Release\net10.0-windows10.0.26100.0\win-x64\
```

### 使用 Visual Studio

1. 打开 `Comet.sln`。
2. 将 `Comet` 设为启动项目。
3. 选择 `Debug | x64` 或 `Release | x64`。
4. 启动调试，或选择“不调试启动”。

`Comet` 是启动项目，`Comet.Core` 是纯 C# 核心类库。应用是 Unpackaged WinUI 程序，不需要部署或注册 MSIX。

解决方案当前直接列出 x86 和 x64；ARM64 已在项目和发布配置中定义，建议通过命令行或对应发布配置生成。

### 调试串口

调试真实设备前，请确认以下事项：

- 关闭可能占用目标 COM 端口的其他串口工具。
- 确认设备管理器中的设备状态正常。
- 连接后串口参数会锁定，断开后才能修改。
- 调试器停止应用时，页面卸载会停止计时器并释放串口。

## 发布便携版

### 选择目标架构

仓库包含以下文件系统发布配置：

| 目标设备           | Platform | Runtime Identifier | 配置文件               |
| -------------- | -------- | ------------------ | ------------------ |
| 32 位 Intel/AMD | x86      | `win-x86`          | `win-x86.pubxml`   |
| 64 位 Intel/AMD | x64      | `win-x64`          | `win-x64.pubxml`   |
| Windows on ARM | ARM64    | `win-arm64`        | `win-arm64.pubxml` |

不同架构的输出不能合并到同一目录。

### 更新版本

打包前先更新 `src/Comet/Comet.csproj` 中的版本元数据，使发布包名称、EXE 产品版本和“关于”面板显示保持一致。

```xml
<Version>0.2.2-rc1</Version>
<AssemblyVersion>0.2.2.0</AssemblyVersion>
<FileVersion>0.2.2.0</FileVersion>
```

`Version` 可包含 `rc1` 等预发布后缀，应用“关于”面板读取该值作为显示版本。

`AssemblyVersion` 和 `FileVersion` 应保持纯数字格式，通常使用同一主版本、次版本和修订号。便携包文件名应与 `Version` 对齐，例如 `Comet-win-x64-v0.2.2-rc1-portable.zip`。

### 生成 x64 便携目录

在仓库根目录执行：

```powershell
dotnet publish .\src\Comet\Comet.csproj `
  -c Release `
  -p:Platform=x64 `
  -r win-x64 `
  --self-contained true `
  -o .\artifacts\publish\win-x64
```

> **重要：** Comet 不是单文件应用。发布时必须完整保留 EXE、DLL、PRI、资源和运行时文件；只复制 `Comet.exe` 无法构成可用发布包。

### 加入文档并压缩

将使用说明加入发布目录，然后生成压缩包：

```powershell
Copy-Item .\README.md .\artifacts\publish\win-x64\
Copy-Item .\docs .\artifacts\publish\win-x64\docs -Recurse

Compress-Archive `
  -Path .\artifacts\publish\win-x64\* `
  -DestinationPath .\artifacts\Comet-win-x64-portable.zip `
  -Force
```

生成压缩包后计算 SHA-256 校验值：

```powershell
Get-FileHash .\artifacts\Comet-win-x64-portable.zip -Algorithm SHA256
```

### 理解发布体积

便携目录同时携带 .NET 运行时、Windows App SDK、WinUI 资源和架构相关本机库，因此体积明显大于业务程序集。发布相关设置为：

- `SelfContained=true`。
- `PublishSingleFile=false`。
- `PublishTrimmed=false`。
- Release 启用 ReadyToRun。

这些设置优先保证 WinUI、WinRT 和 JSON 运行时元数据可靠。未经完整回归测试，不应直接启用裁剪或单文件发布。

## 清理中间文件

普通清理优先使用项目命令：

```powershell
dotnet clean .\src\Comet\Comet.csproj -c Debug -p:Platform=x64
dotnet clean .\src\Comet\Comet.csproj -c Release -p:Platform=x64
```

`bin/`、`obj/`、`.vs/`、`artifacts/` 和发布输出均由 `.gitignore` 排除，不应提交到仓库。

如果 NuGet 缓存文件损坏，请先关闭 Visual Studio 和 Comet，再删除工程内的 `obj` 目录并执行 `dotnet restore`。除非已经确认是全局缓存问题，否则不要删除用户目录中的全局 NuGet 包缓存。

## 排查常见问题

### Visual Studio 要求先部署

项目使用以下 Unpackaged 配置：

```xml
<WindowsPackageType>None</WindowsPackageType>
<EnableMsixTooling>false</EnableMsixTooling>
```

在“生成 → 配置管理器”中关闭该项目的 Deploy，保留 Build，然后重新生成并直接启动应用。

### 出现 DEP0700 或 resources.pri 注册错误

这类错误来自打包部署流程，而 Comet 不需要包注册。关闭 Visual Studio 和 Comet，执行 `dotnet clean`，确认没有启用 Deploy 或旧 MSIX 启动配置，然后重新生成 Unpackaged 项目。

### Git 配置字符错误

如果 `Microsoft.Build.Tasks.Git` 报 Git 配置字符错误，请先检查 `%USERPROFILE%\.gitconfig` 和仓库 `.git\config` 是否为有效 UTF-8 Git 配置。

排查期间可以临时禁止 SourceLink 查询：

```powershell
dotnet build .\src\Comet\Comet.csproj `
  -c Release `
  -p:Platform=x64 `
  -p:EnableSourceControlManagerQueries=false
```

该参数只用于绕过本机 Git 配置读取，不代表项目源码构建失败。

### 目标电脑无法启动

- 确认系统不低于 Windows 10 1809。
- 确认发布架构与目标系统匹配。
- 确认整个发布目录已经完整解压。
- 检查安全软件是否隔离 DLL 或本机运行时文件。

### 串口无法打开

- 确认驱动和设备状态正常。
- 确认端口未被其他程序占用。
- 检查波特率、数据位、停止位、校验和流控制。
- 注意端口列表括号中的设备说明只用于识别，不参与连接。
