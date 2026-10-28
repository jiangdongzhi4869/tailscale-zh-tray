using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace TailscaleZHTray;

internal sealed class MainForm : Form
{
    private readonly TrayAppContext _ctx;

    private readonly Label _lblTitle = new();
    private readonly Label _lblVersion = new();
    private readonly Label _lblState = new();
    private readonly Label _lblSelf = new();
    private readonly Label _lblExit = new();
    private readonly LinkLabel _lnkAuth = new();

    private readonly TabControl _tabs = new();

    private readonly Button _btnToggle = new();
    private readonly Button _btnRefresh = new();
    private readonly Button _btnExitNode = new();
    private readonly Button _btnAdmin = new();
    private readonly Label _lblSummary = new();

    private readonly ListView _list = new();
    private readonly Button _btnPeerDetail = new();
    private readonly Button _btnPeerRefresh = new();

    private readonly Dictionary<string, CheckBox> _settingChecks = new();
    private readonly Button _btnReset = new();

    private readonly Button _btnNetcheck = new();
    private readonly Button _btnSendFile = new();
    private readonly Button _btnReceiveFile = new();
    private readonly Button _btnAccounts = new();
    private readonly Button _btnLogout = new();
    private readonly Button _btnOpenLog = new();
    private readonly TextBox _txtTools = new();

    private readonly ToolStripStatusLabel _statusLabel = new();

    private TailscaleSnapshot _snap;
    private string _authUrl = "";
    private bool _updatingSettings;
    private bool _netchecking;

