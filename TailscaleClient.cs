using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace TailscaleZHTray;

internal sealed class PeerModel
{
    public string HostName = "";
    public string DnsName = "";
    public string Os = "";
    public List<string> Ips = new();
    public bool Online;
    public bool Active;
    public bool ExitNode;
    public bool ExitNodeOption;
    public string Relay = "";
    public string CurAddr = "";
    public long RxBytes;
    public long TxBytes;
    public DateTimeOffset LastHandshake;

    /// <summary>最近一次 ping 测得的延迟，null 表示还没测过。</summary>
    public PeerLatency Latency;

    /// <summary>节点密钥到期时间，default(0001-01-01) 表示永不过期。</summary>
    public DateTimeOffset KeyExpiry;

    public string KeyExpiryText
    {
        get
        {
            if (KeyExpiry.Year <= 1)
                return "永不过期";

            var days = (KeyExpiry.Date - DateTime.Today).Days;
            if (days < 0)
                return $"{KeyExpiry:yyyy-MM-dd}（已过期）";

            return $"{KeyExpiry:yyyy-MM-dd}（剩 {days} 天）";
        }
    }

    /// <summary>快到期或已过期，列表里用醒目颜色提示。</summary>
    public bool KeyExpiryWarning => KeyExpiry.Year > 1 && (KeyExpiry.Date - DateTime.Today).Days <= 30;

    public string LatencyText =>
        Latency == null ? "—" : Latency.TimedOut ? "超时" : Latency.Ms < 0 ? "—" : $"{Latency.Ms} ms";

    public string DisplayName =>
        !string.IsNullOrWhiteSpace(HostName) ? HostName.Trim() : (DnsName ?? "").Trim();

    public string PrimaryIp =>
        Ips.FirstOrDefault(x => x.Contains('.')) ?? Ips.FirstOrDefault() ?? "";

    public string ConnectionText
    {
        get
        {
            // ping 实测到的路径比 status 字段更可信：打洞成功后 status 未必立刻更新
            if (Latency != null && !Latency.TimedOut && !string.IsNullOrWhiteSpace(Latency.Path))
                return Latency.Relay ? "中继 " + Latency.Path : "直连";

            if (!string.IsNullOrWhiteSpace(CurAddr))
                return "直连";
            if (!string.IsNullOrWhiteSpace(Relay))
                return "中继 " + Relay;
            return "—";
        }
    }
}

internal sealed class PeerLatency
{
    public int Ms = -1;
    public bool Relay;
    public string Path = "";
    public bool TimedOut;
    public DateTime At = DateTime.Now;
}

internal static class PingOutputParser
{
    // 实测输出（三种单位都可能出现）：
    //   pong from pc (100.x.x.x) via DERP(sfo) in 1.908s
    //   pong from pc (100.x.x.x) via DERP(sfo) in 405ms
    //   pong from pc (100.x.x.x) via [240e:...]:41641 in 43ms
    private static readonly Regex PongRegex = new(
        @"via\s+(?<via>.+?)\s+in\s+(?<val>[0-9]+(?:\.[0-9]+)?)\s*(?<unit>ns|us|µs|ms|s)\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>取最后一次成功的 pong —— 多发包时最后一次通常是打洞成功后的直连。</summary>
    public static PeerLatency Parse(string output)
    {
        var result = new PeerLatency { At = DateTime.Now };
        if (string.IsNullOrWhiteSpace(output))
        {
            result.TimedOut = true;
            return result;
        }

        var matches = PongRegex.Matches(output);
        if (matches.Count == 0)
        {
            result.TimedOut = true;
            return result;
        }

        var match = matches[matches.Count - 1];
        var via = match.Groups["via"].Value.Trim();

        if (via.StartsWith("DERP", StringComparison.OrdinalIgnoreCase))
        {
            result.Relay = true;
            var open = via.IndexOf('(');
            var close = via.LastIndexOf(')');
            result.Path = open >= 0 && close > open ? via.Substring(open + 1, close - open - 1) : via;
        }
        else
        {
            result.Path = via;
        }

        if (double.TryParse(match.Groups["val"].Value, NumberStyles.Float,
                CultureInfo.InvariantCulture, out var value))
        {
            var ms = match.Groups["unit"].Value switch
            {
                "s" => value * 1000,
                "ms" => value,
                "us" or "µs" => value / 1000,
                "ns" => value / 1_000_000,
                _ => value
            };
            result.Ms = (int)Math.Round(ms);
        }

        return result;
    }
}

internal sealed class TailscaleSnapshot
{
    public string BackendState = "NoState";
    public string AuthUrl = "";
    public string Version = "";
    public List<string> Health = new();
    public string HostName = "";
    public string DnsName = "";
    public List<string> SelfIps = new();
    public bool SelfOnline;
    public bool HasUser;
    public string ExitNodeIp = "";
    public bool LoggedOut;
    public List<PeerModel> Peers = new();

