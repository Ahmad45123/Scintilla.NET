using System.Runtime.InteropServices;
using Scintilla.NET.EtoForms;
using Scintilla.NET.EtoForms.GTK;

namespace Scintilla.NET.Eto;

public class Scintilla: ScintillaControl
{
    private static bool initialized;
    
    public static void PlatformInitialize()
    {
        if (initialized)
        {
            return;
        }
        
        if (!initialized && RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            global::Eto.Platform.Instance.Add<IScintillaControl>(() => new ScintillaControlHandler());
            initialized = true;
        }        
    }
}