using Eto;
using Eto.Forms;

namespace Scintilla.NET.EtoForms;

[Handler(typeof(IScintillaControl))]
public class ScintillaControl : Control
{
    new IScintillaControl Handler => (IScintillaControl)base.Handler;
}