    // 设置项（来自 tailscale get --json）
    public bool AcceptDns;
    public bool AcceptRoutes;
    public bool ShieldsUp;
    public bool AutoUpdate;
    public bool Unattended;
    public bool ExitNodeAllowLanAccess;
    public bool AdvertiseExitNode;
    public bool RunSsh;
    public bool WebClient;
    public string HostnameSetting = "";

    public bool IsRunning =>
        string.Equals(BackendState, "Running", StringComparison.OrdinalIgnoreCase);

    public string ExitNodeName
    {
        get
        {
            if (string.IsNullOrWhiteSpace(ExitNodeIp))
                return "";
            var peer = Peers.FirstOrDefault(p => p.Ips.Contains(ExitNodeIp))
                       ?? Peers.FirstOrDefault(p => p.ExitNode);
            return peer != null ? peer.DisplayName : ExitNodeIp;
        }
    }
}

internal sealed class TailscaleClient
{
    public string ExePath { get; }

    public TailscaleClient()
    {
        ExePath = FindExe(out var source);
        Logger.Log($"tailscale.exe 定位: {ExePath ?? "(未找到)"}  来源={source}");
    }

    /// <summary>
    /// 优先从系统 PATH 里找 tailscale.exe（官方安装器会把它写进 PATH），
    /// 找不到再退回官方默认安装目录。
    /// </summary>
    private static string FindExe(out string source)
    {
        source = "PATH";

        var path = Environment.GetEnvironmentVariable("PATH");
        if (!string.IsNullOrWhiteSpace(path))
        {
            foreach (var raw in path.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var dir = raw.Trim().Trim('"');
                if (dir.Length == 0)
                    continue;

                try
                {
                    var candidate = Path.Combine(dir, "tailscale.exe");
                    if (File.Exists(candidate))
                        return candidate;
                }
                catch
                {
                    // PATH 里可能有非法路径（通配符、超长等），跳过即可
                }
            }
        }

        source = "固定路径";
        foreach (var candidate in new[]
                 {
                     @"C:\Program Files\Tailscale\tailscale.exe",
                     @"C:\Program Files (x86)\Tailscale\tailscale.exe"
                 })
        {
            if (File.Exists(candidate))
                return candidate;
        }

        source = "未找到";
        return null;
    }

    public bool Available => !string.IsNullOrEmpty(ExePath);

    private ProcessStartInfo NewPsi(string args) => new()
    {
        FileName = ExePath,
        Arguments = args,
        UseShellExecute = false,
        CreateNoWindow = true,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        StandardOutputEncoding = new UTF8Encoding(false),
        StandardErrorEncoding = new UTF8Encoding(false),
    };

    public (int Code, string Out, string Err) Run(string args, int timeoutMs = 15000)
    {
        if (!Available)
            return (-3, "", "没有找到 tailscale.exe");

        try
        {
            using var p = new Process { StartInfo = NewPsi(args) };
            p.Start();

            var stdout = p.StandardOutput.ReadToEndAsync();
            var stderr = p.StandardError.ReadToEndAsync();

            if (!p.WaitForExit(timeoutMs))
            {
                try { p.Kill(entireProcessTree: true); } catch { }
                p.WaitForExit(3000);
                return (-1, Drained(stdout), Drained(stderr) + "\n（命令执行超时）");
            }

            return (p.ExitCode, SafeResult(stdout), SafeResult(stderr));
        }
        catch (Exception ex)
        {
            Logger.Log($"Run 失败: {args} => {ex}");
            return (-2, "", ex.Message);
        }
    }

