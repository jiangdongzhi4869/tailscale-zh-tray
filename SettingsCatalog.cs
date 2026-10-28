using System;

namespace TailscaleZHTray;

internal sealed class ToggleSpec
{
    public string Id = "";
    public string Label = "";
    public Func<TailscaleSnapshot, bool> Get;
    public Func<bool, string> ArgFor;
    public Func<bool, string> Confirm;

}

internal static class SettingsCatalog
{
    private static string B(bool value) => value ? "true" : "false";

    public static readonly ToggleSpec[] All =
    {
        new()
        {
            Id = "incoming",
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
            Label = "使用 Tailscale DNS 设置",
            Get = s => s.AcceptDns,
            ArgFor = v => $"set --accept-dns={B(v)}"
        },
        new()
        {
            Id = "routes",
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
            Label = "允许访问本地网络（走出口节点时）",
            Get = s => s.ExitNodeAllowLanAccess,
            ArgFor = v => $"set --exit-node-allow-lan-access={B(v)}"
        },
        new()
        {
            Id = "advertise-exit",
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
