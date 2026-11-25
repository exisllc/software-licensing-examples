namespace AcmeNotes;

/// <summary>Asks for the license key and the buyer's email.</summary>
public sealed class ActivateDialog : Form
{
    private readonly TextBox _key = new() { Width = 320, PlaceholderText = "XXXX-XXXX-XXXX-XXXX" };
    private readonly TextBox _email = new() { Width = 320, PlaceholderText = "you@example.com" };

    public string Key => _key.Text;
    public string Email => _email.Text;

    public ActivateDialog(string? currentKey)
    {
        Text = "Activate Acme Notes";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Padding = new Padding(12);
        _key.Text = currentKey ?? "";

        var ok = new Button { Text = "Activate", DialogResult = DialogResult.OK };
        var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel };
        AcceptButton = ok;
        CancelButton = cancel;

        var layout = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true };
        layout.Controls.AddRange([
            new Label { Text = "License key", AutoSize = true }, _key,
            new Label { Text = "Email used to buy", AutoSize = true }, _email,
        ]);
        var buttons = new FlowLayoutPanel { AutoSize = true };
        buttons.Controls.AddRange([ok, cancel]);
        layout.Controls.Add(buttons);
        Controls.Add(layout);
    }
}
