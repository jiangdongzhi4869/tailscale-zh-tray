using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace TailscaleZHTray;

internal sealed class PickPeerForm : Form
{
    private readonly ListBox _list = new();

    public PeerModel SelectedPeer { get; private set; }

    public PickPeerForm(IEnumerable<PeerModel> peers)
    {
        Text = "选择目标设备";
        ClientSize = new Size(440, 360);
        MinimumSize = new Size(360, 280);
        StartPosition = FormStartPosition.CenterParent;
        Font = new Font("Microsoft YaHei UI", 9F);
        AutoScaleMode = AutoScaleMode.Dpi;

        var items = peers.ToList();

        _list.Dock = DockStyle.Fill;
        _list.IntegralHeight = false;
        foreach (var peer in items)
        {
            _list.Items.Add(new PeerListItem(peer));
        }
        if (_list.Items.Count > 0)
            _list.SelectedIndex = 0;

        var panel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(14, 12, 14, 8) };
        panel.Controls.Add(_list);

        var bar = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 48,
            FlowDirection = FlowDirection.RightToLeft,
            Padding = new Padding(14, 8, 14, 8)
        };

        var cancel = new Button { Text = "取消", Width = 80, Height = 30, DialogResult = DialogResult.Cancel };
        var ok = new Button { Text = "确定", Width = 80, Height = 30, DialogResult = DialogResult.OK };
        ok.Click += (_, _) =>
        {
            if (_list.SelectedItem is PeerListItem item)
                SelectedPeer = item.Peer;
        };

        bar.Controls.Add(cancel);
        bar.Controls.Add(ok);

        Controls.Add(panel);
        Controls.Add(bar);

        AcceptButton = ok;
        CancelButton = cancel;
    }

    private sealed class PeerListItem
    {
        public PeerModel Peer { get; }

        public PeerListItem(PeerModel peer) => Peer = peer;

        public override string ToString()
        {
            var ip = string.IsNullOrWhiteSpace(Peer.PrimaryIp) ? "无 IP" : Peer.PrimaryIp;
            var state = Peer.Online ? "在线" : "离线";
            return $"{Peer.DisplayName}    {ip}    {state}";
        }
    }
}
