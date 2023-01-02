using System.Runtime.InteropServices;

namespace Scintilla.NET.EtoForms.GTK;

internal class Lexilla: ILexilla
{

    public int LexerCount()
    {
        return GetLexerCount();
    }

    public string GetLexerName(uint index)
    {
        var pointer = Marshal.AllocHGlobal(1024);
        GetLexerName(index, pointer, 1024);
        return Marshal.PtrToStringAnsi(pointer);
    }

    [DllImport("liblexilla", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode)]    
    public static extern int GetLexerCount();

    [DllImport("liblexilla", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode)]    
    public static extern void GetLexerName(uint index, IntPtr name, int buflength);
}