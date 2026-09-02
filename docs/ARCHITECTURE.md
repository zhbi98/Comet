# 软件架构

本文定义 Comet 的代码分层、依赖方向和重构边界。用户可见行为以 [使用指南](USER_GUIDE.md) 为准，终端控件内部设计见 [虚拟化终端实现](VIRTUAL_TERMINAL.md)，验收方法见 [测试指南](TESTING.md)。

## 目录

- [架构概览](#架构概览)
- [组件职责](#组件职责)
- [关键数据流](#关键数据流)
- [行为不变量](#行为不变量)
- [代码组织与命名](#代码组织与命名)
- [维护规则](#维护规则)

## 架构概览

Comet 采用分层 MVVM，只包含两个纯 C# 产品项目：

- `Comet.Core`：目标为普通 `net10.0`，保存平台无关的核心规则、状态和服务契约，不引用 Windows App SDK 或 WinUI。
- `Comet`：WinUI 3 应用，提供窗口、控件以及 Windows 串口、设备发现、文件和计时器实现。

整体依赖方向如下：

```text
Comet（WinUI 3 应用）
  App（组合根）
  ├─ 具体 Services
  └─ MainViewModel
       ├─ UserSettingsViewModel ───────> IAppSettingsStorageService
       ├─ ConnectionViewModel ─────────> ISerialPortService
       ├─ TerminalViewModel ───────────> Core/Terminal + Core/Text
       ├─ TerminalAppearanceViewModel
       ├─ TransmissionViewModel ───────> Core/Transmission
       ├─ CommandPresetsViewModel ─────> ICommandPresetStorageService
       ├─ ReceiveRecordingViewModel ───> IRawReceiveRecordingService
       └─ ScheduledSendViewModel ──────> ConnectionViewModel + IPeriodicTimer

Views / Controls
  ├─ ViewModels
  └─ 仅 UI 专属的 WinUI 行为

Comet 中的具体 Services
  ├─ Comet.Core/Services 中的抽象接口
  └─ Models

Comet.Core
  ├─ Models 或 .NET 基础类库
  └─ 不依赖 WinUI、窗口、文件选择器或具体串口服务
```

依赖必须指向图中的下层或抽象接口。以下边界不可跨越：

- 核心规则不读取 WinUI 控件，也不直接访问串口、文件或窗口。
- ViewModel 持有页面状态，并协调核心规则和抽象服务。
- View 负责 XAML、焦点、滚动、文件选择器、`DispatcherQueue` 和窗口生命周期。
- 具体服务封装 Windows 串口、SetupAPI、JSON 文件和高分辨率计时器。
- `App` 是组合根，负责创建具体服务和根 ViewModel，再注入窗口与页面。
- ViewModel 不持有 `TextBox`、`ScrollViewer`、`Brush` 或 `DispatcherQueue`，核心层也不通过静态全局对象反向访问页面。

架构组织借鉴 [Windows Calculator 的应用架构](https://github.com/microsoft/calculator/blob/main/docs/ApplicationArchitecture.md) 和 [Files 的源码结构](https://github.com/files-community/Files/tree/main/src)。

这两个项目仅作为设计参考，不是代码依赖；Comet 不复制其语言组成或项目规模。

## 组件职责

### Core

`Comet.Core` 负责可独立解释输入和维护状态的核心逻辑。各目录的职责如下：

| 目录 | 职责 |
| --- | --- |
| `src/Comet.Core/Models` | 用户设置、串口参数、终端条目和快捷指令等数据模型 |
| `src/Comet.Core/Presets` | 快捷指令容量、JSON 格式、校验和规范化 |
| `src/Comet.Core/Recording` | 原始 RX 有界队列、异步顺序写盘、停止刷新和失败处理 |
| `src/Comet.Core/Services` | 串口、设置、快捷指令、录制和周期计时器的抽象接口 |
| `src/Comet.Core/Terminal` | 文本/HEX 双会话、格式化边界、分段存储和虚拟显示行文档 |
| `src/Comet.Core/Text` | HEX 编解码、文本转义、编码目录和跨接收批次流式解码 |
| `src/Comet.Core/Transmission` | 解释文本转义、HEX 和行尾，生成实际发送字节并规范化内容区换行 |
| `src/Comet.Core/ViewModels` | 页面状态、功能协调和服务调用入口 |

Core 类型不显示消息、不操作控件，也不决定窗口何时滚动。

### ViewModels

`MainViewModel` 是页面的根 ViewModel，只负责聚合以下功能 ViewModel：

| ViewModel | 状态与行为 |
| --- | --- |
| `UserSettingsViewModel` | 加载和保存当前用户设置快照；不包含连接、终端内容或录制等运行时状态 |
| `ConnectionViewModel` | 端口集合、当前端口、连接状态、刷新、打开、关闭和发送入口 |
| `TerminalViewModel` | 完整会话、流式解码器、RX/TX 字节计数和显示模式 |
| `TerminalAppearanceViewModel` | 平台无关的字体名称、字号约束和默认值 |
| `TransmissionViewModel` | 把页面发送意图交给纯核心发送引擎 |
| `CommandPresetsViewModel` | 快捷指令增删改查、顺序保存、JSON 备份和持久化协调 |
| `ReceiveRecordingViewModel` | 原始 RX 录制启停、文件路径和失败通知 |
| `ScheduledSendViewModel` | 单条循环和列表循环的定时状态、并发写入保护和失败通知 |

常规可绑定状态使用 `INotifyPropertyChanged`。后台录制状态和失败等跨线程变化使用显式事件，由 View 调度到 UI 线程。需要 WinUI 控件完成的动作也通过事件交给 View，例如循环发送成功后，View 再把 TX 增量提交到终端控件。

### Services

`Comet.Core/Services` 定义 ViewModel 所需能力。Windows 专属实现位于 `Comet/Services`；仅依赖 .NET 文件流的录制实现位于 `Comet.Core/Recording`。

- **串口会话：** `ISerialPortService` / `SerialPortService` 负责收发、释放和后台自动恢复。串口读写缓冲分别为 16 KiB 和 4 KiB，读写超时分别为 500 ms 和 1000 ms。Core 只区分物理端口可写和用户连接会话有效，不感知恢复过程。
- **端口发现：** `SerialPortDiscovery` 枚举 COM 端口，通过 SetupAPI 读取设备名称并检查端口是否存在；`SerialPortFactory` 为初次连接和自动恢复统一映射参数、创建串口实例。
- **用户设置：** `IAppSettingsStorageService` / `AppSettingsStorageService` 负责 `settings.json` 的容错读取和原子替换保存。
- **快捷指令：** `ICommandPresetStorageService` / `CommandPresetStorageService` 负责 `presets.json` 的读取和保存。
- **周期调度：** `IPeriodicTimer` / `HighResolutionPeriodicTimer` 提供不依赖 UI 调度器的发送周期。
- **原始录制：** `IRawReceiveRecordingService` / `RawReceiveRecordingService` 提供不依赖 WinUI 的原始 RX 后台写入和生命周期。

这些小型接口让 ViewModel 不依赖具体串口或文件实现，也为单元测试替身保留边界。

### Views 与 Controls

`App` 创建服务和 `MainViewModel`，`MainWindow` 将根 ViewModel 注入 `MainPage`。各 UI 组件的边界如下：

| 组件 | 负责 | 不负责 |
| --- | --- | --- |
| `MainWindow` | 窗口生命周期、标题栏、字体和关于对话框、快捷指令文件选择器 | 保存业务状态、解释发送内容 |
| `MainPage` | 组合控件值、调用 ViewModel、调度后台通知、控制焦点、滚动和 InfoBar | 实现核心编码规则、直接写录制文件 |
| `VirtualTerminalControl` | 可见行绘制、选择、复制、光标、滚动和输入代理 | 打开串口、保存快捷指令、解释发送文本 |

**窗口级交互。** `MainWindow.xaml.cs` 只保留窗口生命周期。字体交互、快捷指令备份和运行时信息展示分别位于对应的 partial code-behind 文件。菜单只导航到字体设置、快捷指令导入导出或关于信息，不保存业务状态。

字体设置只修改 `TerminalAppearanceViewModel`。`MainPage` 监听状态变化并把字体名称转换为 WinUI `FontFamily`；`VirtualTerminalControl` 原子应用字体和字号，重新测量字符单元，并按文档偏移保留滚动与选择状态。

**窗口图标。** 应用图标只有一个源文件 `Assets/CometTerminalIcon.ico`。构建时它同时写入 EXE 并嵌入程序集；运行时 `WindowIconManager` 从同一资源创建标题栏图像，并按窗口 DPI 从当前 EXE 选择合适的 Win32 图标帧。图标加载不依赖工作目录或发布目录中的外部图片。

### 快捷指令的跨层协作

快捷指令功能跨越 `MainWindow`、`MainPage`、Core ViewModel 和存储服务，需要保持以下边界：

- **容量和格式：** `CommandPresetLimits` 是 60 条容量上限的唯一来源；`CommandPresetJsonCodec` 定义本地存储和备份共用的 JSON 格式，并在读写边界应用容量限制。`CommandPresetsViewModel` 再次维护集合容量不变量，窗口只绑定 `CanAdd` 控制添加入口，不复制这些规则。
- **初始化和导入：** 初始化只读取，不创建、截断或重写配置。导入在完整解析后替换集合并调用抽象存储服务；保存失败时恢复导入前的集合。Core 不依赖 `StorageFile`、文件选择器或 `ContentDialog`。
- **列表排序：** `MainPage.CommandPresets` 使用 `ListView` 内置重排，只管理排序模式、拖拽手柄授权和未保存标记。多次拖拽只改变内存集合；用户点击“完成”后，`CommandPresetsViewModel` 才一次性保存最终顺序。
- **排序失败：** 保存失败不回滚进程内顺序，用户可以再次进入排序并重试。若关闭前始终没有保存成功，下次启动读取磁盘中最后一次成功保存的顺序。
- **循环发送：** View 按当前卡片生成不可变载荷快照，`ScheduledSendViewModel` 只接收载荷数组、共享周期和调度模式。计时器线程逐项发送，末项后回到首项，直到 View 明确停止。
- **运行时锁定：** 列表循环期间，View 锁定新增、编辑、删除、立即发送和排序，并保证快捷指令循环与底部单条循环互斥。调度器不持有 `ListView`，也不产生滚动行为。

## 关键数据流

### 接收

```text
SerialPortService.DataReceived
  -> ConnectionViewModel.BytesReceived
  ├─> ReceiveRecordingViewModel.TryRecord
  │    -> RawReceiveRecordingService 有界队列
  │    -> FileStream 顺序写入 .bin
  └─> MainPage 并发队列与 UI 批处理
       -> TerminalViewModel.DecodeReceived / RecordReceived
       -> TerminalBuffer.Append
       -> VirtualTerminalControl.AppendText
```

**录制边界。** 录制分支在文本解码和 UI 队列之前接收同一个不可变字节快照。只有开始录制后的新 RX 缓冲进入文件；终端历史、显示转换、TX 和前缀不会反向进入录制服务。

**显示批处理。** 显示分支在低优先级 `DispatcherQueue` 中处理，每批最多累计约 256 KiB 或占用约 8 ms 后让出 UI 线程。单个已入队数据块不会拆分，因此一批可能略大于 256 KiB；队列仍有数据时再次低优先级调度。

格式化字符继续按待渲染量合并，避免高吞吐 RX 长期独占 UI 线程：

| 待渲染字符数 | 合并等待时间 |
| --- | --- |
| 少于 25,000 | 33 ms |
| 25,000–249,999 | 50 ms |
| 250,000 及以上 | 100 ms |

### 发送

```text
View 用户操作
  -> TransmissionViewModel
  -> SerialPayloadEngine
  -> ConnectionViewModel.Send
  -> SerialPortService
  -> TerminalViewModel.RecordSent
  -> View 按当前时间戳规则决定是否显示 TX
```

### 定时发送

```text
View 开启底部循环或快捷指令列表循环
  -> ScheduledSendViewModel
  -> IPeriodicTimer 后台回调
  -> ConnectionViewModel.Send
  -> PayloadSent 事件
  -> View DispatcherQueue
  -> 计数与终端 TX 增量
```

单条模式持续重复当前底部载荷；列表模式按快照顺序循环并在末尾回到首项。串口写入发生在计时器线程，UI 渲染不能反向阻塞周期调度。

## 行为不变量

架构调整不得改变以下系统约束：

1. **时间戳：** 开关只影响新条目，不重写现有内容。
2. **HEX 模式：** “HEX 显示”和“HEX 发送”彼此独立。
3. **内容区输入：** 键入立即发送且不产生本地回显；只有设备 RX 回传进入内容区。
4. **快捷发送：** 快捷指令立即发送不覆盖底部发送框。
5. **载荷解释：** 文本转义、HEX 分隔符、默认行尾和内容区 Enter 规则保持使用指南定义。
6. **完整会话：** 文本和 HEX 会话在清空前不按视口容量淘汰。
7. **传输计数：** RX/TX 按实际成功传输字节累计。
8. **定时发送：** 两种模式都在一个完整周期后首次写入，共用 20–60,000 ms 周期；快捷指令列表持续循环到用户停止。
9. **窗口关闭：** `Closed` 和页面 `Unloaded` 统一进入幂等 `Shutdown()`；先停止定时发送，再关闭并释放串口，重复回调不得重复释放资源。
10. **界面表现：** UI 布局、控件名称、默认值、图标和 Windows 10/11 表现不因分层调整而改变。
11. **原始录制：** 只写开始后的 RX 字节；停止、断开和关闭必须完成队列排空、文件刷新与释放。
12. **设备恢复：** USB 串口物理移除后，服务释放旧句柄并保留连接意图；同一 COM 口恢复时使用原参数自动重连，且不向 UI 暴露临时恢复状态。手动断开或关闭窗口必须取消重连。

具体输入格式、显示规则和操作语义只在 [使用指南](USER_GUIDE.md) 中定义。以上条目仅描述跨层重构时不能破坏的系统约束。

## 代码组织与命名

- 类型、属性和方法使用 PascalCase，例如 `SerialPortService` 和 `DrainReceiveQueue`。
- 接口使用 `I` 加 PascalCase，例如 `ISerialPortService`。
- 私有字段使用 `_camelCase`，例如 `_serialPortService`。
- 常量使用 `UPPER_SNAKE_CASE`；布尔值优先使用 `Is`、`Has`、`Can` 或 `Should` 表达含义。
- 模型和服务分别以 `Model`、`Service` 结尾，异步方法以 `Async` 结尾。
- 文件夹与命名空间保持对应；文件名与主类型一致，一个文件原则上只定义一个顶层类型。
- 同一页面的 XAML 和 partial 文件放在 `Views`；这些文件只协调 WinUI 行为，状态和核心规则进入 `Comet.Core`。

## 维护规则

- 新的协议解析、编码或会话规则放入 `Comet.Core`，并通过 ViewModel 暴露。
- 新的 Windows、设备或文件系统访问先定义小型服务接口，再提供具体实现。
- 新的页面状态放入对应功能 ViewModel；仅视觉状态留在 View。
- 不为单一简单调用创建空洞层级，也不让 ViewModel 依赖 WinUI 类型来追求目录对称。
- 重构提交必须执行 [测试指南](TESTING.md) 中与变更区域对应的回归检查。
