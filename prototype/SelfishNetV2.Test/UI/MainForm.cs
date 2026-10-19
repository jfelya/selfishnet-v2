// Main window: bilingual, DPI-aware, Windows 11 styled.
// Start spoofs ALL discovered devices (no per-device column). Names, caps and
// blocked flags are remembered by MAC in KnownDevices across sessions.

using System.Security.Principal;
using SelfishNetV2.Test.Core;
using SelfishNetV2.Test.Services;

namespace SelfishNetV2.Test.UI;

public sealed class MainForm : Form
{
    private readonly DeviceManager _devices = new();
    private readonly ArpEngine _arp;
    private readonly PacketForwarder _forwarder;
    private readonly AppSettings _settings;
    private readonly KnownDevices _known;
    private AdapterInfo? _adapter;
    private UnitMode _units;

    private readonly ToolStrip _toolbar = new() { GripStyle = ToolStripGripStyle.Hidden };
    private readonly ToolStripButton _btnDiscover = new();
    private readonly ToolStripButton _btnSpoof = new();
    private readonly ToolStripButton _btnAdapter = new();
    private readonly ToolStripButton _btnUnits = new();
    private readonly ToolStripButton _btnAbout = new();
    private readonly ToolStripLabel _langLabel = new();
    private readonly ToolStripComboBox _cmbLanguage = new() { DropDownStyle = ComboBoxStyle.DropDownList };

    private readonly DataGridView _grid = new()
    {
        Dock = DockStyle.Fill,
        AllowUserToAddRows = false,
        AllowUserToDeleteRows = false,
        RowHeadersVisible = false,
        SelectionMode = DataGridViewSelectionMode.FullRowSelect,
        AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
    };

    private readonly StatusStrip _status = new();
    private readonly ToolStripStatusLabel _statusLabel = new();
    private readonly ToolStripStatusLabel _devicesLabel = new();
    private readonly ToolStripStatusLabel _trafficLabel = new();

    private readonly System.Windows.Forms.Timer _uiTimer = new() { Interval = 1000 };
    private readonly System.Windows.Forms.Timer _spoofTimer = new() { Interval = 5000 };
    private readonly System.Windows.Forms.Timer _evictTimer = new() { Interval = 5000 };

    private readonly Dictionary<string, DataGridViewRow> _rows = new();

    public MainForm()
    {
        _arp = new ArpEngine(_devices);
        _forwarder = new PacketForwarder(_devices, _arp);
        _settings = AppSettings.Load();
        _known = KnownDevices.Load();
        _units = SpeedFormatter.Parse(_settings.UnitMode);
        if (_settings.Language == "SpanishLA")
            LocalizationManager.SetLanguage(LocalizationManager.Language.SpanishLA);

        Text = LocalizationManager.Get("AppTitle");
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(900, 500);
        Size = new Size(1150, 620);
        AutoScaleMode = AutoScaleMode.Dpi;

        BuildToolbar();
        BuildGrid();
        _status.Items.AddRange(new ToolStripItem[] { _statusLabel, new ToolStripSeparator(), _devicesLabel, new ToolStripSeparator(), _trafficLabel });
        _status.ShowItemToolTips = true;

        var main = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 3, ColumnCount = 1, BackColor = Color.White };
        main.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        main.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        main.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        main.Controls.Add(_toolbar, 0, 0);
        main.Controls.Add(_grid, 0, 1);
        main.Controls.Add(_status, 0, 2);
        Controls.Add(main);

        _devices.DeviceAdded += d => { _known.ApplyTo(d); BeginInvoke(new Action(SyncRow)); };
        _devices.DeviceRemoved += _ => BeginInvoke(new Action(SyncRow));
        _devices.Changed += () => BeginInvoke(new Action(UpdateCounts));
        LocalizationManager.OnLanguageChanged += ApplyLanguage;

        _uiTimer.Tick += (_, _) => OnUiTick();
        _spoofTimer.Tick += (_, _) => OnSpoofTick();
        _evictTimer.Tick += (_, _) => OnEvictTick();

        _arp.Error += msg => BeginInvoke(new Action(() =>
            MessageBox.Show(this, msg, Text, MessageBoxButtons.OK, MessageBoxIcon.Error)));
        _forwarder.Error += msg => BeginInvoke(new Action(() =>
            MessageBox.Show(this, msg, Text, MessageBoxButtons.OK, MessageBoxIcon.Error)));

        FormClosing += OnClosing;
        ApplyLanguage();

