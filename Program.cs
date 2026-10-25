using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace TailscaleZHTray;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        if (args.Length > 0 && string.Equals(args[0], "--selftest", StringComparison.OrdinalIgnoreCase))
        {
            RunSelfTest();
            return;
        }

        using var mutex = new Mutex(true, "TailscaleZHTray_SingleInstance_v1", out var createdNew);
        if (!createdNew)
        {
            MessageBox.Show(
                "Tailscale 中文托盘已经在运行了，请检查系统托盘区。",
                "Tailscale 中文托盘",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        Application.SetHighDpiMode(HighDpiMode.SystemAware);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        // 任何 UI 线程异常都交给 ThreadException 处理，不让进程直接退出
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);

        Application.ThreadException += (_, e) => Logger.Log("UI 线程异常: " + e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Logger.Log("未处理异常: " + e.ExceptionObject);

        var showWindowOnStart = args.Any(a =>
            a.Equals("--window", StringComparison.OrdinalIgnoreCase));

        Application.Run(new TrayAppContext(showWindowOnStart));
    }

    private static void RunSelfTest()
    {
        var sb = new StringBuilder();
        var client = new TailscaleClient();

        sb.AppendLine("time=" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
        sb.AppendLine("exe=" + (client.ExePath ?? "(not found)"));

        try
        {
            var snapshot = client.Fetch();
            sb.AppendLine($"backend={snapshot.BackendState}");
            sb.AppendLine($"loggedOut={snapshot.LoggedOut}");
            sb.AppendLine($"host={snapshot.HostName}  ips={string.Join(",", snapshot.SelfIps)}");
            sb.AppendLine($"exitNodeIp={(string.IsNullOrEmpty(snapshot.ExitNodeIp) ? "(none)" : snapshot.ExitNodeIp)}  exitNodeName={(string.IsNullOrEmpty(snapshot.ExitNodeName) ? "(none)" : snapshot.ExitNodeName)}");
            sb.AppendLine($"authUrl={(string.IsNullOrEmpty(snapshot.AuthUrl) ? "(none)" : snapshot.AuthUrl)}");
            sb.AppendLine($"version={snapshot.Version}");
            sb.AppendLine("health=" + string.Join(" | ", snapshot.Health.Select(TrayAppContext.TranslateHealth)));
            sb.AppendLine(
                $"prefs: acceptDns={snapshot.AcceptDns} acceptRoutes={snapshot.AcceptRoutes} " +
                $"shieldsUp={snapshot.ShieldsUp} autoUpdate={snapshot.AutoUpdate} " +
                $"unattended={snapshot.Unattended} exitNodeAllowLan={snapshot.ExitNodeAllowLanAccess} " +
                $"advertiseExitNode={snapshot.AdvertiseExitNode} ssh={snapshot.RunSsh}");
            sb.AppendLine("peers=" + snapshot.Peers.Count);

            foreach (var peer in snapshot.Peers.Take(10))
            {
                sb.AppendLine(
                    $"  - {peer.DisplayName} | {peer.PrimaryIp} | {peer.Os} | online={peer.Online} | exitOption={peer.ExitNodeOption} | exit={peer.ExitNode}");
            }

            var (stateText, _, iconKey) = TrayAppContext.StateInfo(snapshot, true);
            sb.AppendLine($"uiState={stateText}  icon={iconKey}");
        }
        catch (Exception ex)
        {
            sb.AppendLine("ERROR: " + ex);
            Logger.Log("selftest error: " + ex);
        }

        Directory.CreateDirectory(Logger.Dir);
        File.WriteAllText(Path.Combine(Logger.Dir, "selftest.txt"), sb.ToString(), new UTF8Encoding(false));
    }
}
