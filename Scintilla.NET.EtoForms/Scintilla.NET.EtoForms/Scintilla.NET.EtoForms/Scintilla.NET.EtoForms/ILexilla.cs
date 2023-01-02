namespace Scintilla.NET.EtoForms;

public interface ILexilla
{
    int LexerCount();

    string GetLexerName(uint index);
}