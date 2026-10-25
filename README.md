# Tailscale 中文托盘 / Tailscale ZH Tray

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
| 状态 | 连接 / 断开、刷新、出口节点、管理后台、健康提示 |
| 设备 | 设备列表（IP / 系统 / 在线 / 直连或 DERP / 收发流量）、详情、Ping |
| 设置 | 7 个开关：允许传入连接、Tailscale DNS、子网路由、自动更新、无人值守、出口节点本地网络、作为出口节点；恢复默认设置 |
| 工具 | 网络检查、Taildrop 发送 / 接收、账号切换、退出登录、打开日志目录 |

托盘菜单提供同样的功能，图标颜色表示连接状态。所有设置值实时读自 `tailscale get --json`，不做本地缓存。

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

产物在 `dist/` 或 `dist_portable/`（已在 `.gitignore` 中排除）。

Windows 下把 `\` 换成 `^` 续行即可。

## 自检

```bash
TailscaleZHTray.exe --selftest
```

结果写入 `%LOCALAPPDATA%\TailscaleZHTray\selftest.txt`，包含后端状态、本机 IP、全部设置项等原始读数。
运行日志在 `%LOCALAPPDATA%\TailscaleZHTray\tray.log`。

## 代码结构

| 文件 | 作用 |
|---|---|
| `TailscaleClient.cs` | 全部 CLI 调用与 JSON 解析 |
| `SettingsCatalog.cs` | 设置开关定义表（加开关只改这一处） |
| `MainForm.cs` | 主窗口 4 个页签 |
| `TrayAppContext.cs` | 托盘图标与右键菜单 |
| `PeerDetailForm.cs` / `AccountForm.cs` / `PickPeerForm.cs` | 子窗口 |
| `Program.cs` | 单实例、自检、异常兜底 |
| `Logger.cs` | 日志 |

## 已知限制

- 设备详情 / Ping 需要至少一台对端设备
- 控制类命令（连接、断开、改设置）需要管理员权限
- 只在作者自己的机器上验证过；Windows 10 未实测

## 致谢

本项目的代码与文档在 deepseek v4.1 flash 1017 协助下完成。

## 许可

[MIT](LICENSE)
