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
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        Font = new Font("Microsoft YaHei UI", 9F);
        BackColor = Color.FromArgb(247, 248, 250);

        var info = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Height = 206,
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
        AddRow(info, "延迟", peer.Latency == null
            ? "尚未测量"
            : $"{peer.LatencyText}（{peer.Latency.At:HH:mm:ss} 测得，最近一次）");
        AddRow(info, "出口节点", peer.ExitNodeOption
            ? (peer.ExitNode ? "当前正在使用" : "可作为出口节点")
            : "否");

        var bar = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
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

        var panel = new Panel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(18, 0, 18, 16)
        };

        _output.Dock = DockStyle.Fill;
        _output.Multiline = true;
        _output.ReadOnly = true;
        _output.ScrollBars = ScrollBars.Vertical;
        _output.WordWrap = true;
        _output.BackColor = Color.FromArgb(30, 32, 36);
        _output.ForeColor = Color.FromArgb(220, 223, 228);
        _output.Font = new Font("Consolas", 9.5F);
        _output.Text = "点击「Ping 测试」开始。";
        panel.Controls.Add(_output);

        // 用 TableLayoutPanel 固定三行布局，避免依赖控件 z-order（曾因 SendToBack
        // 导致输出框被上面的信息区盖住，文字写在看不见的位置）
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            BackColor = Color.FromArgb(247, 248, 250)
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 206));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        layout.Controls.Add(info, 0, 0);
        layout.Controls.Add(bar, 0, 1);
        layout.Controls.Add(panel, 0, 2);

        Controls.Add(layout);
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

        // 顺手把这次结果记为最新的延迟数据，设备列表那边也会跟着更新
        if (result.Code == 0 && !string.IsNullOrWhiteSpace(text))
            _peer.Latency = PingOutputParser.Parse(text);

        _btnPing.Enabled = true;
    }
}
