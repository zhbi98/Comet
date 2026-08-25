# 软件架构

本文定义 Comet 的代码分层、依赖方向和重构边界。功能语义以 [README](../README.md) 为准，终端控件内部设计见 [VIRTUAL_TERMINAL](VIRTUAL_TERMINAL.md)，验收方法见 [TESTING](TESTING.md)。

## 设计原则

Comet 借鉴 Windows Calculator 的“界面与计算核心分离”以及 Files 的 MVVM 组织思路。解决方案只包含两个产品项目，并且全部使用 C#：`Comet.Core` 是不依赖 WinUI 的逻辑类库，`Comet` 是 WinUI 3 应用：

- 核心规则不读取 WinUI 控件，也不直接访问串口、文件或窗口。
- ViewModel 持有页面状态，协调核心规则和抽象服务。
- View 负责 XAML、焦点、滚动、文件选择器、DispatcherQueue 和窗口生命周期。
- 具体服务实现 Windows 串口、SetupAPI、JSON 文件和高分辨率计时器等系统能力。
- `App` 是组合根，创建具体服务和根 ViewModel，再注入窗口与页面。
- 所有实现均为 C#；工程不引入 C、C++、C++/CLI 或跨语言桥接项目。

参考项目并不是代码依赖，也不会复制其语言或工程规模：

