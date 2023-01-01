using Eto;
using Eto.Forms;
using Scintilla.NET.EtoForms;
using Scintilla.NET.EtoForms.GTK;
using TestApplication;

public static class Program
{
    [STAThread]
    public static void Main()
    {
        new Application().Run(new FormMain());
    }
}