    public MainForm(TrayAppContext ctx)
    {
        _ctx = ctx;

        Text = "Tailscale 中文控制台";
        ClientSize = new Size(940, 700);
        MinimumSize = new Size(820, 600);
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Microsoft YaHei UI", 9F);
        Icon = ctx.AppIcon;
        BackColor = Color.FromArgb(247, 248, 250);

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(0),
            BackColor = Color.FromArgb(247, 248, 250)
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 158));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));
        Controls.Add(root);

        root.Controls.Add(BuildHeader(), 0, 0);

        _tabs.Dock = DockStyle.Fill;
        _tabs.TabPages.Add(BuildStatusTab());
        _tabs.TabPages.Add(BuildDevicesTab());
        _tabs.TabPages.Add(BuildSettingsTab());
        _tabs.TabPages.Add(BuildToolsTab());
        root.Controls.Add(_tabs, 0, 1);

        root.Controls.Add(BuildStatusStrip(), 0, 2);
    }

    private Control BuildHeader()
    {
        var header = new Panel { Dock = DockStyle.Fill, BackColor = Color.White };

        _lblTitle.Text = "Tailscale 中文控制台";
        _lblTitle.Font = new Font("Microsoft YaHei UI", 17F, FontStyle.Bold);
        _lblTitle.ForeColor = Color.FromArgb(17, 24, 39);
        _lblTitle.AutoSize = true;
        _lblTitle.Location = new Point(20, 12);
        header.Controls.Add(_lblTitle);

        _lblVersion.AutoSize = false;
        _lblVersion.Size = new Size(340, 24);
        _lblVersion.Location = new Point(580, 16);
        _lblVersion.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        _lblVersion.TextAlign = ContentAlignment.MiddleRight;
        _lblVersion.ForeColor = Color.Gray;
        header.Controls.Add(_lblVersion);

        _lblState.AutoSize = true;
        _lblState.Font = new Font("Microsoft YaHei UI", 14F, FontStyle.Bold);
        _lblState.ForeColor = Color.FromArgb(107, 114, 128);
        _lblState.Location = new Point(20, 50);
        header.Controls.Add(_lblState);

        _lblSelf.AutoSize = true;
        _lblSelf.ForeColor = Color.FromArgb(55, 65, 81);
        _lblSelf.Location = new Point(20, 88);
        header.Controls.Add(_lblSelf);

        _lblExit.AutoSize = true;
        _lblExit.ForeColor = Color.FromArgb(55, 65, 81);
        _lblExit.Location = new Point(20, 112);
        header.Controls.Add(_lblExit);

        _lnkAuth.AutoSize = true;
        _lnkAuth.Text = "→ 打开登录授权页";
        _lnkAuth.LinkColor = Color.FromArgb(16, 163, 127);
        _lnkAuth.ActiveLinkColor = Color.FromArgb(13, 130, 101);
        _lnkAuth.Location = new Point(20, 134);
        _lnkAuth.Visible = false;
        _lnkAuth.LinkClicked += (_, _) =>
        {
            if (!string.IsNullOrWhiteSpace(_authUrl))
                TrayAppContext.OpenUrl(_authUrl);
        };
        header.Controls.Add(_lnkAuth);

        return header;
    }

    private TabPage BuildStatusTab()
    {
        var page = new TabPage("状态") { BackColor = Color.FromArgb(247, 248, 250) };

        var table = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Padding = new Padding(0)
        };
        table.RowStyles.Add(new RowStyle(SizeType.Absolute, 54));
        table.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var bar = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Padding = new Padding(16, 11, 16, 6)
        };

        _btnToggle.Text = "连接";
        _btnToggle.Width = 130;
        StyleButton(_btnToggle, primary: true);
        _btnToggle.Click += async (_, _) => await _ctx.ToggleAsync();

        _btnRefresh.Text = "刷新";
        _btnRefresh.Width = 90;
        StyleButton(_btnRefresh, primary: false);
        _btnRefresh.Click += async (_, _) => await _ctx.RefreshAsync(withLatency: true);

        _btnExitNode.Text = "出口节点";
        _btnExitNode.Width = 120;
        StyleButton(_btnExitNode, primary: false);
        _btnExitNode.Click += (_, _) => _ctx.ShowExitNodeMenu(_btnExitNode);

        _btnAdmin.Text = "管理后台";
        _btnAdmin.Width = 120;
        StyleButton(_btnAdmin, primary: false);
        _btnAdmin.Click += (_, _) =>
            TrayAppContext.OpenUrl("https://login.tailscale.com/admin/machines");

        bar.Controls.Add(_btnToggle);
        bar.Controls.Add(_btnRefresh);
        bar.Controls.Add(_btnExitNode);
        bar.Controls.Add(_btnAdmin);

        _lblSummary.AutoSize = false;
        _lblSummary.Dock = DockStyle.Fill;
        _lblSummary.Padding = new Padding(20, 14, 20, 14);
        _lblSummary.Font = new Font("Microsoft YaHei UI", 10.5F);
        _lblSummary.ForeColor = Color.FromArgb(55, 65, 81);
        _lblSummary.Text = "正在读取状态…";

        table.Controls.Add(bar, 0, 0);
        table.Controls.Add(_lblSummary, 0, 1);
        page.Controls.Add(table);
        return page;
    }

    private TabPage BuildDevicesTab()
    {
        var page = new TabPage("设备") { BackColor = Color.FromArgb(247, 248, 250) };

        var table = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Padding = new Padding(0)
        };
        table.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        table.RowStyles.Add(new RowStyle(SizeType.Absolute, 50));

        var listPanel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(16, 12, 16, 0) };

        _list.Dock = DockStyle.Fill;
        _list.View = View.Details;
        _list.FullRowSelect = true;
        _list.HideSelection = false;
        _list.MultiSelect = false;
        _list.BorderStyle = BorderStyle.FixedSingle;
        _list.BackColor = Color.White;
        _list.Columns.Add("设备", 260);
        _list.Columns.Add("地址", 165);
        _list.Columns.Add("系统", 95);
        _list.Columns.Add("状态", 70);
        _list.Columns.Add("连接方式", 150);
        _list.Columns.Add("延迟", 90);
        _list.DoubleClick += (_, _) => OpenSelectedPeer();
        _list.MouseUp += OnListMouseUp;

        // 拖文件到设备行 = 直接用 Taildrop 发送
        _list.AllowDrop = true;
        _list.DragEnter += OnListDragOver;
        _list.DragOver += OnListDragOver;
        _list.DragDrop += async (_, e) => await OnListDragDropAsync(e);

        listPanel.Controls.Add(_list);

        var bar = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(16, 8, 16, 6),
            WrapContents = false
        };

        _btnPeerDetail.Text = "查看详情 / Ping";
        _btnPeerDetail.Width = 150;
        StyleButton(_btnPeerDetail, primary: true);
        _btnPeerDetail.Click += (_, _) => OpenSelectedPeer();

        _btnPeerRefresh.Text = "刷新";
        _btnPeerRefresh.Width = 90;
        StyleButton(_btnPeerRefresh, primary: false);
        _btnPeerRefresh.Click += async (_, _) => await _ctx.RefreshAsync(withLatency: true);

        bar.Controls.Add(_btnPeerDetail);
        bar.Controls.Add(_btnPeerRefresh);

        table.Controls.Add(listPanel, 0, 0);
        table.Controls.Add(bar, 0, 1);
        page.Controls.Add(table);
        return page;
    }

    private TabPage BuildSettingsTab()
    {
        var page = new TabPage("设置") { BackColor = Color.FromArgb(247, 248, 250), AutoScroll = true };

        var y = 18;
        foreach (var spec in SettingsCatalog.All)
        {
            var check = new CheckBox
            {
                AutoSize = true,
                Location = new Point(20, y),
                Font = new Font("Microsoft YaHei UI", 10F),
                Text = spec.Label,
                Tag = spec
            };
            check.CheckedChanged += async (_, _) => await OnSettingChangedAsync(check);

            page.Controls.Add(check);
            _settingChecks[spec.Id] = check;
            y += 34;
        }

        y += 8;

        _btnReset.Text = "恢复默认设置…";
        _btnReset.Location = new Point(20, y);
        _btnReset.Size = new Size(160, 32);
        _btnReset.FlatStyle = FlatStyle.Flat;
        _btnReset.FlatAppearance.BorderSize = 1;
        _btnReset.FlatAppearance.BorderColor = Color.FromArgb(220, 38, 38);
        _btnReset.BackColor = Color.White;
        _btnReset.ForeColor = Color.FromArgb(185, 28, 28);
        _btnReset.Cursor = Cursors.Hand;
        _btnReset.Click += async (_, _) => await _ctx.ResetDefaultsAsync();
        page.Controls.Add(_btnReset);

        var hint = new Label
        {
            AutoSize = false,
            Location = new Point(20, y + 46),
            Size = new Size(860, 90),
            ForeColor = Color.Gray,
            Text =
                "说明：\n" +
                "· 「允许传入连接」是 Shields Up 的反向：取消勾选等于开启 Shields Up，拒绝所有入站连接。\n" +
                "· 「允许访问本地网络」只在走出口节点时生效。\n" +
                "· 「恢复默认设置」使用 tailscale up --reset，会重置未指定的设置并重新连接，请谨慎使用。"
        };
        page.Controls.Add(hint);

        return page;
    }

    private TabPage BuildToolsTab()
    {
        var page = new TabPage("工具") { BackColor = Color.FromArgb(247, 248, 250) };

        var table = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Padding = new Padding(0)
        };
        table.RowStyles.Add(new RowStyle(SizeType.Absolute, 88));
        table.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var bar = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(16, 10, 16, 6),
            WrapContents = true
        };

        _btnNetcheck.Text = "网络检查";
        _btnNetcheck.Width = 110;
        StyleButton(_btnNetcheck, primary: true);
        _btnNetcheck.Click += async (_, _) => await RunNetcheckAsync();

        _btnSendFile.Text = "发送文件…";
        _btnSendFile.Width = 110;
        StyleButton(_btnSendFile, primary: false);
        _btnSendFile.Click += async (_, _) => await StartSendFileAsync();

        _btnReceiveFile.Text = "接收文件…";
        _btnReceiveFile.Width = 110;
        StyleButton(_btnReceiveFile, primary: false);
        _btnReceiveFile.Click += async (_, _) => await StartReceiveFilesAsync();

        _btnAccounts.Text = "账号…";
        _btnAccounts.Width = 90;
        StyleButton(_btnAccounts, primary: false);
        _btnAccounts.Click += (_, _) => ShowAccounts();

        _btnLogout.Text = "退出登录";
        _btnLogout.Width = 100;
        StyleButton(_btnLogout, primary: false);
        _btnLogout.ForeColor = Color.FromArgb(185, 28, 28);
        _btnLogout.Click += async (_, _) => await _ctx.LogoutAsync();

        _btnOpenLog.Text = "打开日志目录";
        _btnOpenLog.Width = 120;
        StyleButton(_btnOpenLog, primary: false);
        _btnOpenLog.Click += (_, _) => OpenLogFolder();

        bar.Controls.Add(_btnNetcheck);
        bar.Controls.Add(_btnSendFile);
        bar.Controls.Add(_btnReceiveFile);
        bar.Controls.Add(_btnAccounts);
        bar.Controls.Add(_btnLogout);
        bar.Controls.Add(_btnOpenLog);

        var textPanel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(16, 0, 16, 14) };

        _txtTools.Dock = DockStyle.Fill;
        _txtTools.Multiline = true;
        _txtTools.ReadOnly = true;
        _txtTools.ScrollBars = ScrollBars.Vertical;
        _txtTools.WordWrap = true;
        _txtTools.BackColor = Color.FromArgb(30, 32, 36);
        _txtTools.ForeColor = Color.FromArgb(220, 223, 228);
        _txtTools.Font = new Font("Consolas", 9.5F);
        _txtTools.Text =
            "工具说明：" + Environment.NewLine +
            "· 网络检查：查看 NAT / UDP 可用性和各 DERP 区域延迟。" + Environment.NewLine +
            "· 发送文件：用 Taildrop 把文件发给在线设备。" + Environment.NewLine +
            "· 接收文件：把 Taildrop 收件箱里的文件移动到指定文件夹。" + Environment.NewLine +
            "· 账号：查看 / 切换已登录账号，或添加新账号。" + Environment.NewLine +
            "· 退出登录：断开并作废当前节点密钥，下次需要重新授权。";

        textPanel.Controls.Add(_txtTools);

        table.Controls.Add(bar, 0, 0);
        table.Controls.Add(textPanel, 0, 1);
        page.Controls.Add(table);
        return page;
    }

    private Control BuildStatusStrip()
    {
        var strip = new StatusStrip
        {
            Dock = DockStyle.Fill,
            SizingGrip = false,
            BackColor = Color.FromArgb(247, 248, 250)
        };

        _statusLabel.Text = "正在读取状态…";
        _statusLabel.ForeColor = Color.Gray;
        strip.Items.Add(_statusLabel);
        return strip;
    }

    private static void StyleButton(Button button, bool primary)
    {
        button.Height = 32;
        button.FlatStyle = FlatStyle.Flat;
        button.FlatAppearance.BorderSize = primary ? 0 : 1;
        button.FlatAppearance.BorderColor = Color.FromArgb(209, 213, 219);
        button.BackColor = primary ? Color.FromArgb(16, 163, 127) : Color.White;
        button.ForeColor = primary ? Color.White : Color.FromArgb(31, 41, 55);
        button.Font = new Font("Microsoft YaHei UI", 9.5F, primary ? FontStyle.Bold : FontStyle.Regular);
        button.Cursor = Cursors.Hand;
        button.Margin = new Padding(0, 0, 10, 0);
    }

    public void UpdateView(TailscaleSnapshot snapshot, bool available)
    {
        if (IsDisposed)
            return;

        if (InvokeRequired)
        {
            BeginInvoke(new Action(() => UpdateView(snapshot, available)));
            return;
        }

        var (stateText, stateColor, _) = TrayAppContext.StateInfo(snapshot, available);
        _lblState.Text = stateText;
        _lblState.ForeColor = stateColor;

        if (snapshot == null)
            return;

        _snap = snapshot;

        var host = string.IsNullOrWhiteSpace(snapshot.HostName) ? "—" : snapshot.HostName;
        var ips = snapshot.SelfIps.Count > 0 ? string.Join("  /  ", snapshot.SelfIps) : "尚未分配";
        _lblSelf.Text = $"本机：{host}        地址：{ips}";

        _lblExit.Text = "出口节点：" +
                        (string.IsNullOrWhiteSpace(snapshot.ExitNodeName) ? "未使用" : snapshot.ExitNodeName);

        _lblVersion.Text = string.IsNullOrWhiteSpace(snapshot.Version)
            ? ""
            : "客户端 " + snapshot.Version;

        _authUrl = snapshot.AuthUrl ?? "";
        _lnkAuth.Visible = !string.IsNullOrWhiteSpace(_authUrl);

        _btnToggle.Text = snapshot.IsRunning
            ? "断开连接"
            : TrayAppContext.NeedsLogin(snapshot) ? "登录并连接" : "连接";

        var onlineCount = snapshot.Peers.Count(p => p.Online);
        var health = snapshot.Health
            .Where(h => !string.IsNullOrWhiteSpace(h))
            .Select(TrayAppContext.TranslateHealth)
            .Distinct()
            .ToList();

        _lblSummary.Text =
            $"后端状态：{stateText}" + Environment.NewLine + Environment.NewLine +
            $"对端设备：{snapshot.Peers.Count} 台（在线 {onlineCount}）" + Environment.NewLine +
            $"当前出口节点：{(string.IsNullOrWhiteSpace(snapshot.ExitNodeName) ? "未使用" : snapshot.ExitNodeName)}" + Environment.NewLine +
            $"健康提示：{(health.Count == 0 ? "无" : string.Join("；", health))}" + Environment.NewLine + Environment.NewLine +
            "提示：鼠标悬停托盘图标可看当前状态；右键托盘图标可快速连接、切换出口节点。";

        FillDeviceList(snapshot);
        UpdateSettings(snapshot);

        ShowRefreshStatus();
    }

    /// <summary>状态栏的「已完成刷新」文字。没有定时刷新，全靠用户点。</summary>
    public void ShowRefreshStatus()
    {
        if (IsDisposed)
            return;

        if (InvokeRequired)
        {
            BeginInvoke(new Action(ShowRefreshStatus));
            return;
        }

        _statusLabel.Text =
            $"上次刷新：{DateTime.Now:HH:mm:ss}    点「刷新」更新状态与延迟" +
            $"（逐台测，每台间隔 {TrayAppContext.LatencyStepDelayMs / 1000} 秒）";
    }

    /// <summary>状态栏临时文字（例如正在测延迟）。</summary>
    public void SetStatus(string text)
    {
        if (IsDisposed)
            return;

        if (InvokeRequired)
        {
            BeginInvoke(new Action<string>(SetStatus), text);
            return;
        }

        _statusLabel.Text = text;
    }

    private void FillDeviceList(TailscaleSnapshot snapshot)
    {
        _list.BeginUpdate();
        try
        {
            // 刷新后把选中行和滚动位置还原，否则看一眼详情再刷新就丢了
            var selectedIp = (_list.SelectedItems.Count > 0
                ? (_list.SelectedItems[0].Tag as PeerModel)?.PrimaryIp
                : null);
            var topIndex = _list.Items.Count > 0 ? _list.TopItem?.Index ?? -1 : -1;

            _list.Items.Clear();

            if (snapshot.Peers.Count == 0)
            {
                var empty = new ListViewItem(
                    TrayAppContext.NeedsLogin(snapshot)
                        ? "（尚未登录，登录后这里会显示你的设备）"
                        : "（当前没有对端设备）")
                {
                    ForeColor = Color.Silver
                };
                _list.Items.Add(empty);
                return;
            }

            foreach (var peer in snapshot.Peers
                         .OrderByDescending(p => p.Online)
                         .ThenBy(p => p.DisplayName, StringComparer.CurrentCultureIgnoreCase))
            {
                var name = string.IsNullOrWhiteSpace(peer.DisplayName) ? "(未命名设备)" : peer.DisplayName;
                var item = new ListViewItem(name) { Tag = peer };
                item.SubItems.Add(peer.PrimaryIp);
                item.SubItems.Add(OsName(peer.Os));
                item.SubItems.Add(peer.Online ? "在线" : "离线");
                item.SubItems.Add(peer.ConnectionText);
                item.SubItems.Add(peer.Online ? peer.LatencyText : "");
                item.ForeColor = peer.Online ? Color.FromArgb(31, 41, 55) : Color.Silver;
                _list.Items.Add(item);
            }

            RestoreSelection(selectedIp, topIndex);
        }
        finally
        {
            _list.EndUpdate();
        }
    }

    private void RestoreSelection(string selectedIp, int topIndex)
    {
        if (!string.IsNullOrEmpty(selectedIp))
        {
            foreach (ListViewItem item in _list.Items)
            {
                if (item.Tag is PeerModel peer && peer.PrimaryIp == selectedIp)
                {
                    item.Selected = true;
                    item.Focused = true;
                    break;
                }
            }
        }

        if (topIndex > 0 && topIndex < _list.Items.Count)
        {
            try { _list.TopItem = _list.Items[topIndex]; }
            catch { }
        }
    }

    /// <summary>窗体或输出框已销毁时，异步回来的结果就不要写回去了。</summary>
    private bool UiAlive => !IsDisposed && !_txtTools.IsDisposed && _txtTools.IsHandleCreated;

    private readonly ContextMenuStrip _peerMenu = new();
    private PeerModel _peerMenuTarget;
    private ToolStripMenuItem _miCopyPeerIp;
    private ToolStripMenuItem _miSendToPeer;
    private ToolStripMenuItem _miPeerExitNode;

    private void OnListMouseUp(object sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Right)
            return;

        var hit = _list.HitTest(e.Location);
        if (hit.Item?.Tag is not PeerModel peer)
            return;

        hit.Item.Selected = true;
        hit.Item.Focused = true;
        _peerMenuTarget = peer;

        BuildPeerMenuOnce();

        var hasIp = !string.IsNullOrWhiteSpace(peer.PrimaryIp);
        _miCopyPeerIp.Enabled = hasIp;
        _miSendToPeer.Enabled = hasIp && peer.Online;
        _miPeerExitNode.Enabled = peer.ExitNodeOption && hasIp;
        _miPeerExitNode.Text = peer.ExitNode ? "取消出口节点" : "设为出口节点";

        _peerMenu.Show(_list, e.Location);
    }

    /// <summary>菜单只建一次，之后靠 Enabled/Text 切换，避免重复创建和释放。</summary>
    private void BuildPeerMenuOnce()
    {
        if (_peerMenu.Items.Count > 0)
            return;

        _miCopyPeerIp = new ToolStripMenuItem("复制 IP");
        _miCopyPeerIp.Click += (_, _) => CopyPeerIp();

        _miSendToPeer = new ToolStripMenuItem("发送文件到这台…");
        _miSendToPeer.Click += async (_, _) => await SendFileToPeerAsync();

        var miDetail = new ToolStripMenuItem("查看详情 / Ping");
        miDetail.Click += (_, _) => OpenSelectedPeer();

        _miPeerExitNode = new ToolStripMenuItem("设为出口节点");
        _miPeerExitNode.Click += async (_, _) => await ToggleExitNodeForPeerAsync();

        _peerMenu.Items.Add(_miCopyPeerIp);
        _peerMenu.Items.Add(_miSendToPeer);
        _peerMenu.Items.Add(miDetail);
        _peerMenu.Items.Add(new ToolStripSeparator());
        _peerMenu.Items.Add(_miPeerExitNode);
    }

    private void CopyPeerIp()
    {
        var ip = _peerMenuTarget?.PrimaryIp;
        if (string.IsNullOrWhiteSpace(ip))
            return;

        try
        {
            Clipboard.SetText(ip);
        }
        catch (Exception ex)
        {
            Logger.Log("复制对端 IP 失败: " + ex.Message);
        }
    }

    private async Task SendFileToPeerAsync()
    {
        var peer = _peerMenuTarget;
        if (peer == null)
            return;

        if (!peer.Online)
        {
            MessageBox.Show(this, "这台设备当前离线，无法接收文件。", "发送文件",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        using var dialog = new OpenFileDialog
        {
            Title = $"发送文件到「{peer.DisplayName}」",
            Multiselect = true,
            CheckFileExists = true
        };

        if (dialog.ShowDialog(this) != DialogResult.OK)
            return;

        await SendFilesToAsync(peer, dialog.FileNames);
    }

    private async Task ToggleExitNodeForPeerAsync()
    {
        var peer = _peerMenuTarget;
        if (peer == null)
            return;

        await _ctx.SetExitNodeAsync(peer.ExitNode ? "" : peer.PrimaryIp);
    }

    private const int ColConnection = 4;
    private const int ColLatency = 5;

    private PeerModel PeerAtPoint(int x, int y) =>
        _list.HitTest(x, y).Item?.Tag as PeerModel;

    private void OnListDragOver(object sender, DragEventArgs e)
    {
        var peer = PeerAtPoint(e.X, e.Y);
        var fileDrop = e.Data != null && e.Data.GetDataPresent(DataFormats.FileDrop);

        e.Effect = fileDrop && peer != null && peer.Online
            ? DragDropEffects.Copy
            : DragDropEffects.None;
    }

    private async Task OnListDragDropAsync(DragEventArgs e)
    {
        var peer = PeerAtPoint(e.X, e.Y);
        if (peer == null || e.Data == null)
            return;

        if (!peer.Online)
        {
            MessageBox.Show(this, "这台设备当前离线，无法接收文件。", "发送文件",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        if (e.Data.GetData(DataFormats.FileDrop) is not string[] dropped || dropped.Length == 0)
            return;

        var files = ExpandDroppedPaths(dropped);
        if (files.Count == 0)
        {
            MessageBox.Show(this, "拖进来的内容里没有可发送的文件。", "发送文件",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        if (files.Count > 500)
        {
            MessageBox.Show(this,
                $"一次最多发送 500 个文件，这次是 {files.Count} 个。请分批拖入。",
                "发送文件", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var preview = string.Join(Environment.NewLine, files.Take(8).Select(System.IO.Path.GetFileName));
        if (files.Count > 8)
            preview += $"{Environment.NewLine}…以及另外 {files.Count - 8} 个文件";

        var answer = MessageBox.Show(this,
            $"用 Taildrop 把这些文件发送到「{peer.DisplayName}」？{Environment.NewLine}{Environment.NewLine}" +
            $"共 {files.Count} 个文件：{Environment.NewLine}{preview}",
            "发送文件", MessageBoxButtons.YesNo, MessageBoxIcon.Question);

        if (answer != DialogResult.Yes)
            return;

        try
        {
            await SendFilesToAsync(peer, files);
        }
        catch (Exception ex)
        {
            Logger.Log("拖放发送失败: " + ex);
            MessageBox.Show(this, "发送失败：" + ex.Message, "发送文件",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private static List<string> ExpandDroppedPaths(IEnumerable<string> paths)
    {
        var files = new List<string>();
        foreach (var path in paths)
        {
            try
            {
                if (System.IO.Directory.Exists(path))
                    files.AddRange(System.IO.Directory.GetFiles(path, "*", System.IO.SearchOption.AllDirectories));
                else if (System.IO.File.Exists(path))
                    files.Add(path);
            }
            catch (Exception ex)
            {
                Logger.Log($"展开拖放路径失败: {path} => {ex.Message}");
            }
        }
        return files;
    }

    /// <summary>设备列表的「连接方式」「延迟」两列局部刷新，不重绘整个列表。</summary>
    public void RefreshLatencyColumn()
    {
        if (IsDisposed)
            return;

        if (InvokeRequired)
        {
            BeginInvoke(new Action(RefreshLatencyColumn));
            return;
        }

        _list.BeginUpdate();
        try
        {
            foreach (ListViewItem item in _list.Items)
            {
                if (item.Tag is not PeerModel peer)
                    continue;

                if (item.SubItems.Count > ColConnection)
                    item.SubItems[ColConnection].Text = peer.Online ? peer.ConnectionText : "";

                if (item.SubItems.Count <= ColLatency)
                    continue;

                var sub = item.SubItems[ColLatency];
                sub.Text = peer.Online ? peer.LatencyText : "";
                sub.ForeColor = !peer.Online ? Color.Silver
                    : peer.Latency == null ? Color.Gray
                    : peer.Latency.TimedOut ? Color.FromArgb(185, 28, 28)
                    : peer.Latency.Relay ? Color.FromArgb(217, 119, 6)
                    : Color.FromArgb(16, 163, 127);
            }
        }
        finally
        {
            _list.EndUpdate();
        }
    }

    private void UpdateSettings(TailscaleSnapshot snapshot)
    {
        _updatingSettings = true;
        foreach (var spec in SettingsCatalog.All)
        {
            if (_settingChecks.TryGetValue(spec.Id, out var check))
                check.Checked = spec.Get(snapshot);
        }
        _updatingSettings = false;
    }

    private async Task OnSettingChangedAsync(CheckBox check)
    {
        if (_updatingSettings)
            return;

        if (check.Tag is not ToggleSpec spec)
            return;

        var want = check.Checked;
        var confirm = spec.Confirm?.Invoke(want);

        if (!string.IsNullOrEmpty(confirm) &&
            MessageBox.Show(this, confirm, "修改设置",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
        {
            SetCheck(spec, !want);
            return;
        }

        var ok = await _ctx.ApplySettingAsync(spec, want);
        if (!ok)
            SetCheck(spec, !want);
    }

    private void SetCheck(ToggleSpec spec, bool value)
    {
        if (!_settingChecks.TryGetValue(spec.Id, out var check))
            return;

        _updatingSettings = true;
        check.Checked = value;
        _updatingSettings = false;
    }

    public async Task RunNetcheckAsync()
    {
        if (_netchecking)
            return;

        _netchecking = true;
        _btnNetcheck.Enabled = false;
        _btnNetcheck.Text = "检查中…";
        _txtTools.Text = "正在运行网络检查（通常 5~15 秒）…";
        Logger.Log($"网络检查: 开始 uiThread={!InvokeRequired} sync={SynchronizationContext.Current != null}");

        try
        {
            var result = await Task.Run(() => _ctx.Client.Netcheck());

            Logger.Log($"网络检查: 结束 code={result.Code} out={result.Out?.Length ?? 0} err={(result.Err ?? "").Length}");

            if (result.Code != 0)
            {
                var detail = !string.IsNullOrWhiteSpace(result.Err) ? result.Err : result.Out;
                _txtTools.Text = "网络检查失败：" + Environment.NewLine + Environment.NewLine + detail;
                return;
            }

            _txtTools.Text = FormatNetcheck(result.Out);
            Logger.Log($"网络检查: 已写入文本框 len={_txtTools.Text?.Length ?? -1} visible={_txtTools.Visible} hwnd={_txtTools.Handle}");
        }
        finally
        {
            _netchecking = false;
            _btnNetcheck.Enabled = true;
            _btnNetcheck.Text = "网络检查";
        }
    }

    public async Task StartSendFileAsync()
    {
        var peers = _snap?.Peers.Where(p => p.Online).ToList() ?? new List<PeerModel>();
        if (peers.Count == 0)
        {
            MessageBox.Show(this, "当前没有在线设备，无法使用 Taildrop 发送文件。", "发送文件",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        using var dialog = new OpenFileDialog
        {
            Title = "选择要发送的文件",
            Multiselect = true,
            CheckFileExists = true
        };

        if (dialog.ShowDialog(this) != DialogResult.OK)
            return;

        PeerModel target;
        if (peers.Count == 1)
        {
            target = peers[0];
        }
        else
        {
            using var picker = new PickPeerForm(peers);
            if (picker.ShowDialog(this) != DialogResult.OK || picker.SelectedPeer == null)
                return;
            target = picker.SelectedPeer;
        }

        SelectToolsTab();
        await SendFilesToAsync(target, dialog.FileNames);
    }

    /// <summary>把一批文件发到指定设备，结果显示在工具页输出框。</summary>
    private async Task SendFilesToAsync(PeerModel target, IReadOnlyList<string> files)
    {
        var device = string.IsNullOrWhiteSpace(target.PrimaryIp) ? target.DisplayName : target.PrimaryIp;

        SelectToolsTab();
        if (!UiAlive)
            return;

        _txtTools.Text = $"正在向 {target.DisplayName} 发送 {files.Count} 个文件…";

        var lines = new List<string>();
        var succeeded = 0;

        foreach (var file in files)
        {
            var result = await Task.Run(() => _ctx.Client.SendFile(file, device));

            // 传输过程中窗口可能已经被关掉，别再去碰已销毁的控件
            if (!UiAlive)
                return;

            var name = System.IO.Path.GetFileName(file);

            if (result.Code == 0)
            {
                succeeded++;
                lines.Add($"[成功] {name}");
            }
            else
            {
                var detail = !string.IsNullOrWhiteSpace(result.Err) ? result.Err : result.Out;
                lines.Add($"[失败] {name}{Environment.NewLine}{detail.Trim()}");
            }
        }

        if (!UiAlive)
            return;

        _txtTools.Text =
            $"目标设备：{target.DisplayName}（{device}）{Environment.NewLine}" +
            $"成功 {succeeded} / {files.Count}" + Environment.NewLine + Environment.NewLine +
            string.Join(Environment.NewLine + Environment.NewLine, lines);
    }

    public async Task StartReceiveFilesAsync()
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = "选择接收文件保存到的文件夹"
        };

        if (dialog.ShowDialog(this) != DialogResult.OK)
            return;

        _txtTools.Text = "正在接收 Taildrop 文件到：" + Environment.NewLine + dialog.SelectedPath;

        var result = await Task.Run(() => _ctx.Client.ReceiveFiles(dialog.SelectedPath));

        var detail = (result.Code == 0 ? result.Out : result.Err) ?? "";
        if (!UiAlive)
            return;

        _txtTools.Text =
            $"接收目录：{dialog.SelectedPath}" + Environment.NewLine +
            $"退出码：{result.Code}" + Environment.NewLine + Environment.NewLine +
            (string.IsNullOrWhiteSpace(detail) ? "（没有输出，可能收件箱为空）" : detail.Trim());
    }

    public void ShowAccounts()
    {
        using var dialog = new AccountForm(_ctx.Client);
        dialog.ShowDialog(this);
        _ = _ctx.RefreshAsync();
    }

    private static string FormatNetcheck(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return "（没有返回内容）";

        var start = raw.IndexOf('{');
        if (start < 0)
            return raw;

        try
        {
            using var doc = JsonDocument.Parse(raw.Substring(start));
            var root = doc.RootElement;

            bool Flag(string name) =>
                root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True;

            var lines = new List<string>
            {
                "== Tailscale 网络检查 ==",
                "",
                "UDP 直连可用：" + Yn(Flag("UDP")),
                "IPv4 可用：" + Yn(Flag("IPv4")),
                "IPv6 可用：" + Yn(Flag("IPv6")),
                "UDP IPv4 可发送：" + Yn(Flag("IPv4CanSend")),
                "UDP IPv6 可发送：" + Yn(Flag("IPv6CanSend")),
                "UPnP：" + Yn(Flag("UPnP")) +
                "    NAT-PMP：" + Yn(Flag("PMP")) +
                "    PCP：" + Yn(Flag("PCP")),
            };

            if (root.TryGetProperty("PreferredDERP", out var preferred) &&
                preferred.ValueKind == JsonValueKind.Number)
            {
                lines.Add("首选 DERP 区域：" + preferred.GetInt32());
            }

            if (root.TryGetProperty("RegionLatency", out var latency) &&
                latency.ValueKind == JsonValueKind.Object)
            {
                var items = new List<(string id, double ms)>();
                foreach (var property in latency.EnumerateObject())
                {
                    if (property.Value.ValueKind == JsonValueKind.Number &&
                        property.Value.TryGetDouble(out var ns))
                    {
                        items.Add((property.Name, ns / 1_000_000.0));
                    }
                }

                lines.Add("");
                lines.Add("各 DERP 区域延迟（毫秒，越小越好）：");
                foreach (var (id, ms) in items.OrderBy(x => x.ms).Take(10))
                {
                    lines.Add($"  区域 {id,-4} {ms,8:0.0} ms");
                }
            }

            lines.Add("");
            lines.Add("—— 以下是原始 JSON ——");
            lines.Add(raw);
            return string.Join(Environment.NewLine, lines);
        }
        catch
        {
            return raw;
        }
    }

    private static string Yn(bool value) => value ? "是" : "否";

    private void OpenSelectedPeer()
    {
        if (_list.SelectedItems.Count == 0)
        {
            MessageBox.Show(this, "先在列表里选中一台设备。", "设备详情",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        if (_list.SelectedItems[0].Tag is not PeerModel peer)
            return;

        using var dialog = new PeerDetailForm(_ctx.Client, peer);
        dialog.ShowDialog(this);
    }

    private static void OpenLogFolder()
    {
        try
        {
            System.IO.Directory.CreateDirectory(Logger.Dir);
            Process.Start(new ProcessStartInfo("explorer.exe", Logger.Dir) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show("打开日志目录失败：" + ex.Message, "Tailscale 中文托盘",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    public void SelectToolsTab()
    {
        // 只切换页面，绝不自动执行任何命令；必须用户手动点按钮
        _tabs.SelectedIndex = 3;
    }

    public void ShowToolsAndSendFile()
    {
        _tabs.SelectedIndex = 3;
        _ = StartSendFileAsync();
    }

    public void ShowToolsAndReceiveFiles()
    {
        _tabs.SelectedIndex = 3;
        _ = StartReceiveFilesAsync();
    }

    public void ShowToolsAndAccounts()
    {
        _tabs.SelectedIndex = 3;
        ShowAccounts();
    }

    public void UpdateError(string message)
    {
        if (IsDisposed)
            return;

        if (InvokeRequired)
        {
            BeginInvoke(new Action(() => UpdateError(message)));
            return;
        }

        _lblState.Text = message;
        _lblState.ForeColor = Color.FromArgb(185, 28, 28);
        _lblSelf.Text = "本机：—        地址：—";
        _lblExit.Text = "出口节点：—";
        _lnkAuth.Visible = false;
        _lblSummary.Text = "无法读取 Tailscale 状态，请确认 Tailscale 服务正在运行。";
        _list.Items.Clear();
        _statusLabel.Text = "无法读取 Tailscale 状态。";
    }

    private static string OsName(string os) => (os ?? "").ToLowerInvariant() switch
    {
        "windows" => "Windows",
        "linux" => "Linux",
        "macos" => "macOS",
        "ios" => "iOS",
        "android" => "Android",
        "freebsd" => "FreeBSD",
        _ => string.IsNullOrWhiteSpace(os) ? "—" : os
    };

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            Hide();
            return;
        }

        base.OnFormClosing(e);
    }
}
