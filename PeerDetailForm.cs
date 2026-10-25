using System;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace TailscaleZHTray;

internal sealed class PeerDetailForm : Form
{
    private readonly TailscaleClient _client;
    private readonly PeerModel _peer;

    private readonly Button _btnPing = new();
    private readonly TextBox _output = new();

    public PeerDetailForm(TailscaleClient client, PeerModel peer)
    {
        _client = client;
        _peer = peer;

        Text = "设备详情 - " + (string.IsNullOrWhiteSpace(peer.DisplayName) ? "未命名设备" : peer.DisplayName);
        ClientSize = new Size(620, 540);
        MinimumSize = new Size(520, 440);
        StartPosition = FormStartPosition.CenterParent;
        Font = new Font("Microsoft YaHei UI", 9F);
        BackColor = Color.FromArgb(247, 248, 250);

        var info = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 210,
            ColumnCount = 2,
            Padding = new Padding(18, 14, 18, 10),
            BackColor = Color.White,
            AutoSize = false
        };
        info.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));
        info.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        AddRow(info, "设备名称", Safe(peer.DisplayName));
        AddRow(info, "DNS 名称", Safe(peer.DnsName));
        AddRow(info, "IP 地址", Safe(string.Join("  /  ", peer.Ips)));
        AddRow(info, "系统", Safe(peer.Os));
        AddRow(info, "状态", peer.Online ? "在线" : "离线");
        AddRow(info, "连接方式", peer.ConnectionText);
        AddRow(info, "收发流量", peer.TrafficText);
        AddRow(info, "最后握手", peer.LastHandshake == default
            ? "—"
            : peer.LastHandshake.ToString("yyyy-MM-dd HH:mm:ss"));
        AddRow(info, "出口节点", peer.ExitNodeOption
            ? (peer.ExitNode ? "当前正在使用" : "可作为出口节点")
            : "否");

        Controls.Add(info);

        var bar = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 46,
            Padding = new Padding(18, 8, 18, 6),
            BackColor = Color.FromArgb(247, 248, 250)
        };

        _btnPing.Text = "Ping 测试";
        _btnPing.Width = 110;
        _btnPing.Height = 30;
        _btnPing.FlatStyle = FlatStyle.Flat;
        _btnPing.BackColor = Color.FromArgb(16, 163, 127);
        _btnPing.ForeColor = Color.White;
        _btnPing.FlatAppearance.BorderSize = 0;
        _btnPing.Click += async (_, _) => await RunPingAsync();
        bar.Controls.Add(_btnPing);

        var tip = new Label
        {
            AutoSize = true,
            Margin = new Padding(12, 7, 0, 0),
            ForeColor = Color.Gray,
            Text = "Ping 走 Tailscale 层：能看到是直连还是经过 DERP 中继"
        };
        bar.Controls.Add(tip);

        Controls.Add(bar);

        var panel = new Panel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(18, 0, 18, 16)
        };

        _output.Dock = DockStyle.Fill;
        _output.Multiline = true;
        _output.ReadOnly = true;
        _output.ScrollBars = ScrollBars.Both;
        _output.WordWrap = false;
        _output.BackColor = Color.FromArgb(30, 32, 36);
        _output.ForeColor = Color.FromArgb(220, 223, 228);
        _output.Font = new Font("Consolas", 9.5F);
        _output.Text = "点击「Ping 测试」开始。";
        panel.Controls.Add(_output);

        Controls.Add(panel);

        // Dock 顺序：先加的靠内，最后加的贴边，这里用 BringToFront 保证填充区在最下层
        info.BringToFront();
        bar.BringToFront();
        panel.SendToBack();
    }

    private static string Safe(string value) =>
        string.IsNullOrWhiteSpace(value) ? "—" : value;

    private static void AddRow(TableLayoutPanel table, string name, string value)
    {
        var label = new Label
        {
            Text = name,
            AutoSize = false,
            Dock = DockStyle.Fill,
            ForeColor = Color.Gray,
            TextAlign = ContentAlignment.MiddleLeft,
            Margin = new Padding(0, 2, 6, 2)
        };
        var content = new Label
        {
            Text = value,
            AutoSize = false,
            Dock = DockStyle.Fill,
            ForeColor = Color.FromArgb(31, 41, 55),
            TextAlign = ContentAlignment.MiddleLeft,
            AutoEllipsis = true,
            Margin = new Padding(0, 2, 0, 2)
        };
        table.RowStyles.Add(new RowStyle(SizeType.Absolute, 22));
        table.Controls.Add(label);
        table.Controls.Add(content);
    }

    private async Task RunPingAsync()
    {
        var ip = _peer.PrimaryIp;
        if (string.IsNullOrWhiteSpace(ip))
        {
            _output.Text = "这台设备没有可用 IP，无法 ping。";
            return;
        }

        _btnPing.Enabled = false;
        _output.Text = $"正在 ping {ip} …（最多 4 次，可能先走 DERP，打洞成功后转为直连）";

        var result = await Task.Run(() => _client.Ping(ip, 4));

        var text = (result.Out ?? "").Trim();
        var error = (result.Err ?? "").Trim();

        _output.Text =
            $"=== tailscale ping {ip} ==={Environment.NewLine}" +
            (string.IsNullOrWhiteSpace(text) ? "(无输出)" : text) +
            (string.IsNullOrWhiteSpace(error) ? "" : $"{Environment.NewLine}{Environment.NewLine}[错误]{Environment.NewLine}{error}");

        _btnPing.Enabled = true;
    }
}
