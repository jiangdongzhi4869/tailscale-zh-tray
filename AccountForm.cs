using System;
using System.Collections.Generic;
using System.Drawing;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace TailscaleZHTray;

internal sealed class AccountForm : Form
{
    private readonly TailscaleClient _client;

    private readonly ListView _list = new();
    private readonly Button _btnSwitch = new();
    private readonly Button _btnAdd = new();
    private readonly Button _btnRefresh = new();
    private readonly Label _hint = new();

    public AccountForm(TailscaleClient client)
    {
        _client = client;

        Text = "账号管理";
        ClientSize = new Size(620, 420);
        MinimumSize = new Size(520, 340);
        StartPosition = FormStartPosition.CenterParent;
        Font = new Font("Microsoft YaHei UI", 9F);

        var table = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3
        };
        table.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        table.RowStyles.Add(new RowStyle(SizeType.Absolute, 50));
        table.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));

        var listPanel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(14, 12, 14, 4) };

        _list.Dock = DockStyle.Fill;
        _list.View = View.Details;
        _list.FullRowSelect = true;
        _list.MultiSelect = false;
        _list.HideSelection = false;
        _list.Columns.Add("账号", 220);
        _list.Columns.Add("Tailnet", 220);
        _list.Columns.Add("ID", 80);
        _list.Columns.Add("当前", 60);

        listPanel.Controls.Add(_list);

        var bar = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(14, 8, 14, 6),
            WrapContents = false
        };

        _btnSwitch.Text = "切换账号";
        _btnSwitch.Width = 110;
        _btnSwitch.Height = 30;
        _btnSwitch.Click += async (_, _) => await SwitchSelectedAsync();

        _btnAdd.Text = "添加账号…";
        _btnAdd.Width = 110;
        _btnAdd.Height = 30;
        _btnAdd.Click += (_, _) => AddAccount();

        _btnRefresh.Text = "刷新";
        _btnRefresh.Width = 80;
        _btnRefresh.Height = 30;
        _btnRefresh.Click += async (_, _) => await LoadProfilesAsync();

        bar.Controls.Add(_btnSwitch);
        bar.Controls.Add(_btnAdd);
        bar.Controls.Add(_btnRefresh);

        _hint.Dock = DockStyle.Fill;
        _hint.Padding = new Padding(16, 10, 16, 8);
        _hint.ForeColor = Color.Gray;
        _hint.Text = "切换账号会重新连接；「添加账号」会发起一次登录流程，请按浏览器提示操作。";

        table.Controls.Add(listPanel, 0, 0);
        table.Controls.Add(bar, 0, 1);
        table.Controls.Add(_hint, 0, 2);
        Controls.Add(table);

        Shown += async (_, _) => await LoadProfilesAsync();
    }

    private async Task LoadProfilesAsync()
    {
        _btnRefresh.Enabled = false;
        _btnSwitch.Enabled = false;

        var result = await Task.Run(() => _client.ListProfiles());

        _btnRefresh.Enabled = true;
        _btnSwitch.Enabled = true;

        _list.BeginUpdate();
        try
        {
            _list.Items.Clear();

            if (result.Code != 0)
            {
                _hint.Text = "读取账号失败：" +
                             (string.IsNullOrWhiteSpace(result.Err) ? result.Out : result.Err);
                return;
            }

            var start = result.Out.IndexOf('[');
            if (start < 0)
            {
                _hint.Text = "没有返回账号列表。";
                return;
            }

            using var doc = JsonDocument.Parse(result.Out.Substring(start));
            var count = 0;

            foreach (var item in doc.RootElement.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object)
                    continue;

                var id = GetString(item, "id");
                var nickname = GetString(item, "nickname");
                var account = GetString(item, "account");
                var tailnet = GetString(item, "tailnet");
                var selected = item.TryGetProperty("selected", out var sel) &&
                               sel.ValueKind == JsonValueKind.True;

                var name = string.IsNullOrWhiteSpace(nickname) ? account : nickname;

                var row = new ListViewItem(name) { Tag = id };
                row.SubItems.Add(tailnet);
                row.SubItems.Add(id);
                row.SubItems.Add(selected ? "是" : "");
                if (selected)
                {
                    row.Font = new Font(_list.Font, FontStyle.Bold);
                }

                _list.Items.Add(row);
                count++;
            }

            _hint.Text = count == 0
                ? "当前没有已登录的账号。"
                : "切换账号会重新连接；「添加账号」会发起一次登录流程，请按浏览器提示操作。";
        }
        catch (Exception ex)
        {
            _hint.Text = "解析账号列表失败：" + ex.Message;
        }
        finally
        {
            _list.EndUpdate();
        }
    }

    private static string GetString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? ""
            : "";

    private async Task SwitchSelectedAsync()
    {
        if (_list.SelectedItems.Count == 0)
        {
            MessageBox.Show(this, "先选中一个账号。", "账号管理",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var id = _list.SelectedItems[0].Tag as string;
        if (string.IsNullOrWhiteSpace(id))
            return;

        _btnSwitch.Enabled = false;
        var result = await Task.Run(() => _client.SwitchProfile(id));
        _btnSwitch.Enabled = true;

        if (result.Code != 0)
        {
            MessageBox.Show(this,
                (string.IsNullOrWhiteSpace(result.Err) ? result.Out : result.Err).Trim(),
                "切换账号失败", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        await LoadProfilesAsync();
    }

    private void AddAccount()
    {
        var answer = MessageBox.Show(
            this,
            "将发起一次登录流程，浏览器会自动打开 Tailscale 授权页。\n" +
            "如果当前账号已登录，这可能会新增一个账号配置。\n\n继续吗？",
            "添加账号",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Question);

        if (answer != DialogResult.Yes)
            return;

        _client.LoginNewAccount();
        MessageBox.Show(this,
            "已发起登录。请在浏览器里完成授权，然后点「刷新」查看账号列表。",
            "添加账号", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }
}
