using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace TailscaleZHTray;

internal sealed class PeerDetailForm : Form
{
    private readonly TailscaleClient _client;
    private readonly PeerModel _peer;
    private readonly Action _latencyUpdated;

    private readonly Button _btnPing = new();
    private readonly TextBox _output = new();
    private readonly Label _dropZone = new();
    private Label _valConnection;
    private Label _valLatency;
    private Label _valStatus;

    public PeerDetailForm(TailscaleClient client, PeerModel peer, Action latencyUpdated = null)
    {
        _client = client;
        _peer = peer;
        _latencyUpdated = latencyUpdated;

        Text = "设备详情 - " + (string.IsNullOrWhiteSpace(peer.DisplayName) ? "未命名设备" : peer.DisplayName);
        ClientSize = new Size(620, 540);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        Font = new Font("Microsoft YaHei UI", 9F);
        AutoScaleMode = AutoScaleMode.Dpi;
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
        AddRow(info, "系统", Safe(peer.OsText));
        _valStatus = AddRow(info, "状态", peer.Online ? "在线" : "离线");
        _valConnection = AddRow(info, "连接方式", peer.ConnectionText);
        _valLatency = AddRow(info, "延迟", LatencyText());
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

        // 拖放区：把文件拖进来就发给当前这台设备
        _dropZone.Dock = DockStyle.Fill;
        _dropZone.Text = "拖一个文件到这里 → 直接发送给这台设备（Taildrop）";
        _dropZone.TextAlign = ContentAlignment.MiddleCenter;
        _dropZone.BorderStyle = BorderStyle.FixedSingle;
        _dropZone.BackColor = Color.FromArgb(240, 253, 248);
        _dropZone.ForeColor = Color.FromArgb(16, 163, 127);
        _dropZone.Margin = new Padding(18, 2, 18, 6);
        _dropZone.AllowDrop = true;
        _dropZone.DragEnter += OnDropEnter;
        _dropZone.DragOver += OnDropEnter;
        _dropZone.DragLeave += (_, _) => SetDropHighlight(false);
        _dropZone.DragDrop += async (_, e) => await OnDropAsync(e);

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
            RowCount = 4,
            BackColor = Color.FromArgb(247, 248, 250)
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 206));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        layout.Controls.Add(info, 0, 0);
        layout.Controls.Add(_dropZone, 0, 1);
        layout.Controls.Add(bar, 0, 2);
        layout.Controls.Add(panel, 0, 3);

        Controls.Add(layout);
    }

    private static string Safe(string value) =>
        string.IsNullOrWhiteSpace(value) ? "—" : value;

    private static Label AddRow(TableLayoutPanel table, string name, string value)
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
        return content;
    }

    /// <summary>窗口一打开就重新取一次状态，别拿列表里那份旧快照糊弄人。</summary>
    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        _ = RefreshInfoAsync();
    }

    private async Task RefreshInfoAsync()
    {
        try
        {
            var snapshot = await Task.Run(() => _client.Fetch());
            if (IsDisposed || snapshot == null)
                return;

            var fresh = snapshot.Peers.FirstOrDefault(SamePeer);
            if (fresh == null)
                return;

            // 延迟是本地 Ping 测的，status 里没有，留着不动
            var latency = _peer.Latency;

            _peer.HostName = fresh.HostName;
            _peer.DnsName = fresh.DnsName;
            _peer.Os = fresh.Os;
            _peer.Ips = fresh.Ips;
            _peer.Online = fresh.Online;
            _peer.ExitNode = fresh.ExitNode;
            _peer.ExitNodeOption = fresh.ExitNodeOption;
            _peer.Relay = fresh.Relay;
            _peer.CurAddr = fresh.CurAddr;
            _peer.KeyExpiry = fresh.KeyExpiry;
            _peer.Latency = latency;

            if (IsDisposed)
                return;

            _valStatus.Text = _peer.Online ? "在线" : "离线";
            UpdateInfoRows();
        }
        catch (Exception ex)
        {
            Logger.Log("详情页刷新状态失败：" + ex.Message);
        }
    }

    /// <summary>按 DNS 名 / 设备名 / 主 IP 认人，任意一项对上就算同一个节点。</summary>
    private bool SamePeer(PeerModel other)
    {
        if (!string.IsNullOrWhiteSpace(_peer.DnsName) &&
            string.Equals(other.DnsName, _peer.DnsName, StringComparison.OrdinalIgnoreCase))
            return true;

        if (!string.IsNullOrWhiteSpace(_peer.HostName) &&
            string.Equals(other.HostName, _peer.HostName, StringComparison.OrdinalIgnoreCase))
            return true;

        return !string.IsNullOrWhiteSpace(_peer.PrimaryIp) && other.Ips.Contains(_peer.PrimaryIp);
    }

    private string LatencyText() =>
        _peer.Latency == null
            ? "尚未测量"
            : $"{_peer.LatencyText}（{_peer.Latency.At:HH:mm:ss} 测得，最近一次）";

    /// <summary>Ping 完成后把上面两行刷新掉，别让信息区还挂着旧值。</summary>
    private void UpdateInfoRows()
    {
        _valLatency.Text = LatencyText();
        _valConnection.Text = _peer.ConnectionText;
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

        // 顺手把这次结果记为最新的延迟数据，并通知设备列表刷新这一列
        if (result.Code == 0 && !string.IsNullOrWhiteSpace(text))
        {
            _peer.Latency = PingOutputParser.Parse(text);
            UpdateInfoRows();
            _latencyUpdated?.Invoke();
        }

        _btnPing.Enabled = true;
    }

    private static readonly Color DropNormal = Color.FromArgb(240, 253, 248);
    private static readonly Color DropActive = Color.FromArgb(204, 251, 241);

    private void SetDropHighlight(bool active) =>
        _dropZone.BackColor = active ? DropActive : DropNormal;

    private void OnDropEnter(object sender, DragEventArgs e)
    {
        // 只接受单个文件：多选或文件夹不给"可放下"的光标
        var isFile = FileDropHelper.IsSingleFile(e.Data);
        e.Effect = isFile ? DragDropEffects.Copy : DragDropEffects.None;
        SetDropHighlight(isFile);
    }

    private async Task OnDropAsync(DragEventArgs e)
    {
        SetDropHighlight(false);

        if (e.Data?.GetData(DataFormats.FileDrop) is not string[] dropped)
            return;

        if (!_peer.Online)
        {
            _output.Text = "这台设备当前离线，无法接收文件。";
            return;
        }

        if (!FileDropHelper.TryGetSingleFile(dropped, out var file, out var error))
        {
            _output.Text = error;
            return;
        }

        var answer = MessageBox.Show(this,
            $"用 Taildrop 把这个文件发送到「{_peer.DisplayName}」？{Environment.NewLine}{Environment.NewLine}" +
            System.IO.Path.GetFileName(file),
            "发送文件", MessageBoxButtons.YesNo, MessageBoxIcon.Question);

        if (answer != DialogResult.Yes)
            return;

        await SendFileAsync(file);
    }

    private async Task SendFileAsync(string file)
    {
        var device = string.IsNullOrWhiteSpace(_peer.PrimaryIp) ? _peer.DisplayName : _peer.PrimaryIp;
        var name = System.IO.Path.GetFileName(file);

        _output.Text = $"正在向 {_peer.DisplayName} 发送：{name} …";

        var result = await Task.Run(() => _client.SendFile(file, device));

        if (IsDisposed)
            return;

        if (result.Code == 0)
        {
            _output.Text = $"已发送：{name}";
            return;
        }

        var detail = !string.IsNullOrWhiteSpace(result.Err) ? result.Err : result.Out;
        _output.Text = $"发送失败：{name}" + Environment.NewLine + Environment.NewLine + detail.Trim();
    }
}