    /// <summary>进程已退出或被杀掉之后取剩余输出（管道已关闭，不会挂住）。</summary>
    private static string Drained(Task<string> task)
    {
        try
        {
            return task.GetAwaiter().GetResult();
        }
        catch
        {
            return "";
        }
    }

    private static string SafeResult(Task<string> task)
    {
        try
        {
            return task.Wait(3000) ? task.Result : "";
        }
        catch
        {
            return "";
        }
    }

    /// <summary>发起可能阻塞的命令（如 up 会等待登录），不等待退出。</summary>
    public void StartDetached(string args)
    {
        if (!Available)
            return;

        try
        {
            // 注意：不重定向输出时不能设置 StandardOutputEncoding，否则 Process.Start 会抛异常
            var psi = new ProcessStartInfo
            {
                FileName = ExePath,
                Arguments = args,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = false,
                RedirectStandardError = false,
            };

            var p = Process.Start(psi);
            Logger.Log($"已发起命令: tailscale {args} (pid={p?.Id})");

            if (p != null)
            {
                p.EnableRaisingEvents = true;
                p.Exited += (_, _) =>
                {
                    try { Logger.Log($"命令结束: tailscale {args} (code={p.ExitCode})"); }
                    catch { }
                };
            }
        }
        catch (Exception ex)
        {
            Logger.Log($"StartDetached 失败: tailscale {args} => {ex}");
        }
    }

    public TailscaleSnapshot Fetch()
    {
        var status = Run("status --json", 20000);
        if (status.Code != 0)
        {
            var msg = string.IsNullOrWhiteSpace(status.Err) ? status.Out : status.Err;
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(msg)
                ? "tailscale status 调用失败"
                : msg.Trim());
        }

        var snapshot = ParseStatus(status.Out);

        var prefs = Run("get --json", 10000);
        if (prefs.Code == 0 && !string.IsNullOrWhiteSpace(prefs.Out))
        {
            try
            {
                ParsePrefs(prefs.Out, snapshot);
            }
            catch (Exception ex)
            {
                Logger.Log("get --json 解析失败: " + ex.Message);
            }
        }

        return snapshot;
    }

    public (int Code, string Out, string Err) Down() => Run("down", 20000);

    public (int Code, string Out, string Err) SetExitNode(string ip) =>
        Run($"set --exit-node={ip}", 20000);

    public (int Code, string Out, string Err) SetUnattended(bool enabled) =>
        Run($"set --unattended={(enabled ? "true" : "false")}", 20000);

    public (int Code, string Out, string Err) Logout() => Run("logout", 20000);

    public (int Code, string Out, string Err) Ping(string host, int count = 4, int timeoutMs = 30000) =>
        Run($"ping --c {count} {host}", timeoutMs);

    public (int Code, string Out, string Err) Netcheck() =>
        Run("netcheck --format json", 60000);

    public (int Code, string Out, string Err) ResetDefaults() =>
        Run("up --reset", 40000);

    public (int Code, string Out, string Err) ListProfiles() =>
        Run("switch --list --json", 10000);

    public (int Code, string Out, string Err) SwitchProfile(string id) =>
        Run($"switch {Quote(id)}", 20000);

    public void LoginNewAccount() => StartDetached("login");

    /// <summary>
    /// 发送超时按文件大小算：固定 120 秒会让稍大的文件在走 DERP 中继时被强杀。
    /// 实测中继只有几十 KB/s，所以留足余量（每 MB 给 30 秒，另加 60 秒余量，上限 1 小时）。
    /// </summary>
    public (int Code, string Out, string Err) SendFile(string path, string device)
    {
        long size = 0;
        try { size = new FileInfo(path).Length; } catch { }

        var sizeMb = size / (1024.0 * 1024.0);
        var timeoutMs = 60_000 + (int)Math.Min(sizeMb * 30_000, 3_600_000);

        return Run($"file cp {Quote(path)} {Quote(device + ":")}", timeoutMs);
    }

