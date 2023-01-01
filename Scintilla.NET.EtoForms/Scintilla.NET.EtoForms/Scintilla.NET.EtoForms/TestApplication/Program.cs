using Eto.Forms;
using TestApplication;
using Xceed.Wpf.Toolkit.Core.Converters;

public static class Program
{
    [STAThread]
    public static void Main()
    {
        new Application().Run(new FormMain());
    }
}

