# 环境安装、构建与发布

本文说明 Comet 的开发环境、源码运行、便携发布和常见构建问题。功能设计见项目根目录 [README](../README.md)，验证方法见 [测试指南](TESTING.md)。

## 目录

- [项目基线](#项目基线)
- [安装开发环境](#安装开发环境)
- [验证环境](#验证环境)
- [构建与运行](#构建与运行)
- [发布便携版](#发布便携版)
- [清理中间文件](#清理中间文件)
- [常见问题](#常见问题)

## 项目基线

以下值直接来自 `Comet.csproj` 和发布配置：

| 项目 | 当前值 |
| --- | --- |
| 目标框架 | `net10.0-windows10.0.26100.0` |
| 最低系统版本 | Windows 10 1809 / build 17763 |
| Windows App SDK | 2.4.0 |
| Windows SDK Build Tools | 10.0.26100.7705 |
| 串口库 | `System.IO.Ports` 10.0.11 |
| 项目平台 | x86、x64、ARM64 |
| 包类型 | `WindowsPackageType=None`，Unpackaged |
| Windows App SDK | Self-contained |
| .NET 发布 | Self-contained、非单文件、未裁剪 |

## 安装开发环境

### 必需软件

1. Windows 10 1809 或更高版本，推荐 Windows 11。
2. [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)。
3. Windows 11 SDK 10.0.26100 或更高版本。
4. 对应串口设备的 Windows 驱动。

项目依赖通过 NuGet 还原，不要求安装额外的 `dotnet workload`。

### Visual Studio

推荐使用 Visual Studio 2026 18.x，或其他明确支持 .NET 10、WinUI 3 和 Windows App SDK C# 项目的版本。

在 Visual Studio Installer 中确认安装以下能力：

- C# 与 .NET 桌面开发工具
- Windows 应用和通用 Windows 平台开发工具
- 使用 C++ 的桌面开发工具
- Windows 11 SDK 10.0.26100

工作负载名称会随 Visual Studio 版本和语言变化；以安装器右侧“安装详细信息”中的组件版本为准。

### 仅使用命令行

命令行构建至少需要：

- .NET 10 SDK
- Windows SDK 10.0.26100
- NuGet 网络访问，或已准备好的本地包缓存

首次还原会下载 Windows App SDK、Windows SDK Build Tools 和 `System.IO.Ports` 包。

## 验证环境

在 PowerShell 中执行：

```powershell
dotnet --info
dotnet --list-sdks
```

确认输出中存在 `10.0.x` SDK，操作系统 RID 与目标架构匹配。然后在仓库根目录验证还原：

```powershell
dotnet restore .\Comet.csproj
```

当前没有 `global.json`，因此默认使用机器上可用的最高兼容 .NET SDK。团队开发需要锁定 SDK 时，可另行添加 `global.json` 并统一版本。

## 构建与运行

### 命令行

Debug x64：

```powershell
dotnet build .\Comet.csproj -c Debug -p:Platform=x64
dotnet run --project .\Comet.csproj -c Debug -p:Platform=x64
```

Release x64：

```powershell
dotnet build .\Comet.csproj -c Release -p:Platform=x64
```

典型输出目录：

```text
bin\x64\Debug\net10.0-windows10.0.26100.0\win-x64\
bin\x64\Release\net10.0-windows10.0.26100.0\win-x64\
```

### Visual Studio

1. 打开 `Comet.sln`。
2. 将 `Comet` 设为启动项目。
3. 选择 `Debug | x64` 或 `Release | x64`。
4. 直接启动调试或执行“不调试启动”。

项目是 Unpackaged WinUI 应用，不需要部署或注册 MSIX。解决方案当前直接列出 x86 和 x64；ARM64 已在项目及发布配置中定义，建议通过命令行或对应发布配置生成。

### 调试串口

- 关闭可能占用目标 COM 端口的其他串口工具。
- 确认设备管理器中设备状态正常。
- 连接后串口参数会锁定，断开后才能修改。
- 调试器停止应用时，页面卸载会停止计时器并释放串口。

## 发布便携版

### 发布配置

仓库包含三个文件系统发布配置：

| 架构 | Platform | Runtime Identifier | 配置文件 |
| --- | --- | --- | --- |
| 32 位 Intel/AMD | x86 | `win-x86` | `win-x86.pubxml` |
| 64 位 Intel/AMD | x64 | `win-x64` | `win-x64.pubxml` |
| Windows on ARM | ARM64 | `win-arm64` | `win-arm64.pubxml` |

不要将不同架构的输出合并到同一目录。

### 生成 x64 便携目录

```powershell
dotnet publish .\Comet.csproj `
  -c Release `
  -p:Platform=x64 `
  -r win-x64 `
  --self-contained true `
  -o .\artifacts\publish\win-x64
```

应用不是单文件发布。必须完整保留发布目录中的 EXE、DLL、PRI、资源和运行时文件；只复制 `Comet.exe` 无法构成可用发布包。

### 加入文档并压缩

```powershell
Copy-Item .\README.md .\artifacts\publish\win-x64\
Copy-Item .\docs .\artifacts\publish\win-x64\docs -Recurse

Compress-Archive `
  -Path .\artifacts\publish\win-x64\* `
  -DestinationPath .\artifacts\Comet-win-x64-portable.zip `
  -Force
```

计算校验值：

```powershell
Get-FileHash .\artifacts\Comet-win-x64-portable.zip -Algorithm SHA256
```

### 包体积说明

便携版同时携带 .NET 运行时、Windows App SDK、WinUI 资源和架构相关本机库，因此体积明显大于业务程序集。项目当前设置：

- `SelfContained=true`
- `PublishSingleFile=false`
- `PublishTrimmed=false`
- Release 启用 ReadyToRun

这些设置优先保证 WinUI、WinRT 和 JSON 运行时元数据可靠。未经完整回归测试，不应直接启用裁剪或单文件发布。

## 清理中间文件

普通清理优先使用：

```powershell
dotnet clean .\Comet.csproj -c Debug -p:Platform=x64
dotnet clean .\Comet.csproj -c Release -p:Platform=x64
```

`bin/`、`obj/`、`.vs/`、`artifacts/` 和发布输出均已由 `.gitignore` 排除，不应提交到仓库。

若 NuGet 缓存文件损坏，可在关闭 Visual Studio 和 Comet 后删除工程内的 `obj` 目录，再执行 `dotnet restore`。不要删除用户目录中的全局 NuGet 包缓存，除非已确认是全局缓存问题。

## 常见问题

### Visual Studio 提示“需要先部署”

当前项目使用：

```xml
<WindowsPackageType>None</WindowsPackageType>
<EnableMsixTooling>false</EnableMsixTooling>
```

在“生成 → 配置管理器”中关闭该项目的 Deploy，保留 Build，然后重新生成并直接启动应用。

### `DEP0700` 或 `resources.pri` 注册失败

这是打包部署流程的错误，而当前项目不需要包注册。关闭 Visual Studio 与 Comet，执行 `dotnet clean`，确认没有启用 Deploy 或旧 MSIX 启动配置，再重新生成 Unpackaged 项目。

### `Microsoft.Build.Tasks.Git` 报 Git 配置字符错误

先检查 `%USERPROFILE%\.gitconfig` 和仓库 `.git\config` 是否为有效 UTF-8 Git 配置。排查期间可临时禁止 SourceLink 查询：

```powershell
dotnet build .\Comet.csproj `
  -c Release `
  -p:Platform=x64 `
  -p:EnableSourceControlManagerQueries=false
```

该参数只用于绕过本机 Git 配置读取，不代表项目源码构建失败。

### 目标电脑无法启动

- 确认系统不低于 Windows 10 1809。
- 确认发布架构与系统匹配。
- 确认整个发布目录已完整解压。
- 检查安全软件是否隔离了 DLL 或本机运行时文件。

### 串口无法打开

- 确认驱动和设备状态正常。
- 确认端口未被其他程序占用。
- 检查波特率、数据位、停止位、校验和流控制。
- 端口列表括号中的设备说明仅用于识别，不参与连接。