    private static string Quote(string value) =>
        "\"" + (value ?? "").Replace("\"", "\\\"") + "\"";

    public void Up() => StartDetached("up");

    private static void ParsePrefs(string json, TailscaleSnapshot snapshot)
    {
        var start = json.IndexOf('{');
        if (start < 0)
            return;

        using var doc = JsonDocument.Parse(json.Substring(start));
        var root = doc.RootElement;

        snapshot.AcceptDns = Bool(root, "accept-dns");
        snapshot.AcceptRoutes = Bool(root, "accept-routes");
        snapshot.ShieldsUp = Bool(root, "shields-up");
        snapshot.AutoUpdate = Bool(root, "auto-update");
        snapshot.Unattended = Bool(root, "unattended");
        snapshot.ExitNodeAllowLanAccess = Bool(root, "exit-node-allow-lan-access");
        snapshot.AdvertiseExitNode = Bool(root, "advertise-exit-node");
        snapshot.RunSsh = Bool(root, "ssh");
        snapshot.WebClient = Bool(root, "webclient");
        snapshot.HostnameSetting = Str(root, "hostname");
        snapshot.ExitNodeIp = Str(root, "exit-node");
    }

    private static TailscaleSnapshot ParseStatus(string json)
    {
        var snapshot = new TailscaleSnapshot();

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        snapshot.BackendState = Str(root, "BackendState");
        snapshot.AuthUrl = Str(root, "AuthURL");
        snapshot.Version = Str(root, "Version");
        snapshot.Health = StrList(root, "Health");
        snapshot.HasUser = root.TryGetProperty("User", out var user) &&
                           user.ValueKind == JsonValueKind.Object;

        snapshot.LoggedOut =
            string.Equals(snapshot.BackendState, "NeedsLogin", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(snapshot.BackendState, "NoState", StringComparison.OrdinalIgnoreCase);

        if (root.TryGetProperty("Self", out var self) && self.ValueKind == JsonValueKind.Object)
        {
            snapshot.HostName = Str(self, "HostName");
            snapshot.DnsName = Str(self, "DNSName");
            snapshot.SelfIps = StrList(self, "TailscaleIPs");
            snapshot.SelfOnline = Bool(self, "Online");
        }

        if (root.TryGetProperty("Peer", out var peers) && peers.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in peers.EnumerateObject())
            {
                var value = property.Value;
                if (value.ValueKind != JsonValueKind.Object)
                    continue;

                snapshot.Peers.Add(new PeerModel
                {
                    HostName = Str(value, "HostName"),
                    DnsName = Str(value, "DNSName"),
                    Os = Str(value, "OS"),
                    Ips = StrList(value, "TailscaleIPs"),
                    Online = Bool(value, "Online"),
                    Active = Bool(value, "Active"),
                    ExitNode = Bool(value, "ExitNode"),
                    ExitNodeOption = Bool(value, "ExitNodeOption"),
                    Relay = Str(value, "Relay"),
                    CurAddr = Str(value, "CurAddr"),
                    RxBytes = Long(value, "RxBytes"),
                    TxBytes = Long(value, "TxBytes"),
                    LastHandshake = Time(value, "LastHandshake"),
                    KeyExpiry = Time(value, "KeyExpiry"),
                });
            }
        }

        return snapshot;
    }

    private static string Str(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? ""
            : "";

    private static bool Bool(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True;

    private static long Long(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number &&
        value.TryGetInt64(out var number)
            ? number
            : 0;

    private static DateTimeOffset Time(JsonElement element, string name)
    {
        var text = Str(element, name);
        return DateTimeOffset.TryParse(text, out var time) ? time.ToLocalTime() : default;
    }

    private static List<string> StrList(JsonElement element, string name)
    {
        var list = new List<string>();
        if (!element.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.Array)
            return list;

        foreach (var item in value.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.String)
                list.Add(item.GetString() ?? "");
        }

        return list;
    }
}
