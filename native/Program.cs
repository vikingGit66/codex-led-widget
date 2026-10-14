using System.Windows.Forms;

namespace CodexQuotaWidgetLite;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();
        Application.Run(new QuotaForm());
    }
}
