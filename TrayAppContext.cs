using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace TailscaleZHTray;

internal sealed class TrayAppContext : ApplicationContext
{
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr hIcon);

    private const string AdminConsoleUrl = "https://login.tailscale.com/admin/machines";

    // 点「刷新」后逐台测延迟，每测完一台隔 3 秒再测下一台
    public const int LatencyStepDelayMs = 3000;

    // 不做后台轮询：状态和延迟只在用户操作时刷新（点「刷新」、连接/断开、改设置之后）
    private readonly TailscaleClient _client = new();
    private readonly NotifyIcon _tray = new();
    private readonly ContextMenuStrip _menu = new();
    private readonly Dictionary<string, PeerLatency> _latency = new();
    private readonly Dictionary<string, Icon> _icons = new();
    private readonly Dictionary<string, ToolStripMenuItem> _miSettings = new();

    private readonly ToolStripMenuItem _miState = new() { Enabled = false };
    private readonly ToolStripMenuItem _miToggle = new("连接");
    private readonly ToolStripMenuItem _miExitNodes = new("出口节点");
    private readonly ToolStripMenuItem _miCopySelfV4 = new("复制本机 IPv4");
    private readonly ToolStripMenuItem _miCopySelfV6 = new("复制本机 IPv6");
    private readonly ToolStripMenuItem _miCopySelfAll = new("复制本机全部 IP");
    private readonly ToolStripMenuItem _miReset = new("恢复默认设置…");

    private MainForm _form;
    private ContextMenuStrip _exitMenu;
    private TailscaleSnapshot _snap;
    private bool _busy;
    private bool _probing;
    private bool _refreshPending;
    private bool _pendingWithLatency;
    private bool _updatingMenu;
    private CancellationTokenSource _scanCts;

    /// <summary>正在退出：后台循环看到它就收手，别再碰已经释放掉的托盘和窗体。</summary>
    private bool _closing;

    /// <summary>连接后正在盯状态：这期间不允许再点连接/断开。</summary>
    private bool _polling;
    private readonly Control _marshaller = new();
    private EventWaitHandle _showSignal;
    private Thread _showWaiter;

    public Icon AppIcon => GetIcon("normal");

    public TailscaleClient Client => _client;

    public TrayAppContext(bool showWindowOnStart = false)
    {
        BuildMenu();

        _tray.Icon = GetIcon("normal");
        _tray.Text = "Tailscale 中文托盘（正在读取状态…）";
        _tray.Visible = true;
        _tray.DoubleClick += (_, _) => ShowMainForm();

        StartShowSignalListener();

        // 启动时读一次状态（托盘图标和提示要有个初始值），之后不再自动刷新
        _ = RefreshAsync();

        if (showWindowOnStart)
            ShowMainForm();
    }

    /// <summary>
    /// 第二个实例启动时会 Set 这个具名事件，我们把已存在的窗口叫到前台，
    /// 而不是弹一句「已经在运行了」然后什么也不做。
    /// </summary>
    private void StartShowSignalListener()
    {
        try
        {
            _ = _marshaller.Handle;   // 提前建句柄，后台线程才能 BeginInvoke
            _showSignal = new EventWaitHandle(false, EventResetMode.AutoReset, Program.ShowSignalName);

            _showWaiter = new Thread(() =>
            {
                while (true)
                {
                    try
                    {
                        if (!_showSignal.WaitOne())
                            return;
                    }
                    catch
                    {
                        return;
                    }

                    try
                    {
                        _marshaller.BeginInvoke(new Action(ShowMainForm));
                    }
                    catch
                    {
                        return;
                    }
                }
            })
            {
                IsBackground = true,
                Name = "TailscaleZHTray-ShowSignal"
            };
            _showWaiter.Start();
        }
        catch (Exception ex)
        {
            Logger.Log("单实例信号初始化失败: " + ex.Message);
        }
    }

    private void BuildMenu()
    {
        _miState.Text = "状态：正在读取…";

        _menu.Items.Add(_miState);
        _menu.Items.Add(new ToolStripSeparator());

        var miOpen = new ToolStripMenuItem("打开主界面");
        miOpen.Click += (_, _) => ShowMainForm();
        _menu.Items.Add(miOpen);

        // 没有后台轮询，托盘里得留个手动刷新的入口
        var miRefresh = new ToolStripMenuItem("刷新状态");
        miRefresh.Click += async (_, _) => await RefreshAsync();
        _menu.Items.Add(miRefresh);

        _miToggle.Click += async (_, _) => await ToggleAsync();
        _menu.Items.Add(_miToggle);

        _miExitNodes.DropDownOpening += (_, _) => FillExitNodeMenu(_miExitNodes.DropDownItems);
        _menu.Items.Add(_miExitNodes);

        _miCopySelfV4.Click += (_, _) => CopySelfIp(false);
        _miCopySelfV6.Click += (_, _) => CopySelfIp(true);
        _miCopySelfAll.Click += (_, _) => CopySelfIp(null);
        _menu.Items.Add(_miCopySelfV4);
        _menu.Items.Add(_miCopySelfV6);
        _menu.Items.Add(_miCopySelfAll);

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
        var miReceive = new ToolStripMenuItem("打开下载目录");
        miReceive.Click += (_, _) => KnownFolders.OpenDownloads();
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

    /// <param name="withLatency">用户主动刷新时为 true：顺手把在线设备的延迟也测一遍。</param>
    public async Task<bool> RefreshAsync(bool withLatency = false)
    {
        if (_busy)
        {
            // 忙的时候（正在测延迟）不能直接丢掉这次请求，
            // 否则「断开连接」之后界面会停在旧状态
            _refreshPending = true;
            _pendingWithLatency |= withLatency;
            _form?.SetStatus("正在刷新中，已排队再刷一次…");
            return true;
        }

        _busy = true;
        try
        {
            if (!_client.Available)
            {
                ApplyUnavailable("未找到 tailscale.exe");
                return false;
            }

            var snapshot = await Task.Run(() => _client.Fetch());
            _snap = snapshot;
            Apply();

            if (withLatency)
                await MeasureLatenciesAsync(snapshot);
            else
                _form?.ShowRefreshStatus();

            return true;
        }
        catch (Exception ex)
        {
            Logger.Log("刷新失败: " + ex);
            ApplyUnavailable("读取状态失败");
            _form?.ShowRefreshStatus();
            return false;
        }
        finally
        {
            _busy = false;

            if (_refreshPending)
            {
                var again = _pendingWithLatency;
                _refreshPending = false;
                _pendingWithLatency = false;
                _ = RefreshAsync(again);
            }
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

    /// <summary>
    /// 逐台测在线设备的延迟。只在用户点「刷新」时调用一次，
    /// 不做定时轮询 —— 没有用户动作就不该产生 ping 流量。
    /// </summary>
    private async Task MeasureLatenciesAsync(TailscaleSnapshot snapshot)
    {
        if (_probing || snapshot == null || !snapshot.IsRunning || !_client.Available)
        {
            _form?.ShowRefreshStatus();
            return;
        }

        var online = snapshot.Peers
            .Where(p => p.Online && LatencyKey(p) != null)
            .ToList();

        if (online.Count == 0)
        {
            _form?.ShowRefreshStatus();
            return;
        }

        var cts = new CancellationTokenSource();
        _scanCts = cts;
        var token = cts.Token;

        _probing = true;
        try
        {
            for (var i = 0; i < online.Count; i++)
            {
                if (token.IsCancellationRequested)
                    break;

                var peer = online[i];
                var ip = LatencyKey(peer);

                _form?.SetStatus($"正在测延迟 {i + 1}/{online.Count}：{peer.DisplayName} …");

                // 发 3 个包：第一个常常还是 DERP 中继，最后一个多半已打洞成直连
                var result = await Task.Run(() => _client.Ping(ip, 3, 12000));
                if (token.IsCancellationRequested)
                    break;

                var sample = PingOutputParser.Parse(result.Out);

                var key = LatencyKey(peer);
                if (key != null)
                    _latency[key] = sample;

                Logger.Log($"延迟 {i + 1}/{online.Count} {peer.DisplayName} = " +
                           (sample.TimedOut ? "超时" : $"{sample.Ms}ms {(sample.Relay ? "中继 " + sample.Path : "直连")}"));

                AttachLatency(_snap);
                _form?.RefreshLatencyColumn();

                // 测完一台歇 3 秒再测下一台，避免连续 ping 抢带宽
                if (i < online.Count - 1)
                {
                    _form?.SetStatus(
                        $"延迟已测 {i + 1}/{online.Count}，{LatencyStepDelayMs / 1000} 秒后测下一台…");
                    try
                    {
                        await Task.Delay(LatencyStepDelayMs, token);
                    }
                    catch (TaskCanceledException)
                    {
                        break;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Log("延迟测量失败: " + ex);
        }
        finally
        {
            _probing = false;
            if (ReferenceEquals(_scanCts, cts))
                _scanCts = null;
            cts.Dispose();

            // 退出中就不要再去碰已经销毁的窗体了
            if (!token.IsCancellationRequested)
                _form?.ShowRefreshStatus();
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
        _miToggle.Enabled = !_polling;
        var selfV4 = SelfIps(false);
        var selfV6 = SelfIps(true);
        _miCopySelfV4.Enabled = selfV4.Count > 0;
        _miCopySelfV6.Enabled = selfV6.Count > 0;
        _miCopySelfAll.Enabled = snapshot.SelfIps.Count > 0;

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
        // 连接后正在盯状态时别再点一次，否则会并发下发 up/down
        if (_polling)
            return;

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

            // 刚 up 时后端还在 Starting 这类过渡态，只看一次会一直停在「正在启动…」
            await PollUntilSettledAsync();
            return;
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

    private List<string> SelfIps(bool? wantV6)
    {
        var ips = _snap?.SelfIps ?? new List<string>();
        if (wantV6 == null)
            return ips.Where(ip => !string.IsNullOrWhiteSpace(ip)).ToList();

        return ips.Where(ip => !string.IsNullOrWhiteSpace(ip))
                  .Where(ip => ip.Contains(':') == wantV6.Value)
                  .ToList();
    }

    /// <param name="wantV6">null = 全部地址，每行一个</param>
    public void CopySelfIp(bool? wantV6)
    {
        var text = string.Join(Environment.NewLine, SelfIps(wantV6));
        if (string.IsNullOrWhiteSpace(text))
        {
            MessageBox.Show("当前还没有分配这一类 Tailscale IP（可能尚未登录）。",
                "Tailscale 中文托盘", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        try
        {
            Clipboard.SetText(text);
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
        // 强行退出：先掐掉正在跑的延迟扫描和连接后的状态轮询，不等它们自己结束
        _closing = true;
        try { _scanCts?.Cancel(); } catch { }

        try { _tray.Visible = false; _tray.Dispose(); } catch { }

        foreach (var icon in _icons.Values)
        {
            try { icon.Dispose(); } catch { }
        }
        _icons.Clear();

        try { _exitMenu?.Dispose(); } catch { }
        try { _showSignal?.Dispose(); } catch { }

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

        // tailscale.exe 权限不足时只回一句英文，用户看不出要干什么
        if (detail.Contains("Access is denied", StringComparison.OrdinalIgnoreCase) ||
            detail.Contains("拒绝访问", StringComparison.OrdinalIgnoreCase) ||
            detail.Contains("may not be running", StringComparison.OrdinalIgnoreCase) ||
            detail.Contains("Failed to connect to local tailscaled", StringComparison.OrdinalIgnoreCase))
        {
            MessageBox.Show(
                "这个操作需要管理员权限（当前是普通权限在跑）。" + Environment.NewLine + Environment.NewLine +
                "请退出本程序，右键 TailscaleZHTray.exe 选「以管理员身份运行」，再试一次。" + Environment.NewLine + Environment.NewLine +
                "原始错误：" + detail.Trim(),
                "需要管理员权限", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        MessageBox.Show(detail.Trim(), title, MessageBoxButtons.OK, MessageBoxIcon.Warning);
    }

    /// <summary>
    /// 刚发起连接时后端要经历 Starting 之类的过渡状态，只刷新一次界面会一直停在「正在启动…」。
    /// 这里最多盯 30 秒，状态稳定就停；一直没稳定下来就提示用户检查 Tailscale 是否在运行。
    /// </summary>
    private async Task PollUntilSettledAsync()
    {
        var failures = 0;

        _polling = true;
        try
        {
            for (var i = 0; i < 15 && !_closing; i++)
            {
                await Task.Delay(2000);
                if (_closing)
                    return;

                var ok = await RefreshAsync();
                if (_closing)
                    return;

                // 刷新连续失败说明连不上 tailscaled，再等也是空转
                failures = ok ? 0 : failures + 1;
                if (failures >= 2)
                    return;

                var state = (_snap?.BackendState ?? "").Trim().ToLowerInvariant();
                if (state == "running" || state == "stopped" ||
                    state == "needslogin" || state == "needsmachineauth")
                    return;

                // NoState / 空状态是终态：服务没起来，别等满 30 秒才说
                if (state.Length == 0 || state == "nostate")
                {
                    MessageBox.Show(
                        "Tailscale 服务好像没有在运行。" + Environment.NewLine + Environment.NewLine +
                        "请先打开 Tailscale 客户端，再回来点「刷新」。",
                        "Tailscale 中文托盘", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
            }

            if (_closing)
                return;

            MessageBox.Show(
                "等了 30 秒，连接状态还没稳定下来。" + Environment.NewLine + Environment.NewLine +
                "如果 Tailscale 服务没有在运行，请先打开 Tailscale 客户端，再回来点「刷新」。",
                "Tailscale 中文托盘", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        finally
        {
            _polling = false;
            if (!_closing)
                _miToggle.Enabled = _snap != null;
        }
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
