#region License
/*
MIT License

Copyright(c) 2023 Petteri Kautonen

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
*/
#endregion

using System.Runtime.InteropServices;
using Scintilla.NET.EtoForms;
using Scintilla.NET.EtoForms.GTK;

namespace Scintilla.NET.Eto;

/// <summary>
/// A Scintilla control wrapper for Eto.Forms.
/// Implements the <see cref="ScintillaControl" />
/// </summary>
/// <seealso cref="ScintillaControl" />
public class Scintilla: ScintillaControl
{
    private static bool initialized;

    /// <summary>
    /// Initialization of the Scintilla control for the appropriate Eto platform.
    /// </summary>
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

    private IScintillaControl BaseControl => (IScintillaControl)Handler;

    /// <summary>
    /// Gets the Lexilla library access.
    /// </summary>
    /// <value>The lexilla library access.</value>
    public ILexilla Lexilla => BaseControl.Lexilla;
}