using Acme.Licensing;
namespace AcmeNotes;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();

        var settings = LicenseSettings.Load();
        if (settings.Problem() is { } problem)
        {
            MessageBox.Show(problem, "Acme Notes: configuration", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        Application.Run(new MainForm(new LicenseService(settings, "Acme Notes"), settings));
    }
}
