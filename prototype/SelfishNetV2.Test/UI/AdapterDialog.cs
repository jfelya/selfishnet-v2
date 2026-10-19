// FREE MODEL TEST PROTOTYPE — adapter selector dialog.

using SelfishNetV2.Test.Core;
using SelfishNetV2.Test.Services;

namespace SelfishNetV2.Test.UI;

public sealed class AdapterDialog : Form
{
    private readonly ComboBox _combo = new() { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Top };
    private readonly Label _prompt = new() { AutoSize = true, Dock = DockStyle.Top, Padding = new Padding(0, 0, 0, 6) };
    private readonly Label _ip = new() { AutoSize = true, Dock = DockStyle.Top };
    private readonly Label _gw = new() { AutoSize = true, Dock = DockStyle.Top };
    private readonly Label _fwd = new() { AutoSize = true, Dock = DockStyle.Top, Padding = new Padding(0, 6, 0, 6) };
    private readonly Button _ok = new() { DialogResult = DialogResult.OK };
    private readonly Button _cancel = new() { DialogResult = DialogResult.Cancel };
    private readonly List<AdapterInfo> _adapters;

    public AdapterInfo? Selected =>
        _combo.SelectedIndex >= 0 && _combo.SelectedIndex < _adapters.Count ? _adapters[_combo.SelectedIndex] : null;

    public AdapterDialog(List<AdapterInfo> adapters, string? preselectId)
    {
        _adapters = adapters;
        Text = LocalizationManager.Get("AdapterDialogTitle");
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false; MinimizeBox = false;
        ShowInTaskbar = false;
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(460, 220);

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(12), RowCount = 6, ColumnCount = 1 };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, FlowDirection = FlowDirection.RightToLeft, AutoSize = true };
        buttons.Controls.Add(_ok); buttons.Controls.Add(_cancel);
        AcceptButton = _ok; CancelButton = _cancel;

        layout.Controls.Add(_prompt, 0, 0);
        layout.Controls.Add(_combo, 0, 1);
        layout.Controls.Add(_ip, 0, 2);
        layout.Controls.Add(_gw, 0, 3);
        layout.Controls.Add(_fwd, 0, 4);
        layout.Controls.Add(buttons, 0, 5);
        Controls.Add(layout);

        foreach (var a in adapters)
            _combo.Items.Add($"{a.Name} — {a.LocalIp}");

        int idx = 0;
        if (preselectId != null)
        {
            int found = adapters.FindIndex(a => a.Id == preselectId);
            if (found >= 0) idx = found;
        }
        if (_combo.Items.Count > 0) _combo.SelectedIndex = idx;
        _combo.SelectedIndexChanged += (_, _) => RefreshDetails();

        LocalizationManager.OnLanguageChanged += ApplyLanguage;
        ApplyLanguage();
        RefreshDetails();
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        BackColor = Color.White;
        Font = Win11Theme.PreferredFont(9f);
    }

    private void ApplyLanguage()
    {
        Text = LocalizationManager.Get("AdapterDialogTitle");
        _prompt.Text = LocalizationManager.Get("AdapterSelectPrompt");
        _ok.Text = LocalizationManager.Get("BtnOK");
        _cancel.Text = LocalizationManager.Get("BtnCancel");
        RefreshDetails();
    }

    private void RefreshDetails()
    {
        var s = Selected;
        _ip.Text = LocalizationManager.Get("AdapterIP", s?.LocalIp.ToString() ?? "-");
        _gw.Text = LocalizationManager.Get("AdapterGateway", s?.GatewayIp?.ToString() ?? "-");
        _fwd.Text = NetworkInterfaces.IsForwardingEnabled()
            ? LocalizationManager.Get("AdapterForwardingOn")
            : LocalizationManager.Get("AdapterForwardingOff");
    }

    protected override void OnClosed(EventArgs e)
    {
        LocalizationManager.OnLanguageChanged -= ApplyLanguage;
        base.OnClosed(e);
    }
}