        if (!DriverCheck.IsAvailable)
            MessageBox.Show(this, LocalizationManager.Get("MsgDriverNotFound"), Text,
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
        else
            _statusLabel.Text = $"{LocalizationManager.Get("StatusReady")} ({DriverCheck.BackendName})";
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        Win11Theme.Apply(this);
    }

    // ----- construction -----

    private void BuildToolbar()
    {
        _btnDiscover.Click += (_, _) => OnDiscover();
        _btnSpoof.Click += (_, _) => OnToggleSpoof();
        _btnAdapter.Click += (_, _) => OnPickAdapter();
        _btnUnits.Click += (_, _) => OnToggleUnits();
        _btnUnits.ToolTipText = LocalizationManager.Get("BtnUnitsTooltip");
        _btnAbout.Click += (_, _) => MessageBox.Show(this, LocalizationManager.Get("AboutText"),
            LocalizationManager.Get("BtnAbout"), MessageBoxButtons.OK, MessageBoxIcon.Information);

        _cmbLanguage.Items.AddRange(new object[] { "English", "Español (Latam)" });
        _cmbLanguage.SelectedIndex = LocalizationManager.CurrentLanguage == LocalizationManager.Language.English ? 0 : 1;
        _cmbLanguage.SelectedIndexChanged += (_, _) =>
        {
            LocalizationManager.SetLanguage(_cmbLanguage.SelectedIndex == 0
                ? LocalizationManager.Language.English
                : LocalizationManager.Language.SpanishLA);
            _settings.Language = LocalizationManager.CurrentLanguage.ToString();
            _settings.Save();
        };

        _toolbar.Items.AddRange(new ToolStripItem[]
        {
            _btnDiscover, _btnSpoof, new ToolStripSeparator(),
            _btnAdapter, _btnUnits, _btnAbout, new ToolStripSeparator(),
            _langLabel, _cmbLanguage,
        });
    }