- [Windows Calculator application architecture](https://github.com/microsoft/calculator/blob/main/docs/ApplicationArchitecture.md)
- [Files source tree](https://github.com/files-community/Files/tree/main/src)

## 分层与依赖方向

```text
Comet（WinUI 3 应用）
  App（组合根）
  -> 具体 Services
  -> MainViewModel
       ├─ ConnectionViewModel -> ISerialPortService
       ├─ TerminalViewModel   -> Core/Terminal + Core/Text
       ├─ TransmissionViewModel -> Core/Transmission
       ├─ CommandPresetsViewModel -> ICommandPresetStorageService
       └─ RepeatSendViewModel -> ConnectionViewModel + IPeriodicTimer

Views / Controls
  -> ViewModels
  -> 仅 UI 专属的 WinUI 行为

Comet 中的具体 Services
  -> Comet.Core/Services 中的抽象接口
  -> Models

Comet.Core
  -> Models 或 .NET 基础类库
  -X-> WinUI、窗口、文件选择器、具体串口服务
```

依赖只能指向图中的下层或抽象接口。核心层不得通过静态全局对象反向访问页面；ViewModel 不应持有 `TextBox`、`ScrollViewer`、`Brush` 或 `DispatcherQueue`。

## 各层职责

### Core

`Comet.Core` 相当于 Calculator 中可独立解释输入和维护状态的计算核心。该项目目标为普通 `net10.0`，不引用 Windows App SDK 或 WinUI。

| 目录 | 职责 |
| --- | --- |
| `src/Comet.Core/Transmission` | 解释文本转义、HEX 和行尾，生成实际发送字节；规范化内容区换行输入 |
| `src/Comet.Core/Text` | HEX 编解码、文本转义、编码目录和跨接收批次流式解码 |
| `src/Comet.Core/Terminal` | 文本/HEX 双会话、格式化边界、完整分段存储和虚拟显示行文档 |

这些类型不显示消息、不操作控件，也不决定窗口何时滚动。

### ViewModels

`MainViewModel` 是页面的根 ViewModel，只聚合功能 ViewModel：

| ViewModel | 状态与行为 |
| --- | --- |
| `ConnectionViewModel` | 端口集合、当前端口、连接状态、刷新、打开、关闭和发送入口 |
| `TerminalViewModel` | 完整会话、流式解码器、RX/TX 字节计数和显示模式 |
| `TransmissionViewModel` | 把页面发送意图交给纯核心发送引擎 |
| `CommandPresetsViewModel` | 快捷指令集合、增删改查和持久化协调 |
| `RepeatSendViewModel` | 周期发送状态、并发写入保护以及成功/失败通知 |

ViewModel 使用 `INotifyPropertyChanged` 暴露可绑定状态。需要 WinUI 控件才能完成的动作由事件返回给 View，例如循环发送成功后，View 再把 TX 文本提交到终端控件。

### Services

`Comet.Core/Services` 定义 ViewModel 所需能力，具体实现位于 WinUI 项目的 `Comet/Services`：

- `ISerialPortService` / `SerialPortService`：端口枚举、SetupAPI 设备名称、连接、收发和释放。
- `ICommandPresetStorageService` / `CommandPresetStorageService`：`presets.json` 读取与保存。
- `IPeriodicTimer` / `HighResolutionPeriodicTimer`：不依赖 UI 调度器的循环发送周期。

抽象接口使 ViewModel 不需要知道串口或文件的实现细节，也为后续单元测试替身保留边界。

### Views 与 Controls

WinUI 项目的 `App` 创建服务和 `MainViewModel`，`MainWindow` 把 ViewModel 注入 `MainPage`。页面 code-behind 只保留以下职责：

- 把控件当前值组合成一次用户操作并调用 ViewModel。
- 使用 `DispatcherQueue` 合并后台接收通知。
- 控制 InfoBar、焦点、滚动、文件选择器和窗口标题。
- 把 ViewModel 产生的终端增量交给 `VirtualTerminalControl`。

`VirtualTerminalControl` 是专用 View 控件。它管理绘制、选择、复制、光标与输入代理，但不打开串口、不保存快捷指令，也不解释发送文本。

## 关键数据流

### 接收

```text
SerialPortService.DataReceived
  -> ConnectionViewModel.BytesReceived
  -> MainPage 并发队列与 UI 批处理
  -> TerminalViewModel.DecodeReceived / RecordReceived
  -> TerminalBuffer.Append
  -> VirtualTerminalControl.AppendText
```

### 发送

```text
View 用户操作
  -> TransmissionViewModel
  -> SerialPayloadEngine
  -> ConnectionViewModel.Send
  -> SerialPortService
  -> TerminalViewModel.RecordSent
  -> View 按现有时间戳规则决定是否显示 TX
```

### 循环发送

```text
View 开启循环发送
  -> RepeatSendViewModel
  -> IPeriodicTimer 后台回调
  -> ConnectionViewModel.Send
  -> PayloadSent 事件
  -> View DispatcherQueue
  -> 计数与终端 TX 增量
```

串口写入发生在计时器线程；UI 渲染不能反向阻塞周期调度。

## 行为不变量

架构重构不得改变以下约束：

1. 时间戳开关只影响新条目，不重写现有内容。
2. “HEX 显示”和“HEX 发送”彼此独立。
3. 内容区键入立即发送且不产生本地回显；只有设备 RX 回传进入内容区。
4. 快捷指令立即发送不覆盖底部发送框。
5. 文本转义、HEX 分隔符、默认行尾和内容区 Enter 规则保持 README 定义。
6. 文本和 HEX 完整会话在清空前不按视口容量淘汰。
7. RX/TX 计数按实际成功传输字节累计。
8. 循环发送首次写入仍在一个完整周期后发生，周期范围仍为 20–60,000 ms。
9. 关闭窗口时先停止循环发送，再关闭并释放串口；整个过程保持幂等。
10. UI 布局、控件名称、默认值、图标和 Windows 10/11 表现不因分层调整而改变。

## 维护规则

- 新的协议解析、编码或会话规则放入 `Core`，并通过 ViewModel 暴露。
- 新的 Windows、设备或文件系统访问先定义小型服务接口，再实现具体服务。
- 新的页面状态放入对应功能 ViewModel；仅视觉状态才留在 View。
- 不为单一简单调用创建空洞层级，也不让 ViewModel 依赖 WinUI 类型来追求目录对称。
- 重构提交必须执行 [TESTING](TESTING.md) 中与变更区域对应的回归检查。
