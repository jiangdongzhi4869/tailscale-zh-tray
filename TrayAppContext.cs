using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace TailscaleZHTray;

internal sealed class TrayAppContext : ApplicationContext
{
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr hIcon);

    private const string AdminConsoleUrl = "https://login.tailscale.com/admin/machines";

    // 后台轮询间隔，主界面状态栏的文字也读这个值，避免改了一处忘了另一处
    public const int RefreshIntervalMs = 60000;

    public static int RefreshIntervalSeconds => RefreshIntervalMs / 1000;

    // 延迟探测：每轮只测一台在线设备，轮着来。20 台设备约 3 分钟转一圈，
    // 不用把整轮 ping 塞进一次刷新里（那会让界面卡住十几秒）
    public const int LatencyProbeIntervalMs = 10000;

    public static int LatencyProbeIntervalSeconds => LatencyProbeIntervalMs / 1000;

    private readonly TailscaleClient _client = new();
    private readonly NotifyIcon _tray = new();
    private readonly ContextMenuStrip _menu = new();
    private readonly System.Windows.Forms.Timer _timer = new();
    private readonly System.Windows.Forms.Timer _probeTimer = new();
    private readonly Dictionary<string, PeerLatency> _latency = new();
    private readonly Dictionary<string, Icon> _icons = new();
    private readonly Dictionary<string, ToolStripMenuItem> _miSettings = new();

    private readonly ToolStripMenuItem _miState = new() { Enabled = false };
    private readonly ToolStripMenuItem _miToggle = new("连接");
    private readonly ToolStripMenuItem _miExitNodes = new("出口节点");
    private readonly ToolStripMenuItem _miCopyIp = new("复制本机 IP");
    private readonly ToolStripMenuItem _miReset = new("恢复默认设置…");

    private MainForm _form;
    private ContextMenuStrip _exitMenu;
    private TailscaleSnapshot _snap;
    private bool _busy;
    private bool _probing;
    private bool _updatingMenu;
    private int _probeCursor;

    public Icon AppIcon => GetIcon("normal");

    public TailscaleClient Client => _client;

    public TrayAppContext(bool showWindowOnStart = false)
    {
        BuildMenu();

        _tray.Icon = GetIcon("normal");
        _tray.Text = "Tailscale 中文托盘（正在读取状态…）";
        _tray.Visible = true;
        _tray.DoubleClick += (_, _) => ShowMainForm();

        _timer.Interval = RefreshIntervalMs;
        _timer.Tick += async (_, _) => await RefreshAsync();
        _timer.Start();

        _probeTimer.Interval = LatencyProbeIntervalMs;
        _probeTimer.Tick += async (_, _) => await ProbeNextLatencyAsync();
        _probeTimer.Start();

        _ = RefreshAsync();

        if (showWindowOnStart)
            ShowMainForm();
    }

    private void BuildMenu()
    {
        _miState.Text = "状态：正在读取…";

        _menu.Items.Add(_miState);
        _menu.Items.Add(new ToolStripSeparator());

        var miOpen = new ToolStripMenuItem("打开主界面");
        miOpen.Click += (_, _) => ShowMainForm();
        _menu.Items.Add(miOpen);

        _miToggle.Click += async (_, _) => await ToggleAsync();
        _menu.Items.Add(_miToggle);

        _miExitNodes.DropDownOpening += (_, _) => FillExitNodeMenu(_miExitNodes.DropDownItems);
        _menu.Items.Add(_miExitNodes);

        _miCopyIp.Click += (_, _) => CopyIp();
        _menu.Items.Add(_miCopyIp);

        _menu.Items.Add(new ToolStripSeparator());

        var miSettings = new ToolStripMenuItem("设置");
        foreach (var spec in SettingsCatalog.All)
        {
            var item = new ToolStripMenuItem(spec.Label) { CheckOnClick = true, Tag = spec };
            item.Click += async (_, _) => await OnSettingMenuClicked(item, spec);
            miSettings.DropDownItems.Add(item);
            _miSettings[spec.Id] = item;
        }

        miSettings.DropDownItems.Add(new ToolStripSeparator());
        _miReset.Click += async (_, _) => await ResetDefaultsAsync();
        miSettings.DropDownItems.Add(_miReset);
        _menu.Items.Add(miSettings);

        var miTools = new ToolStripMenuItem("工具");
        var miToolsPage = new ToolStripMenuItem("打开工具页");
        miToolsPage.Click += (_, _) => ShowToolsPage();
        var miSend = new ToolStripMenuItem("发送文件…");
        miSend.Click += (_, _) => ShowSendFile();
        var miReceive = new ToolStripMenuItem("接收文件…");
        miReceive.Click += (_, _) => ShowReceiveFiles();
        var miAccounts = new ToolStripMenuItem("账号…");
        miAccounts.Click += (_, _) => ShowAccounts();
        var miLogout = new ToolStripMenuItem("退出登录…");
        miLogout.Click += async (_, _) => await LogoutAsync();
        var miOpenLog = new ToolStripMenuItem("打开日志目录");
        miOpenLog.Click += (_, _) => OpenLogFolder();

        miTools.DropDownItems.Add(miToolsPage);
        miTools.DropDownItems.Add(miSend);
        miTools.DropDownItems.Add(miReceive);
        miTools.DropDownItems.Add(new ToolStripSeparator());
        miTools.DropDownItems.Add(miAccounts);
        miTools.DropDownItems.Add(miLogout);
        miTools.DropDownItems.Add(new ToolStripSeparator());
        miTools.DropDownItems.Add(miOpenLog);
        _menu.Items.Add(miTools);

        _menu.Items.Add(new ToolStripSeparator());

        var miAdmin = new ToolStripMenuItem("打开管理后台");
        miAdmin.Click += (_, _) => OpenUrl(AdminConsoleUrl);
        _menu.Items.Add(miAdmin);

        var miOfficial = new ToolStripMenuItem("官方界面在系统托盘（点 Tailscale 图标）")
        {
            Enabled = false
        };
        _menu.Items.Add(miOfficial);

        _menu.Items.Add(new ToolStripSeparator());

        var miQuit = new ToolStripMenuItem("退出");
        miQuit.Click += (_, _) => Quit();
        _menu.Items.Add(miQuit);

        _tray.ContextMenuStrip = _menu;
    }

    public async Task RefreshAsync()
    {
        if (_busy)
            return;

        _busy = true;
        try
        {
            if (!_client.Available)
            {
                ApplyUnavailable("未找到 tailscale.exe");
                return;
            }

            var snapshot = await Task.Run(() => _client.Fetch());
            _snap = snapshot;
            Apply();
        }
        catch (Exception ex)
        {
            Logger.Log("刷新失败: " + ex);
            ApplyUnavailable("读取状态失败");
        }
        finally
        {
            _busy = false;
        }
    }

    private static string LatencyKey(PeerModel peer)
    {
        if (peer == null)
            return null;
        return string.IsNullOrWhiteSpace(peer.PrimaryIp) ? null : peer.PrimaryIp;
    }

    /// <summary>把已测到的延迟贴到当前快照上，界面直接读 PeerModel 即可。</summary>
    private void AttachLatency(TailscaleSnapshot snapshot)
    {
        if (snapshot == null)
            return;

        foreach (var peer in snapshot.Peers)
        {
            var key = LatencyKey(peer);
            if (key != null && _latency.TryGetValue(key, out var sample))
                peer.Latency = sample;
        }
    }

    private async Task ProbeNextLatencyAsync()
    {
        if (_probing)
            return;

        var snapshot = _snap;
        if (snapshot == null || !snapshot.IsRunning || !_client.Available)
            return;

        var online = snapshot.Peers
            .Where(p => p.Online && LatencyKey(p) != null)
            .ToList();

        if (online.Count == 0)
            return;

        if (_probeCursor >= online.Count)
            _probeCursor = 0;

        var peer = online[_probeCursor++ % online.Count];
        var ip = LatencyKey(peer);

        _probing = true;
        try
        {
            // 发 3 个包：第一个常常还是 DERP 中继，最后一个多半已打洞成直连
            var result = await Task.Run(() => _client.Ping(ip, 3, 12000));
            var sample = PingOutputParser.Parse(result.Out);

            var key = LatencyKey(peer);
            if (key != null)
                _latency[key] = sample;

            AttachLatency(_snap);
            _form?.RefreshLatencyColumn();
        }
        catch (Exception ex)
        {
            Logger.Log("延迟探测失败: " + ex);
        }
        finally
        {
            _probing = false;
        }
    }

    private void Apply()
    {
        var snapshot = _snap;
        if (snapshot == null)
            return;

        AttachLatency(snapshot);

        var (stateText, _, iconKey) = StateInfo(snapshot, _client.Available);

        _miState.Text = "状态：" + stateText;
        _tray.Icon = GetIcon(iconKey);

        var tip = "Tailscale：" + stateText;
        if (snapshot.IsRunning && snapshot.SelfIps.Count > 0)
            tip += "  " + snapshot.SelfIps[0];
        _tray.Text = tip.Length > 63 ? tip[..63] : tip;

        _miToggle.Text = snapshot.IsRunning
            ? "断开连接"
            : NeedsLogin(snapshot) ? "登录并连接" : "连接";
        _miToggle.Enabled = true;
        _miCopyIp.Enabled = snapshot.SelfIps.Count > 0;

        _updatingMenu = true;
        foreach (var spec in SettingsCatalog.All)
        {
            if (_miSettings.TryGetValue(spec.Id, out var item))
                item.Checked = spec.Get(snapshot);
        }
        _updatingMenu = false;

        FillExitNodeMenu(_miExitNodes.DropDownItems);
        _form?.UpdateView(snapshot, true);
    }

    private void ApplyUnavailable(string message)
    {
        _miState.Text = "状态：" + message;
        _tray.Icon = GetIcon("error");
        _tray.Text = "Tailscale 中文托盘：" + message;
        _miToggle.Enabled = false;
        _form?.UpdateError(message);
    }

    public async Task ToggleAsync()
    {
        if (!_client.Available)
        {
            MessageBox.Show("没有找到 tailscale.exe，请确认 Tailscale 已正确安装。",
                "Tailscale 中文托盘", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        if (_snap == null)
        {
            await RefreshAsync();
            if (_snap == null)
                return;
        }

        if (_snap.IsRunning)
        {
            var result = await Task.Run(_client.Down);
            if (result.Code != 0)
                ShowCommandError("断开失败", result.Err, result.Out);
        }
        else
        {
            _client.Up();
            MessageBox.Show(
                "已发起连接。\n\n" +
                "如果这台机器还没有登录 Tailscale，浏览器会自动打开授权页面；" +
                "如果没有自动打开，请回到主界面点『打开登录授权页』。",
                "Tailscale 中文托盘", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        await Task.Delay(1200);
        await RefreshAsync();
    }

    public async Task SetExitNodeAsync(string ip)
    {
        var result = await Task.Run(() => _client.SetExitNode(ip));
        if (result.Code != 0)
            ShowCommandError("设置出口节点失败", result.Err, result.Out);

        await RefreshAsync();
    }

    public async Task<bool> ApplySettingAsync(ToggleSpec spec, bool value)
    {
        var result = await Task.Run(() => _client.Run(spec.ArgFor(value), 20000));
        if (result.Code != 0)
        {
            ShowCommandError("修改设置失败", result.Err, result.Out);
            return false;
        }

        await RefreshAsync();
        return true;
    }

    private async Task OnSettingMenuClicked(ToolStripMenuItem item, ToggleSpec spec)
    {
        if (_updatingMenu)
            return;

        var want = item.Checked;
        var confirm = spec.Confirm?.Invoke(want);

        if (!string.IsNullOrEmpty(confirm) &&
            MessageBox.Show(confirm, "修改设置",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
        {
            SetMenuCheck(spec, !want);
            return;
        }

        var ok = await ApplySettingAsync(spec, want);
        if (!ok)
            SetMenuCheck(spec, !want);
    }

    private void SetMenuCheck(ToggleSpec spec, bool value)
    {
        if (!_miSettings.TryGetValue(spec.Id, out var item))
            return;

        _updatingMenu = true;
        item.Checked = value;
        _updatingMenu = false;
    }

    public async Task ResetDefaultsAsync()
    {
        var answer = MessageBox.Show(
            "恢复默认设置会把未指定的 Tailscale 设置重置为默认值，并重新连接网络。\n\n" +
            "这可能会改变当前的连接行为和 DNS 设置。确定继续吗？",
            "恢复默认设置",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning);

        if (answer != DialogResult.Yes)
            return;

        var result = await Task.Run(_client.ResetDefaults);
        if (result.Code != 0)
            ShowCommandError("恢复默认设置失败", result.Err, result.Out);

        await RefreshAsync();
    }

    public async Task LogoutAsync()
    {
        var answer = MessageBox.Show(
            "退出登录会断开连接，并作废当前节点的密钥。\n" +
            "下次使用需要重新登录授权。\n\n确定退出登录吗？",
            "确认退出登录",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning);

        if (answer != DialogResult.Yes)
            return;

        var result = await Task.Run(_client.Logout);
        if (result.Code != 0)
            ShowCommandError("退出登录失败", result.Err, result.Out);

        await RefreshAsync();
    }

    private void FillExitNodeMenu(ToolStripItemCollection items)
    {
        items.Clear();

        var noneItem = new ToolStripMenuItem("不使用出口节点")
        {
            Checked = string.IsNullOrWhiteSpace(_snap?.ExitNodeIp)
        };
        noneItem.Click += async (_, _) => await SetExitNodeAsync("");
        items.Add(noneItem);

        var options = _snap?.Peers
            .Where(p => p.ExitNodeOption)
            .OrderBy(p => p.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ToList() ?? new List<PeerModel>();

        items.Add(new ToolStripSeparator());

        if (options.Count == 0)
        {
            items.Add(new ToolStripMenuItem("（没有可用的出口节点）") { Enabled = false });
            return;
        }

        foreach (var peer in options)
        {
            var ip = peer.PrimaryIp;
            var text = string.IsNullOrEmpty(ip)
                ? peer.DisplayName
                : $"{peer.DisplayName}    {ip}";

            var item = new ToolStripMenuItem(text)
            {
                Checked = !string.IsNullOrWhiteSpace(_snap?.ExitNodeIp) &&
                          (peer.Ips.Contains(_snap.ExitNodeIp) || peer.ExitNode)
            };
            item.Click += async (_, _) => await SetExitNodeAsync(ip);
            items.Add(item);
        }
    }

    public void ShowExitNodeMenu(Control anchor)
    {
        // 复用同一个菜单，绝不在 Closed 事件里 Dispose，否则菜单关闭时会被二次释放导致崩溃
        if (_exitMenu == null || _exitMenu.IsDisposed)
            _exitMenu = new ContextMenuStrip();

        FillExitNodeMenu(_exitMenu.Items);
        _exitMenu.Show(anchor, new Point(0, anchor.Height + 4));
    }

    public void CopyIp()
    {
        var ip = _snap?.SelfIps.FirstOrDefault();
        if (string.IsNullOrEmpty(ip))
        {
            MessageBox.Show("当前还没有分配 Tailscale IP（可能尚未登录）。",
                "Tailscale 中文托盘", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        try
        {
            Clipboard.SetText(ip);
        }
        catch (Exception ex)
        {
            MessageBox.Show("复制失败：" + ex.Message,
                "Tailscale 中文托盘", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    public void ShowMainForm()
    {
        if (_form == null || _form.IsDisposed)
        {
            _form = new MainForm(this);
            _form.FormClosed += (_, _) => _form = null;
        }

        _form.Show();
        if (_form.WindowState == FormWindowState.Minimized)
            _form.WindowState = FormWindowState.Normal;
        _form.Activate();

        if (_snap != null)
            _form.UpdateView(_snap, _client.Available);
    }

    public void ShowToolsPage()
    {
        ShowMainForm();
        _form?.SelectToolsTab();
    }

    public void ShowSendFile()
    {
        ShowMainForm();
        _form?.ShowToolsAndSendFile();
    }

    public void ShowReceiveFiles()
    {
        ShowMainForm();
        _form?.ShowToolsAndReceiveFiles();
    }

    public void ShowAccounts()
    {
        ShowMainForm();
        _form?.ShowToolsAndAccounts();
    }

    private static void OpenLogFolder()
    {
        try
        {
            Directory.CreateDirectory(Logger.Dir);
            Process.Start(new ProcessStartInfo("explorer.exe", Logger.Dir) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show("打开日志目录失败：" + ex.Message,
                "Tailscale 中文托盘", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void Quit()
    {
        _timer.Stop();
        _probeTimer.Stop();

        try { _tray.Visible = false; _tray.Dispose(); } catch { }

        foreach (var icon in _icons.Values)
        {
            try { icon.Dispose(); } catch { }
        }
        _icons.Clear();

        try { _exitMenu?.Dispose(); } catch { }

        ExitThread();
    }

    public static void OpenUrl(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show("打开链接失败：" + ex.Message,
                "Tailscale 中文托盘", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private static void ShowCommandError(string title, string stderr, string stdout)
    {
        var detail = !string.IsNullOrWhiteSpace(stderr) ? stderr : stdout;
        if (string.IsNullOrWhiteSpace(detail))
            detail = "命令执行失败，但没有返回错误信息。";
        MessageBox.Show(detail.Trim(), title, MessageBoxButtons.OK, MessageBoxIcon.Warning);
    }

    public static bool NeedsLogin(TailscaleSnapshot snapshot)
    {
        if (snapshot == null)
            return false;

        return snapshot.LoggedOut ||
               string.Equals(snapshot.BackendState, "NeedsLogin", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(snapshot.BackendState, "NeedsMachineAuth", StringComparison.OrdinalIgnoreCase);
    }

    public static (string Text, Color Color, string IconKey) StateInfo(
        TailscaleSnapshot snapshot, bool available)
    {
        if (!available)
            return ("不可用", Color.FromArgb(185, 28, 28), "error");

        if (snapshot == null)
            return ("读取中…", Color.FromArgb(107, 114, 128), "normal");

        if (snapshot.LoggedOut)
            return ("已退出登录", Color.FromArgb(217, 119, 6), "needslogin");

        return (snapshot.BackendState ?? "").ToLowerInvariant() switch
        {
            "running" => ("已连接", Color.FromArgb(16, 163, 127), "connected"),
            "starting" => ("正在启动…", Color.FromArgb(217, 119, 6), "needslogin"),
            "needslogin" => ("需要登录", Color.FromArgb(217, 119, 6), "needslogin"),
            "needsmachineauth" => ("等待管理员授权", Color.FromArgb(217, 119, 6), "needslogin"),
            "stopped" => ("已停止", Color.FromArgb(107, 114, 128), "normal"),
            "nostate" => ("未初始化", Color.FromArgb(107, 114, 128), "normal"),
            _ => (string.IsNullOrWhiteSpace(snapshot.BackendState) ? "未知" : snapshot.BackendState,
                Color.FromArgb(107, 114, 128), "normal")
        };
    }

    public static string TranslateHealth(string health)
    {
        if (string.IsNullOrWhiteSpace(health))
            return "";

        return health.Trim() switch
        {
            "Tailscale is stopped." => "Tailscale 已停止",
            "You are logged out." => "你已退出登录",
            "Tailscale is starting." => "Tailscale 正在启动",
            "This node is offline." => "本机当前离线",
            _ => health.Trim()
        };
    }

    private Icon GetIcon(string key)
    {
        if (_icons.TryGetValue(key, out var existing))
            return existing;

        var color = key switch
        {
            "connected" => Color.FromArgb(16, 163, 127),
            "needslogin" => Color.FromArgb(217, 119, 6),
            "error" => Color.FromArgb(185, 28, 28),
            _ => Color.FromArgb(107, 114, 128)
        };

        using var bitmap = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bitmap))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);

            using var brush = new SolidBrush(color);
            g.FillEllipse(brush, 1, 1, 30, 30);

            using var font = new Font("Segoe UI", 17, FontStyle.Bold, GraphicsUnit.Pixel);
            using var format = new StringFormat
            {
                Alignment = StringAlignment.Center,
                LineAlignment = StringAlignment.Center
            };
            g.DrawString("T", font, Brushes.White, new RectangleF(0, 0, 32, 32), format);
        }

        var handle = bitmap.GetHicon();
        var icon = (Icon)Icon.FromHandle(handle).Clone();
        DestroyIcon(handle);

        _icons[key] = icon;
        return icon;
    }
}