    private void BuildGrid()
    {
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Name" });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "IP", ReadOnly = true });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "MAC", ReadOnly = true });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Down", ReadOnly = true });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Up", ReadOnly = true });
        _grid.Columns.Add(new NumericUpDownColumn { Name = "DownCap" });
        _grid.Columns.Add(new NumericUpDownColumn { Name = "UpCap" });
        _grid.Columns.Add(new DataGridViewCheckBoxColumn { Name = "Block" });
        _grid.Columns["Name"]!.ToolTipText = LocalizationManager.Get("NameColumnTooltip");
        _grid.CellValueChanged += OnCellChanged;
        _grid.CurrentCellDirtyStateChanged += (_, _) =>
        {
            if (_grid.IsCurrentCellDirty && (_grid.CurrentCell is DataGridViewCheckBoxCell ||
                                             _grid.CurrentCell is NumericUpDownCell))
                _grid.CommitEdit(DataGridViewDataErrorContexts.Commit);
        };
        _grid.DataError += (_, e) => { e.ThrowException = false; };
        // Typing a limit then clicking anywhere (button, another cell) commits it —
        // no Enter key required.
        _grid.Leave += (_, _) => CommitPendingEdits();
        _grid.CellLeave += (_, e) =>
        {
            if (e.RowIndex >= 0 && _grid.IsCurrentCellDirty)
                CommitPendingEdits();
        };
    }

    /// <summary>Flushes a half-typed limit spinner into the grid so the value
    /// applies even when the user never pressed Enter.</summary>
    private void CommitPendingEdits()
    {
        try
        {
            if (_grid.EditingControl is NumericUpDownEditingControl nud)
                nud.FlushText();
            if (_grid.IsCurrentCellDirty)
                _grid.CommitEdit(DataGridViewDataErrorContexts.Commit);
            if (_grid.IsCurrentCellInEditMode)
                _grid.EndEdit();
        }
        catch { /* best-effort */ }
    }

    // ----- localization + units -----

    private void ApplyLanguage()
    {
        Text = LocalizationManager.Get("AppTitle");
        _btnDiscover.Text = LocalizationManager.Get("BtnDiscover");
        _btnDiscover.ToolTipText = LocalizationManager.Get("BtnDiscoverTooltip");
        RefreshSpoofButton();
        _btnAdapter.Text = LocalizationManager.Get("BtnAdapter");
        _btnAdapter.ToolTipText = LocalizationManager.Get("BtnAdapterTooltip");
        _btnUnits.Text = LocalizationManager.Get("BtnUnits", SpeedFormatter.CapUnit(_units));
        _btnUnits.ToolTipText = LocalizationManager.Get("BtnUnitsTooltip");
        _btnAbout.Text = LocalizationManager.Get("BtnAbout");
        _langLabel.Text = LocalizationManager.Get("MenuLanguage") + ":";

        var cols = _grid.Columns;
        cols["Name"]!.HeaderText = LocalizationManager.Get("ColPCName");
        cols["Name"]!.ToolTipText = LocalizationManager.Get("NameColumnTooltip");
        cols["IP"]!.HeaderText = LocalizationManager.Get("ColPCIP");
        cols["MAC"]!.HeaderText = LocalizationManager.Get("ColPCMac");
        cols["Down"]!.HeaderText = $"{LocalizationManager.Get("SpeedDownName")} ({SpeedFormatter.CapUnit(_units)})";
        cols["Up"]!.HeaderText = $"{LocalizationManager.Get("SpeedUpName")} ({SpeedFormatter.CapUnit(_units)})";
        cols["DownCap"]!.HeaderText = $"{LocalizationManager.Get("CapDownName")} ({SpeedFormatter.CapUnit(_units)})";
        cols["UpCap"]!.HeaderText = $"{LocalizationManager.Get("CapUpName")} ({SpeedFormatter.CapUnit(_units)})";
        cols["Block"]!.HeaderText = LocalizationManager.Get("ColBlock");

        if (!_forwarder.Running)
            _statusLabel.Text = LocalizationManager.Get("StatusReady");
        UpdateCounts();
        SyncRow();
    }

    private void OnToggleUnits()
    {
        CommitPendingEdits();
        _units = _units == UnitMode.Bytes ? UnitMode.Bits : UnitMode.Bytes;
        _settings.UnitMode = _units.ToString();
        _settings.Save();
        ApplyLanguage(); // rebuilds headers + refreshes cap spinners in the new unit
    }

    // ----- grid sync -----

    private static string DisplayName(Device dev)
    {
        string name = !string.IsNullOrWhiteSpace(dev.CustomName) ? dev.CustomName! : dev.Name;
        if (string.IsNullOrWhiteSpace(name)) name = dev.Ip.ToString();
        if (dev.IsLocalPc) name += $" [{LocalizationManager.Get("LocalHost")}]";
        else if (dev.IsGateway) name += $" [{LocalizationManager.Get("Gateway")}]";
        return name;
    }

    private static string StripSuffix(string text)
    {
        foreach (string suffix in new[]
        {
            $" [{LocalizationManager.Get("LocalHost")}]",
            $" [{LocalizationManager.Get("Gateway")}]",
        })
        {
            if (text.EndsWith(suffix, StringComparison.Ordinal))
                return text[..^suffix.Length].Trim();
        }
        // Also strip the other language's suffixes in case of a mid-edit language switch.
        foreach (string suffix in new[] { " [Your PC (Local)]", " [Default Gateway (Router)]",
                                          " [Tu Equipo (Local)]", " [Puerta de Enlace (Router)]" })
        {
            if (text.EndsWith(suffix, StringComparison.Ordinal))
                return text[..^suffix.Length].Trim();
        }
        return text.Trim();
    }

    private bool IsEditingCell(DataGridViewRow row, string columnName)
    {
        if (!_grid.IsCurrentCellInEditMode || _grid.CurrentCell == null) return false;
        return _grid.CurrentCell.RowIndex == row.Index &&
               _grid.Columns[_grid.CurrentCell.ColumnIndex].Name == columnName;
    }

    private void SyncRow()
    {
        if (IsDisposed || _grid.IsDisposed) return;
        var seen = new HashSet<string>();
        foreach (var dev in _devices.Devices)
        {
            string key = dev.Ip.ToString();
            seen.Add(key);
            if (!_rows.TryGetValue(key, out var row))
            {
                int idx = _grid.Rows.Add();
                row = _grid.Rows[idx];
                row.Tag = dev;
                _rows[key] = row;
            }
            if (!IsEditingCell(row, "Name"))
                row.Cells["Name"].Value = DisplayName(dev);
            row.Cells["IP"].Value = dev.Ip.ToString();
            row.Cells["MAC"].Value = dev.Mac.ToString();
            row.Cells["Down"].Value = SpeedFormatter.FormatBytes(dev.LastDownBytes, _units);
            row.Cells["Up"].Value = SpeedFormatter.FormatBytes(dev.LastUpBytes, _units);
            if (!IsEditingCell(row, "DownCap"))
                row.Cells["DownCap"].Value = SpeedFormatter.ToCapDisplay(dev.CapDownBytesPerSec, _units);
            if (!IsEditingCell(row, "UpCap"))
                row.Cells["UpCap"].Value = SpeedFormatter.ToCapDisplay(dev.CapUpBytesPerSec, _units);
            row.Cells["Block"].Value = !dev.Redirect;
        }
        foreach (var key in _rows.Keys.ToList())
            if (!seen.Contains(key))
            {
                var row = _rows[key];
                _grid.Rows.Remove(row);
                _rows.Remove(key);
            }
        UpdateCounts();
    }

    private void OnCellChanged(object? sender, DataGridViewCellEventArgs e)
    {
        if (e.RowIndex < 0 || e.ColumnIndex < 0) return;
        var row = _grid.Rows[e.RowIndex];
        if (row.Tag is not Device dev) return;
        string col = _grid.Columns[e.ColumnIndex].Name;
        try
        {
            switch (col)
            {
                case "Name":
                {
                    string text = StripSuffix(Convert.ToString(row.Cells["Name"].Value) ?? "");
                    dev.CustomName = string.IsNullOrEmpty(text) ? null : text;
                    _known.SetName(dev.Mac, dev.CustomName, dev.Ip.ToString());
                    row.Cells["Name"].Value = DisplayName(dev);
                    break;
                }
                case "DownCap":
                {
                    decimal d = Convert.ToDecimal(row.Cells["DownCap"].Value ?? 0m);
                    dev.CapDownBytesPerSec = SpeedFormatter.FromCapDisplay(Math.Max(0, d), _units);
                    _known.SetCaps(dev.Mac, dev.CapDownBytesPerSec, dev.CapUpBytesPerSec, dev.Ip.ToString());
                    break;
                }
                case "UpCap":
                {
                    decimal u = Convert.ToDecimal(row.Cells["UpCap"].Value ?? 0m);
                    dev.CapUpBytesPerSec = SpeedFormatter.FromCapDisplay(Math.Max(0, u), _units);
                    _known.SetCaps(dev.Mac, dev.CapDownBytesPerSec, dev.CapUpBytesPerSec, dev.Ip.ToString());
                    break;
                }
                case "Block":
                    dev.Redirect = !Convert.ToBoolean(row.Cells["Block"].Value ?? false);
                    _known.SetBlocked(dev.Mac, !dev.Redirect, dev.Ip.ToString());
                    break;
            }
        }
        catch { /* keep old value */ }
    }

    // ----- actions -----

    private void OnPickAdapter()
    {
        var adapters = NetworkInterfaces.GetUsableAdapters();
        if (adapters.Count == 0)
        {
            MessageBox.Show(this, LocalizationManager.Get("MsgNoAdapter"), Text,
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        using var dlg = new AdapterDialog(adapters, _settings.LastAdapterId ?? _adapter?.Id);
        if (dlg.ShowDialog(this) != DialogResult.OK || dlg.Selected == null) return;
        _adapter = dlg.Selected;
        _settings.LastAdapterId = _adapter.Id;
        _settings.Save();
        if (string.IsNullOrEmpty(_adapter.PcapName))
        {
            MessageBox.Show(this, LocalizationManager.Get("MsgDriverNotFound"), Text,
                MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }
        _devices.Clear();
        _rows.Clear();
        _grid.Rows.Clear();
        if (!_arp.Open(_adapter))
            return;
        // Learn the gateway MAC right away (original findMacRouter behavior).
        var opened = _adapter;
        if (opened.GatewayIp != null && !_arp.GatewayMacKnown())
            Task.Run(() => _arp.ResolveMac(opened.GatewayIp));
        _statusLabel.Text = LocalizationManager.Get("StatusReady");
        SyncRow();
    }

    private void EnsureAdapter()
    {
        if (_adapter != null) return;
        OnPickAdapter();
    }

    private void OnDiscover()
    {
        CommitPendingEdits();
        EnsureAdapter();
        if (_adapter == null) return;
        if (_arp.Open(_adapter))
        {
            _statusLabel.Text = LocalizationManager.Get("StatusScanning");
            _arp.StartDiscovery();
        }
    }

    /// <summary>One button: shows what it is about to do (start or stop spoofing).</summary>
    private void RefreshSpoofButton()
    {
        if (_forwarder.Running)
        {
            _btnSpoof.Text = LocalizationManager.Get("BtnSpoofStop");
            _btnSpoof.ToolTipText = LocalizationManager.Get("BtnStopTooltip");
        }
        else
        {
            _btnSpoof.Text = LocalizationManager.Get("BtnSpoofStart");
            _btnSpoof.ToolTipText = LocalizationManager.Get("BtnStartTooltip");
        }
    }

    private void OnToggleSpoof()
    {
        if (_forwarder.Running)
            StopSpoofing();
        else
            StartSpoofing();
    }

    private static bool IsAdmin()
    {
        try
        {
            using var id = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch { return false; }
    }

    private void StartSpoofing()
    {
        CommitPendingEdits();
        EnsureAdapter();
        if (_adapter == null) return;
        if (!IsAdmin())
        {
            MessageBox.Show(this, LocalizationManager.Get("MsgAdminRequired"), Text,
                MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }
        if (NetworkInterfaces.IsForwardingEnabled())
        {
            // Kernel forwarding routes traffic around our userspace shaper,
            // so limits would silently not apply.
            var cont = MessageBox.Show(this, LocalizationManager.Get("MsgForwardingOn"), Text,
                MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
            if (cont != DialogResult.Yes) return;
        }
        // The forwarder matches download traffic by the router's source MAC
        // (original-faithful), so resolve it actively like findMacRouter does.
        if (_adapter.GatewayIp != null && !_arp.GatewayMacKnown())
        {
            _statusLabel.Text = LocalizationManager.Get("StatusScanning");
            _status.Refresh();
            _arp.ResolveMac(_adapter.GatewayIp);
            if (!_arp.GatewayMacKnown())
            {
                var go = MessageBox.Show(this, LocalizationManager.Get("MsgGatewayUnknown"), Text,
                    MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
                if (go != DialogResult.Yes) return;
            }
        }
        if (!_forwarder.Start(_adapter))
            return;
        RefreshSpoofButton();
        _uiTimer.Start();
        _spoofTimer.Start();
        _evictTimer.Start();
        int n = _arp.SpoofAll(); // immediate first poison of everything
        _statusLabel.Text = LocalizationManager.Get("StatusRedirecting", n);
    }

    private void StopSpoofing()
    {
        _spoofTimer.Stop();
        _forwarder.Stop();
        RefreshSpoofButton();
        _statusLabel.Text = LocalizationManager.Get("StatusStopped");
        // Heal LAN: 35 genuine frames per device (original completeUnspoof), off-UI-thread.
        Task.Run(() => _arp.UnspoofAll()).ContinueWith(_ =>
            BeginInvoke(new Action(() =>
            {
                _statusLabel.Text = LocalizationManager.Get("StatusReady");
            })));
    }

    // ----- timers -----

    private void OnUiTick()
    {
        double totalDown = 0, totalUp = 0;
        foreach (var dev in _devices.Devices)
        {
            dev.LastDownBytes = dev.ResetDown();
            dev.LastUpBytes = dev.ResetUp();
            totalDown += dev.LastDownBytes;
            totalUp += dev.LastUpBytes;
        }
        SyncRow();
        _trafficLabel.Text = LocalizationManager.Get("TotalTraffic",
            totalDown / 1024, totalUp / 1024);
    }

    private void OnSpoofTick()
    {
        if (!_forwarder.Running) return;
        Task.Run(() =>
        {
            int n = _arp.SpoofAll();
            BeginInvoke(new Action(() =>
                _statusLabel.Text = LocalizationManager.Get("StatusRedirecting", n)));
        });
    }

    private void OnEvictTick()
    {
        var now = DateTime.UtcNow;
        foreach (var dev in _devices.Devices.ToList())
        {
            if (dev.IsLocalPc || dev.IsGateway) continue;
            double silent = (now - dev.LastSeenUtc).TotalSeconds;
            if (silent > 350) // ~5.8 min (original timer2 eviction)
                _devices.Remove(dev);
        }
    }

    private void UpdateCounts()
    {
        if (IsDisposed) return;
        _devicesLabel.Text = LocalizationManager.Get("DevicesCount", _devices.Count);
    }

    private void OnClosing(object? sender, FormClosingEventArgs e)
    {
        if (_forwarder.Running)
        {
            var r = MessageBox.Show(this, LocalizationManager.Get("MsgConfirmQuit"), Text,
                MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (r != DialogResult.Yes) { e.Cancel = true; return; }
            _statusLabel.Text = LocalizationManager.Get("MsgRestoringArp");
            _status.Refresh();
            try { _arp.UnspoofAll(); }
            catch { /* best-effort on exit */ }
        }
        _uiTimer.Stop(); _spoofTimer.Stop(); _evictTimer.Stop();
        try { _forwarder.Stop(); } catch { }
        try { _arp.Close(); } catch { }
        _settings.Save();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            LocalizationManager.OnLanguageChanged -= ApplyLanguage;
            _uiTimer.Dispose(); _spoofTimer.Dispose(); _evictTimer.Dispose();
            _forwarder.Dispose(); _arp.Dispose();
        }
        base.Dispose(disposing);
    }
}
