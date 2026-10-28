using System;

namespace TailscaleZHTray;

internal sealed class ToggleSpec
{
    public string Id = "";
    public string Label = "";
    public Func<TailscaleSnapshot, bool> Get;
    public Func<bool, string> ArgFor;
    public Func<bool, string> Confirm;

    /// <summary>鼠标悬停在勾选框上时的说明。</summary>
    public string Hint = "";
}

internal static class SettingsCatalog
{
    private static string B(bool value) => value ? "true" : "false";

    public static readonly ToggleSpec[] All =
    {
        new()
        {
            Id = "incoming",
            Hint = "关闭这一项等于开启 Shields Up：拒绝所有入站连接，别人也发不了 Taildrop 文件给你。",
            Label = "允许传入连接",
            Get = s => !s.ShieldsUp,
            ArgFor = v => $"set --shields-up={B(!v)}",
            Confirm = v => v
                ? null
                : "关闭「允许传入连接」会开启 Shields Up，拒绝所有入站连接。\n\n确定吗？"
        },
        new()
        {
            Id = "dns",
            Hint = "允许 Tailscale 接管 DNS 解析，这样可以用设备名（MagicDNS）和内网域名访问；关闭后只能走 IP。",
            Label = "使用 Tailscale DNS 设置",
            Get = s => s.AcceptDns,
            ArgFor = v => $"set --accept-dns={B(v)}"
        },
        new()
        {
            Id = "routes",
            Hint = "接受其他设备宣告的子网路由，从而访问对方所在的整个内网网段，而不只是那一台机器。",
            Label = "使用子网路由",
            Get = s => s.AcceptRoutes,
            ArgFor = v => $"set --accept-routes={B(v)}"
        },
        new()
        {
            Id = "autoupdate",
            Label = "自动安装更新（Tailscale 官方客户端）",
            Get = s => s.AutoUpdate,
            ArgFor = v => $"set --auto-update={B(v)}"
        },
        new()
        {
            Id = "unattended",
            Hint = "开启后，即使当前 Windows 用户注销，Tailscale 仍以系统服务身份继续运行。",
            Label = "无人值守运行",
            Get = s => s.Unattended,
            ArgFor = v => $"set --unattended={B(v)}",
            Confirm = v => v
                ? "开启「无人值守运行」后，即使当前 Windows 用户注销，Tailscale 也会继续在后台运行。\n\n确定开启吗？"
                : "关闭「无人值守运行」后，用户注销时 Tailscale 会停止。\n\n确定关闭吗？"
        },
        new()
        {
            Id = "lan",
            Hint = "使用出口节点时，仍然允许访问本机所在的局域网设备（例如家里的打印机、路由器）。",
            Label = "允许访问本地网络（走出口节点时）",
            Get = s => s.ExitNodeAllowLanAccess,
            ArgFor = v => $"set --exit-node-allow-lan-access={B(v)}"
        },
        new()
        {
            Id = "advertise-exit",
            Hint = "把本机作为出口节点，让其他设备经由本机上网。设置后还需要在 Tailscale 管理后台批准。",
            Label = "作为出口节点",
            Get = s => s.AdvertiseExitNode,
            ArgFor = v => $"set --advertise-exit-node={B(v)}",
            Confirm = v => v
                ? "把本机设为出口节点后，其他设备可以经由本机上网。\n" +
                  "设置后还需要在管理后台批准该出口节点。\n\n确定吗？"
                : "不再作为出口节点后，正在使用本机作出口的设备会失去出口。\n\n确定吗？"
        },
    };
}
