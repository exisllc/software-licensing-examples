using Acme.Licensing;
using System.Diagnostics;

namespace AcmeNotes;

/// <summary>
/// A pretend note editor. The editor is enabled only while the license (or trial) allows it;
/// the bar at the top shows the license state and the actions that make sense for it.
/// </summary>
public sealed class MainForm : Form
{
    private readonly LicenseService _license;
    private readonly LicenseSettings _settings;

    private readonly Label _status = new() { AutoSize = true, Padding = new Padding(0, 6, 12, 0) };
    private readonly Button _activate = new() { Text = "Enter license key…", AutoSize = true };
    private readonly Button _buy = new() { Text = "Buy", AutoSize = true };
    private readonly Button _deactivate = new() { Text = "Deactivate this PC", AutoSize = true };
    private readonly Button _retry = new() { Text = "Retry", AutoSize = true };
    private readonly Label _update = new() { AutoSize = true, Visible = false, Padding = new Padding(0, 6, 0, 0), ForeColor = Color.RoyalBlue };
    private readonly TextBox _editor = new() { Multiline = true, Dock = DockStyle.Fill, ScrollBars = ScrollBars.Vertical,
        Font = new Font("Segoe UI", 11), Text = "Welcome to Acme Notes.\r\n\r\nThis editor only works while you have a license or an active trial." };

    public MainForm(LicenseService license, LicenseSettings settings)
    {
        _license = license;
        _settings = settings;
        Text = $"Acme Notes {LicenseService.AppVersion}";
        Size = new Size(760, 480);
        StartPosition = FormStartPosition.CenterScreen;

        var bar = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(8) };
        bar.Controls.AddRange([_status, _activate, _buy, _deactivate, _retry, _update]);
        Controls.Add(_editor);
        Controls.Add(bar);

        _activate.Click += async (_, _) => await ActivateAsync();
        _buy.Click += (_, _) => Open(_settings.BuyUrl);
        _deactivate.Click += async (_, _) => await DeactivateAsync();
        _retry.Click += async (_, _) => await RefreshLicenseAsync();
        Shown += async (_, _) => await RefreshLicenseAsync();   // check on every start
    }

    private async Task RefreshLicenseAsync()
    {
        _status.Text = "Checking license…";
        Show(await _license.CheckAsync());
    }

    private void Show(LicenseState s)
    {
        _status.Text = s.Message;
        _status.ForeColor = s.CanUseApp ? (s.Offline ? Color.DarkOrange : Color.DarkGreen) : Color.Firebrick;
        _editor.Enabled = s.CanUseApp;

        _activate.Visible = s.Kind is not LicenseKind.Licensed and not LicenseKind.UpdateRequired;
        _buy.Visible = s.Kind is LicenseKind.Trial or LicenseKind.TrialEnded or LicenseKind.Expired or LicenseKind.NotLicensed;
        _buy.Text = s.Kind == LicenseKind.Expired ? "Renew" : "Buy";
        _deactivate.Visible = s.Kind == LicenseKind.Licensed && !s.Offline;
        _retry.Visible = s.Kind == LicenseKind.NeedsConnection || s.Offline;

        _update.Visible = s.UpdateAvailable != null;
        _update.Text = s.Kind == LicenseKind.UpdateRequired ? "Please install the latest version." : $"Version {s.UpdateAvailable} is available.";
    }

    private async Task ActivateAsync()
    {
        using var dlg = new ActivateDialog(_license.StoredKey);
        if (dlg.ShowDialog(this) != DialogResult.OK) return;

        _status.Text = "Activating…";
        var (ok, message) = await _license.ActivateAsync(dlg.Key, dlg.Email);
        MessageBox.Show(this, message, "Activation", MessageBoxButtons.OK, ok ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
        await RefreshLicenseAsync();
    }

    private async Task DeactivateAsync()
    {
        if (MessageBox.Show(this, "Deactivate Acme Notes on this computer? You can then activate the key on another one.",
                "Deactivate", MessageBoxButtons.OKCancel) != DialogResult.OK) return;
        var (ok, message) = await _license.DeactivateAsync();
        MessageBox.Show(this, message, "Deactivate", MessageBoxButtons.OK, ok ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
        await RefreshLicenseAsync();
    }

    private static void Open(string url)
    {
        if (!string.IsNullOrWhiteSpace(url)) Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
    }
}
