# Tailscale 中文托盘 / Tailscale ZH Tray

> ### ⚠️ 本项目已归档，不再更新
>
> - **永久可用** —— 仓库长期保留、不会删除，代码可以随时克隆、编译、使用。
> - **不接受任何提交** —— Issue 和 Pull Request 都已关闭，不必再提。
> - **欢迎 Fork** —— 想继续改就自己 Fork 一份维护，不需要申请，注明出处即可。
>
> 归档前的最终版本是 **1.4.24**，功能完整、可直接使用；本项目不再修复问题、不再加新功能。

---

> **本项目与 Tailscale Inc. 无任何关联，是非官方第三方程序。**
> This is an **unofficial** third-party project, not affiliated with Tailscale Inc.

用官方 `tailscale.exe` 命令行接口实现的**中文托盘前端**，不改官方任何文件、不注入、不写注册表。

A Chinese-language tray front-end for Windows, built on top of the official `tailscale.exe` CLI.
It does **not** modify, patch, or inject into any Tailscale file.

**本仓库只提供源码，不提供任何预编译的可执行文件。**
This repository contains **source code only** — no prebuilt binaries are published.

---

## 为什么做这个

Tailscale 的 Windows 桌面客户端（`tailscale-ipn.exe`）是闭源的：

- 界面代码在私有 Go 模块 `tailscale.io/win/gui` / `tailscale.io/win/ui`
- 文案直接编译进二进制，没有语言包或资源文件
- 官方中文支持请求（issue #7771）长期未实现

所以不存在"汉化包"这种东西。本项目用 Tailscale 官方公开的 CLI 接口另做一个中文界面，与官方托盘并存，互不干扰。

## 功能

| 页面 | 内容 |
|---|---|
| 状态 | 连接 / 断开、刷新、出口节点、管理后台、健康提示；右下角吉祥物（可换图） |
| 设备 | 设备列表（设备名 / 地址 / 系统 / 在线 / 连接方式 / 延迟 / 密钥过期）、右键菜单、拖放发送单个文件、列宽记忆 |
| 设置 | 7 个开关：允许传入连接、Tailscale DNS、子网路由、自动更新（官方客户端）、无人值守、出口节点本地网络、作为出口节点；恢复默认设置 |
| 工具 | 网络检查、发送文件、打开下载目录、账号切换、退出登录、打开日志目录 |

**不做后台轮询。** 只有点「刷新」或托盘菜单里的刷新才会读状态；延迟也是点刷新后才测，并逐台进行、每台之间隔 3 秒，随时可以从托盘退出强制中断。所有设置值实时读自 `tailscale get --json`，不做本地缓存。

### 设备页细节

- **连接方式**：显示「直连」或「中继 xxx」；如果刚 Ping 过，优先用 Ping 结果里的路径，没 Ping 过才看 `status --json` 的 `CurAddr` / `Relay`。
- **延迟**：`tailscale ping -c 3` 取最后一次 pong 的往返时间；超时显示「超时」。
- **密钥过期**：显示到期日期和剩余天数，30 天内会标红；永不过期的密钥显示「永不过期」。
- **右键菜单**：复制 IPv4 / 复制 IPv6 / 复制全部 IP / 复制设备名 / 发送文件到这台 / 查看详情 / Ping / 设为出口节点。
- **拖放发送**：把**一个**文件拖到设备行上直接发送；设备详情窗口里也有一块拖放区。文件夹和多选不支持，拖进去会提示。
- 列宽改完会记住，下次启动保持（存在 exe 同级 `data\columns.txt`）。

### 吉祥物

状态页右下角会显示 `assets\mascot.png`。**图片是运行时从 exe 旁边读的**，想换形象直接替换这个文件重启即可，不用重新编译。文件不存在或读不出来就整块不显示，不影响其他功能。

## 系统要求

- Windows 10（1607 及以上，建议 LTSC / 企业版）或 Windows 11，**64 位**
- 已安装 **Tailscale**（需要 `tailscale.exe` 在系统 PATH 中）
- 编译需要 **.NET SDK 10**（运行时可自包含打包，不需要目标机器装 .NET）

## 从源码编译

```bash
# 依赖框架版（体积小，目标机器需装 .NET 10 桌面运行时）
dotnet publish -c Release -r win-x64 --self-contained false \
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o dist

# 便携版（自包含，不依赖运行时，体积较大）
dotnet publish -c Release -r win-x64 --self-contained true \
  -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true \
  -p:IncludeNativeLibrariesForSelfExtract=true -o dist_portable
```

产物在 `dist/` 或 `dist_portable/`（已在 `.gitignore` 中排除）。吉祥物图片会作为 `assets\mascot.png` 一起输出到产物目录，不要把 `assets` 文件夹删掉。

Windows 下把 `\` 换成 `^` 续行即可。

## 自检

```bash
TailscaleZHTray.exe --selftest
```

结果写入 exe 同级 `data\selftest.txt`，包含后端状态、本机 IP、全部设置项等原始读数。
运行日志在 exe 同级 `data\tray.log`。
（`data\` 目录不可写时——例如程序装在 `Program Files`——会自动回退到 `%LOCALAPPDATA%\TailscaleZHTray\`。）

## 代码结构

| 文件 | 作用 |
|---|---|
| `TailscaleClient.cs` | 全部 CLI 调用与 JSON 解析（含 ping 输出解析、Taildrop 发送） |
| `SettingsCatalog.cs` | 设置开关定义表（加开关只改这一处） |
| `MainForm.cs` | 主窗口 4 个页签 |
| `TrayAppContext.cs` | 托盘图标与右键菜单、逐台测延迟的调度 |
| `PeerDetailForm.cs` / `AccountForm.cs` / `PickPeerForm.cs` | 子窗口（详情窗口带拖放发送区） |
| `FileDropHelper.cs` | 拖放校验（只接受单个文件） |
| `KnownFolders.cs` | 取系统「下载」文件夹路径并用资源管理器打开 |
| `Program.cs` | 单实例、自检、异常兜底 |
| `Logger.cs` | 日志 |
| `assets/mascot.png` | 状态页吉祥物（可替换） |

## 已知限制

- **Taildrop**：一次只发一个文件（文件夹请先压缩）；只能发给**在线**设备，离线会直接报错（Taildrop 本身不排队）；不能发给自己；收到的文件由官方客户端自动存进系统「下载」文件夹，本程序只负责把这个目录打开。
- **延迟**不会自动测，需要点「刷新」；逐台测是为了不把 `tailscale ping` 一次性铺满。
- 设备详情 / Ping 需要至少一台对端设备。
- 控制类命令（连接、断开、改设置）需要管理员权限；权限不足时程序会提示，但不会自动提权重启。
- 与官方托盘并存时，**建议只用其中一个做控制操作**：两边同时发命令时 `WantRunning` 可能互相覆盖，对方界面也不保证即时刷新。
- 只在作者自己的机器上验证过；Windows 10 未实测。

## 致谢

本项目的代码与文档在 deepseek v4.1 flash 1017 协助下完成。

## 许可

[MIT](LICENSE)
