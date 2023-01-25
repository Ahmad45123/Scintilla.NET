﻿using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Design;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;
using System.Windows.Forms.VisualStyles;
using Scintilla.NET.Abstractions;
using Scintilla.NET.Abstractions.Enumerations;
using Scintilla.NET.Abstractions.Interfaces;
using Scintilla.NET.Abstractions.Structs;
using Scintilla.NET.WinForms.EventArguments;
using static Scintilla.NET.Abstractions.ScintillaConstants;
using static Scintilla.NET.Abstractions.Classes.ScintillaApiStructs;
using Bitmap = System.Drawing.Bitmap;
using TabDrawMode = Scintilla.NET.Abstractions.Enumerations.TabDrawMode;

namespace Scintilla.NET.WinForms;

/// <summary>
/// Represents a Scintilla editor control.
/// </summary>
[Docking(DockingBehavior.Ask)]
public class Scintilla : Control,
    IScintillaApi<MarkerCollection, StyleCollection, IndicatorCollection, LineCollection, MarginCollection,
        SelectionCollection, SCNotificationEventArgs, Marker, Style, Indicator, Line, Margin, Selection, Bitmap, Color>,
    IScintillaProperties<Color>,
    IScintillaCollectionProperties<MarkerCollection, StyleCollection, IndicatorCollection, LineCollection, MarginCollection,
        SelectionCollection, SCNotificationEventArgs, Marker, Style, Indicator, Line, Margin, Selection, Bitmap, Color>,
    IScintillaMethods<Color, Keys, Bitmap>,
    IScintillaEvents<MarkerCollection, StyleCollection, IndicatorCollection, LineCollection, MarginCollection,
        SelectionCollection, SCNotificationEventArgs, Marker, Style, Indicator, Line, Margin, Selection, Bitmap, Color, Keys, 
        AutoCSelectionEventArgs, BeforeModificationEventArgs, ChangeAnnotationEventArgs, CharAddedEventArgs, DoubleClickEventArgs,
        DwellEventArgs, CallTipClickEventArgs, HotspotClickEventArgs, IndicatorClickEventArgs, IndicatorReleaseEventArgs,
        InsertCheckEventArgs, MarginClickEventArgs, NeedShownEventArgs, StyleNeededEventArgs, UpdateUIEventArgs, SCNotificationEventArgs>
{
    static Scintilla()
    {
        string basePath = LocateNativeDllDirectory();
        modulePathScintilla = Path.Combine(basePath, "Scintilla.dll");
        modulePathLexilla = Path.Combine(basePath, "Lexilla.dll");

        try
        {
            var info = FileVersionInfo.GetVersionInfo(modulePathScintilla);
            scintillaVersion = info.ProductVersion ?? info.FileVersion;
            info = FileVersionInfo.GetVersionInfo(modulePathLexilla);
            lexillaVersion = info.ProductVersion ?? info.FileVersion;
        }
        catch
        {
            scintillaVersion = "ERROR";
            lexillaVersion = "ERROR";
        }
    }

    private static string LocateNativeDllDirectory()
    {
        string platform = (IntPtr.Size == 4 ? "x86" : "x64");
        string managedLocation = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
        var basePath = Path.Combine(managedLocation, platform);

        // Directory exists when application is built, but not if Scintilla is dragged on in the Winforms designer
        if (Directory.Exists(basePath))
        {
            return basePath;
        }

        // In the Designer, look for the native dlls in the nuget package
        return Path.Combine(managedLocation, "..", "..", "build", platform);
    }

    #region Fields

    // WM_DESTROY workaround
    private static bool? reparentAll;
    private bool reparent;

    // Static module data
    private static readonly string modulePathScintilla;

    private static readonly string modulePathLexilla;

    private static IntPtr moduleHandle;
    private static IntPtr lexillaHandle;
    private static Lexilla lexilla;

    // Events
    private static readonly object scNotificationEventKey = new object();
    private static readonly object insertCheckEventKey = new object();
    private static readonly object beforeInsertEventKey = new object();
    private static readonly object beforeDeleteEventKey = new object();
    private static readonly object insertEventKey = new object();
    private static readonly object deleteEventKey = new object();
    private static readonly object updateUIEventKey = new object();
    private static readonly object modifyAttemptEventKey = new object();
    private static readonly object styleNeededEventKey = new object();
    private static readonly object savePointReachedEventKey = new object();
    private static readonly object savePointLeftEventKey = new object();
    private static readonly object changeAnnotationEventKey = new object();
    private static readonly object marginClickEventKey = new object();
    private static readonly object marginRightClickEventKey = new object();
    private static readonly object charAddedEventKey = new object();
    private static readonly object autoCSelectionEventKey = new object();
    private static readonly object autoCCompletedEventKey = new object();
    private static readonly object autoCCancelledEventKey = new object();
    private static readonly object autoCCharDeletedEventKey = new object();
    private static readonly object dwellStartEventKey = new object();
    private static readonly object callTipClickEventKey = new object();
    private static readonly object dwellEndEventKey = new object();
    private static readonly object borderStyleChangedEventKey = new object();
    private static readonly object doubleClickEventKey = new object();
    private static readonly object paintedEventKey = new object();
    private static readonly object needShownEventKey = new object();
    private static readonly object hotspotClickEventKey = new object();
    private static readonly object hotspotDoubleClickEventKey = new object();
    private static readonly object hotspotReleaseClickEventKey = new object();
    private static readonly object indicatorClickEventKey = new object();
    private static readonly object indicatorReleaseEventKey = new object();
    private static readonly object zoomChangedEventKey = new object();

    // The goods
    private IntPtr sciPtr;
    private BorderStyle borderStyle;

    // Set style
    private int stylingPosition;
    private int stylingBytePosition;

    // Modified event optimization
    private int? cachedPosition;
    private string cachedText;

    // Double-click
    private bool doubleClick;

    // Pinned dataDwellStart
    private IntPtr fillUpChars;

    // For highlight calculations
    private string lastCallTip = string.Empty;
    #endregion Fields

    #region Methods
        
    /// <summary>
    /// Sets the name of the lexer by its name.
    /// </summary>
    /// <param name="lexerName">Name of the lexer.</param>
    /// <returns><c>true</c> if the lexer was successfully set, <c>false</c> otherwise.</returns>
    private bool SetLexerByName(string lexerName)
    {
        var ptr = Lexilla.CreateLexer(lexerName);

        if (ptr == IntPtr.Zero)
        {
            return false;
        }

        DirectMessage(SCI_SETILEXER, IntPtr.Zero, ptr);

        return true;
    }

    /// <summary>
    /// Increases the reference count of the specified document by 1.
    /// </summary>
    /// <param name="document">The document reference count to increase.</param>
    public void AddRefDocument(Document document)
    {
        var ptr = document.Value;
        DirectMessage(SCI_ADDREFDOCUMENT, IntPtr.Zero, ptr);
    }

    /// <summary>
    /// Adds an additional selection range to the existing main selection.
    /// </summary>
    /// <param name="caret">The zero-based document position to end the selection.</param>
    /// <param name="anchor">The zero-based document position to start the selection.</param>
    /// <remarks>A main selection must first have been set by a call to <see cref="SetSelection" />.</remarks>
    public void AddSelection(int caret, int anchor)
    {
        var textLength = TextLength;
        caret = Helpers.Clamp(caret, 0, textLength);
        anchor = Helpers.Clamp(anchor, 0, textLength);

        caret = Lines.CharToBytePosition(caret);
        anchor = Lines.CharToBytePosition(anchor);

        DirectMessage(SCI_ADDSELECTION, new IntPtr(caret), new IntPtr(anchor));
    }

    /// <summary>
    /// Inserts the specified text at the current caret position.
    /// </summary>
    /// <param name="text">The text to insert at the current caret position.</param>
    /// <remarks>The caret position is set to the end of the inserted text, but it is not scrolled into view.</remarks>
    public unsafe void AddText(string text)
    {
        var bytes = Helpers.GetBytes(text ?? string.Empty, Encoding, zeroTerminated: false);
        fixed (byte* bp = bytes)
            DirectMessage(SCI_ADDTEXT, new IntPtr(bytes.Length), new IntPtr(bp));
    }

    /// <summary>
    /// Allocates some number of sub-styles for a particular base style. Substyles are allocated contiguously.
    /// </summary>
    /// <param name="styleBase">The lexer style integer</param>
    /// <param name="numberStyles">The amount of sub-styles to allocate</param>
    /// <returns>Returns the first sub-style number allocated.</returns>
    public int AllocateSubStyles(int styleBase, int numberStyles)
    {
        return this.DirectMessage(SCI_ALLOCATESUBSTYLES, new IntPtr(styleBase), new IntPtr(numberStyles)).ToInt32();
    }

    /// <summary>
    /// Removes the annotation text for every <see cref="Line" /> in the document.
    /// </summary>
    public void AnnotationClearAll()
    {
        DirectMessage(SCI_ANNOTATIONCLEARALL);
    }

    /// <summary>
    /// Adds the specified text to the end of the document.
    /// </summary>
    /// <param name="text">The text to add to the document.</param>
    /// <remarks>The current selection is not changed and the new text is not scrolled into view.</remarks>
    public unsafe void AppendText(string text)
    {
        var bytes = Helpers.GetBytes(text ?? string.Empty, Encoding, zeroTerminated: false);
        fixed (byte* bp = bytes)
            DirectMessage(SCI_APPENDTEXT, new IntPtr(bytes.Length), new IntPtr(bp));
    }

    /// <summary>
    /// Assigns the specified key definition to a <see cref="Scintilla" /> command.
    /// </summary>
    /// <param name="keyDefinition">The key combination to bind.</param>
    /// <param name="sciCommand">The command to assign.</param>
    public void AssignCmdKey(Keys keyDefinition, Command sciCommand)
    {
        var keys = Helpers.TranslateKeys(keyDefinition);
        DirectMessage(SCI_ASSIGNCMDKEY, new IntPtr(keys), new IntPtr((int)sciCommand));
    }

    /// <summary>
    /// Cancels any displayed autocompletion list.
    /// </summary>
    /// <seealso cref="AutoCStops" />
    public void AutoCCancel()
    {
        DirectMessage(SCI_AUTOCCANCEL);
    }

    /// <summary>
    /// Triggers completion of the current autocompletion word.
    /// </summary>
    public void AutoCComplete()
    {
        DirectMessage(SCI_AUTOCCOMPLETE);
    }

    /// <summary>
    /// Selects an item in the autocompletion list.
    /// </summary>
    /// <param name="select">
    /// The autocompletion word to select.
    /// If found, the word in the autocompletion list is selected and the index can be obtained by calling <see cref="AutoCCurrent" />.
    /// If not found, the behavior is determined by <see cref="AutoCAutoHide" />.
    /// </param>
    /// <remarks>
    /// Comparisons are performed according to the <see cref="AutoCIgnoreCase" /> property
    /// and will match the first word starting with <paramref name="select" />.
    /// </remarks>
    /// <seealso cref="AutoCCurrent" />
    /// <seealso cref="AutoCAutoHide" />
    /// <seealso cref="AutoCIgnoreCase" />
    public unsafe void AutoCSelect(string select)
    {
        var bytes = Helpers.GetBytes(select, Encoding, zeroTerminated: true);
        fixed (byte* bp = bytes)
            DirectMessage(SCI_AUTOCSELECT, IntPtr.Zero, new IntPtr(bp));
    }

    /// <summary>
    /// Sets the characters that, when typed, cause the autocompletion item to be added to the document.
    /// </summary>
    /// <param name="chars">A string of characters that trigger autocompletion. The default is null.</param>
    /// <remarks>Common fillup characters are '(', '[', and '.' depending on the language.</remarks>
    public unsafe void AutoCSetFillUps(string chars)
    {
        // Apparently Scintilla doesn't make a copy of our fill up string; it just keeps a pointer to it....
        // That means we need to keep a copy of the string around for the life of the control AND put it
        // in a place where it won't get moved by the GC.

        if (chars == null)
            chars = string.Empty;

        if (fillUpChars != IntPtr.Zero)
        {
            Marshal.FreeHGlobal(fillUpChars);
            fillUpChars = IntPtr.Zero;
        }

        var count = (Encoding.GetByteCount(chars) + 1);
        IntPtr newFillUpChars = Marshal.AllocHGlobal(count);
        fixed (char* ch = chars)
            Encoding.GetBytes(ch, chars.Length, (byte*)newFillUpChars, count);

        ((byte*)newFillUpChars)[count - 1] = 0; // Null terminate
        fillUpChars = newFillUpChars;

        // var str = new String((sbyte*)fillUpChars, 0, count, Encoding);

        DirectMessage(SCI_AUTOCSETFILLUPS, IntPtr.Zero, fillUpChars);
    }

    /// <summary>
    /// Displays an auto completion list.
    /// </summary>
    /// <param name="lenEntered">The number of characters already entered to match on.</param>
    /// <param name="list">A list of autocompletion words separated by the <see cref="AutoCSeparator" /> character.</param>
    public unsafe void AutoCShow(int lenEntered, string list)
    {
        if (string.IsNullOrEmpty(list))
            return;

        lenEntered = Helpers.ClampMin(lenEntered, 0);
        if (lenEntered > 0)
        {
            // Convert to bytes by counting back the specified number of characters
            var endPos = DirectMessage(SCI_GETCURRENTPOS).ToInt32();
            var startPos = endPos;
            for (int i = 0; i < lenEntered; i++)
                startPos = DirectMessage(SCI_POSITIONRELATIVE, new IntPtr(startPos), new IntPtr(-1)).ToInt32();

            lenEntered = (endPos - startPos);
        }

        var bytes = Helpers.GetBytes(list, Encoding, zeroTerminated: true);
        fixed (byte* bp = bytes)
            DirectMessage(SCI_AUTOCSHOW, new IntPtr(lenEntered), new IntPtr(bp));
    }

    /// <summary>
    /// Specifies the characters that will automatically cancel autocompletion without the need to call <see cref="AutoCCancel" />.
    /// </summary>
    /// <param name="chars">A String of the characters that will cancel autocompletion. The default is empty.</param>
    /// <remarks>Characters specified should be limited to printable ASCII characters.</remarks>
    public unsafe void AutoCStops(string chars)
    {
        var bytes = Helpers.GetBytes(chars ?? string.Empty, Encoding.ASCII, zeroTerminated: true);
        fixed (byte* bp = bytes)
            DirectMessage(SCI_AUTOCSTOPS, IntPtr.Zero, new IntPtr(bp));
    }

    /// <summary>
    /// Marks the beginning of a set of actions that should be treated as a single undo action.
    /// </summary>
    /// <remarks>A call to <see cref="BeginUndoAction" /> should be followed by a call to <see cref="EndUndoAction" />.</remarks>
    /// <seealso cref="EndUndoAction" />
    public void BeginUndoAction()
    {
        DirectMessage(SCI_BEGINUNDOACTION);
    }

    /// <summary>
    /// Styles the specified character position with the <see cref="Style.BraceBad" /> style when there is an unmatched brace.
    /// </summary>
    /// <param name="position">The zero-based document position of the unmatched brace character or <seealso cref="IApiConstants.InvalidPosition"/> to remove the highlight.</param>
    public void BraceBadLight(int position)
    {
        position = Helpers.Clamp(position, -1, TextLength);
        if (position > 0)
            position = Lines.CharToBytePosition(position);

        DirectMessage(SCI_BRACEBADLIGHT, new IntPtr(position));
    }

    /// <summary>
    /// Styles the specified character positions with the <see cref="Style.BraceLight" /> style.
    /// </summary>
    /// <param name="position1">The zero-based document position of the open brace character.</param>
    /// <param name="position2">The zero-based document position of the close brace character.</param>
    /// <remarks>Brace highlighting can be removed by specifying <see cref="IApiConstants.InvalidPosition" /> for <paramref name="position1" /> and <paramref name="position2" />.</remarks>
    /// <seealso cref="HighlightGuide" />
    public void BraceHighlight(int position1, int position2)
    {
        var textLength = TextLength;

        position1 = Helpers.Clamp(position1, -1, textLength);
        if (position1 > 0)
            position1 = Lines.CharToBytePosition(position1);

        position2 = Helpers.Clamp(position2, -1, textLength);
        if (position2 > 0)
            position2 = Lines.CharToBytePosition(position2);

        DirectMessage(SCI_BRACEHIGHLIGHT, new IntPtr(position1), new IntPtr(position2));
    }

    /// <summary>
    /// Finds a corresponding matching brace starting at the position specified.
    /// The brace characters handled are '(', ')', '[', ']', '{', '}', '&lt;', and '&gt;'.
    /// </summary>
    /// <param name="position">The zero-based document position of a brace character to start the search from for a matching brace character.</param>
    /// <returns>The zero-based document position of the corresponding matching brace or <see cref="IApiConstants.InvalidPosition" /> it no matching brace could be found.</returns>
    /// <remarks>A match only occurs if the style of the matching brace is the same as the starting brace. Nested braces are handled correctly.</remarks>
    public int BraceMatch(int position)
    {
        position = Helpers.Clamp(position, 0, TextLength);
        position = Lines.CharToBytePosition(position);

        var match = DirectMessage(SCI_BRACEMATCH, new IntPtr(position), IntPtr.Zero).ToInt32();
        if (match > 0)
            match = Lines.ByteToCharPosition(match);

        return match;
    }

    /// <summary>
    /// Cancels the display of a call tip window.
    /// </summary>
    public void CallTipCancel()
    {
        DirectMessage(SCI_CALLTIPCANCEL);
    }

    /// <summary>
    /// Sets the color of highlighted text in a call tip.
    /// </summary>
    /// <param name="color">The new highlight text Color. The default is dark blue.</param>
    public void CallTipSetForeHlt(Color color)
    {
        var colour = ColorTranslator.ToWin32(color);
        DirectMessage(SCI_CALLTIPSETFOREHLT, new IntPtr(colour));
    }

    /// <summary>
    /// Sets the specified range of the call tip text to display in a highlighted style.
    /// </summary>
    /// <param name="hlStart">The zero-based index in the call tip text to start highlighting.</param>
    /// <param name="hlEnd">The zero-based index in the call tip text to stop highlighting (exclusive).</param>
    public unsafe void CallTipSetHlt(int hlStart, int hlEnd)
    {
        // To do the char->byte translation we need to use a cached copy of the last call tip
        hlStart = Helpers.Clamp(hlStart, 0, lastCallTip.Length);
        hlEnd = Helpers.Clamp(hlEnd, 0, lastCallTip.Length);

        fixed (char* cp = lastCallTip)
        {
            hlEnd = Encoding.GetByteCount(cp + hlStart, hlEnd - hlStart);  // The bytes between start and end
            hlStart = Encoding.GetByteCount(cp, hlStart);                  // The bytes between 0 and start
            hlEnd += hlStart;                                              // The bytes between 0 and end
        }

        DirectMessage(SCI_CALLTIPSETHLT, new IntPtr(hlStart), new IntPtr(hlEnd));
    }

    /// <summary>
    /// Determines whether to display a call tip above or below text.
    /// </summary>
    /// <param name="above">true to display above text; otherwise, false. The default is false.</param>
    public void CallTipSetPosition(bool above)
    {
        var val = (above ? new IntPtr(1) : IntPtr.Zero);
        DirectMessage(SCI_CALLTIPSETPOSITION, val);
    }

    /// <summary>
    /// Displays a call tip window.
    /// </summary>
    /// <param name="posStart">The zero-based document position where the call tip window should be aligned.</param>
    /// <param name="definition">The call tip text.</param>
    /// <remarks>
    /// Call tips can contain multiple lines separated by '\n' characters. Do not include '\r', as this will most likely print as an empty box.
    /// The '\t' character is supported and the size can be set by using <see cref="CallTipTabSize" />.
    /// </remarks>
    public unsafe void CallTipShow(int posStart, string definition)
    {
        posStart = Helpers.Clamp(posStart, 0, TextLength);
        if (definition == null)
            return;

        lastCallTip = definition;
        posStart = Lines.CharToBytePosition(posStart);
        var bytes = Helpers.GetBytes(definition, Encoding, zeroTerminated: true);
        fixed (byte* bp = bytes)
            DirectMessage(SCI_CALLTIPSHOW, new IntPtr(posStart), new IntPtr(bp));
    }

    /// <summary>
    /// Sets the call tip tab size in pixels.
    /// </summary>
    /// <param name="tabSize">The width in pixels of a tab '\t' character in a call tip. Specifying 0 disables special treatment of tabs.</param>
    public void CallTipTabSize(int tabSize)
    {
        // To support the STYLE_CALLTIP style we call SCI_CALLTIPUSESTYLE when the control is created. At
        // this point we're only adjusting the tab size. This breaks a bit with Scintilla convention, but
        // that's okay because the Scintilla convention is lame.

        tabSize = Helpers.ClampMin(tabSize, 0);
        DirectMessage(SCI_CALLTIPUSESTYLE, new IntPtr(tabSize));
    }

    /// <summary>
    /// Indicates to the current <see cref="Lexer" /> that the internal lexer state has changed in the specified
    /// range and therefore may need to be redrawn.
    /// </summary>
    /// <param name="startPos">The zero-based document position at which the lexer state change starts.</param>
    /// <param name="endPos">The zero-based document position at which the lexer state change ends.</param>
    public void ChangeLexerState(int startPos, int endPos)
    {
        var textLength = TextLength;
        startPos = Helpers.Clamp(startPos, 0, textLength);
        endPos = Helpers.Clamp(endPos, 0, textLength);

        startPos = Lines.CharToBytePosition(startPos);
        endPos = Lines.CharToBytePosition(endPos);

        DirectMessage(SCI_CHANGELEXERSTATE, new IntPtr(startPos), new IntPtr(endPos));
    }

    /// <summary>
    /// Finds the closest character position to the specified display point.
    /// </summary>
    /// <param name="x">The x pixel coordinate within the client rectangle of the control.</param>
    /// <param name="y">The y pixel coordinate within the client rectangle of the control.</param>
    /// <returns>The zero-based document position of the nearest character to the point specified.</returns>
    public int CharPositionFromPoint(int x, int y)
    {
        var pos = DirectMessage(SCI_CHARPOSITIONFROMPOINT, new IntPtr(x), new IntPtr(y)).ToInt32();
        pos = Lines.ByteToCharPosition(pos);

        return pos;
    }

    /// <summary>
    /// Finds the closest character position to the specified display point or returns -1
    /// if the point is outside the window or not close to any characters.
    /// </summary>
    /// <param name="x">The x pixel coordinate within the client rectangle of the control.</param>
    /// <param name="y">The y pixel coordinate within the client rectangle of the control.</param>
    /// <returns>The zero-based document position of the nearest character to the point specified when near a character; otherwise, -1.</returns>
    public int CharPositionFromPointClose(int x, int y)
    {
        var pos = DirectMessage(SCI_CHARPOSITIONFROMPOINTCLOSE, new IntPtr(x), new IntPtr(y)).ToInt32();
        if (pos >= 0)
            pos = Lines.ByteToCharPosition(pos);

        return pos;
    }

    /// <summary>
    /// Explicitly sets the current horizontal offset of the caret as the X position to track
    /// when the user moves the caret vertically using the up and down keys.
    /// </summary>
    /// <remarks>
    /// When not set explicitly, Scintilla automatically sets this value each time the user moves
    /// the caret horizontally.
    /// </remarks>
    public void ChooseCaretX()
    {
        DirectMessage(SCI_CHOOSECARETX);
    }

    /// <summary>
    /// Removes the selected text from the document.
    /// </summary>
    public void Clear()
    {
        DirectMessage(SCI_CLEAR);
    }

    /// <summary>
    /// Deletes all document text, unless the document is read-only.
    /// </summary>
    public void ClearAll()
    {
        DirectMessage(SCI_CLEARALL);
    }

    /// <summary>
    /// Makes the specified key definition do nothing.
    /// </summary>
    /// <param name="keyDefinition">The key combination to bind.</param>
    /// <remarks>This is equivalent to binding the keys to <see cref="Command.Null" />.</remarks>
    public void ClearCmdKey(Keys keyDefinition)
    {
        var keys = Helpers.TranslateKeys(keyDefinition);
        DirectMessage(SCI_CLEARCMDKEY, new IntPtr(keys));
    }

    /// <summary>
    /// Removes all the key definition command mappings.
    /// </summary>
    public void ClearAllCmdKeys()
    {
        DirectMessage(SCI_CLEARALLCMDKEYS);
    }

    /// <summary>
    /// Removes all styling from the document and resets the folding state.
    /// </summary>
    public void ClearDocumentStyle()
    {
        DirectMessage(SCI_CLEARDOCUMENTSTYLE);
    }

    /// <summary>
    /// Removes all images registered for autocompletion lists.
    /// </summary>
    public void ClearRegisteredImages()
    {
        DirectMessage(SCI_CLEARREGISTEREDIMAGES);
    }

    /// <summary>
    /// Sets a single empty selection at the start of the document.
    /// </summary>
    public void ClearSelections()
    {
        DirectMessage(SCI_CLEARSELECTIONS);
    }

    /// <summary>
    /// Requests that the current lexer restyle the specified range.
    /// </summary>
    /// <param name="startPos">The zero-based document position at which to start styling.</param>
    /// <param name="endPos">The zero-based document position at which to stop styling (exclusive).</param>
    /// <remarks>This will also cause fold levels in the range specified to be reset.</remarks>
    public void Colorize(int startPos, int endPos)
    {
        var textLength = TextLength;
        startPos = Helpers.Clamp(startPos, 0, textLength);
        endPos = Helpers.Clamp(endPos, 0, textLength);

        startPos = Lines.CharToBytePosition(startPos);
        endPos = Lines.CharToBytePosition(endPos);

        DirectMessage(SCI_COLOURISE, new IntPtr(startPos), new IntPtr(endPos));
    }

    /// <summary>
    /// Changes all end-of-line characters in the document to the format specified.
    /// </summary>
    /// <param name="eolMode">One of the <see cref="Eol" /> enumeration values.</param>
    public void ConvertEols(Eol eolMode)
    {
        var eol = (int)eolMode;
        DirectMessage(SCI_CONVERTEOLS, new IntPtr(eol));
    }

    /// <summary>
    /// Copies the selected text from the document and places it on the clipboard.
    /// </summary>
    public void Copy()
    {
        DirectMessage(SCI_COPY);
    }

    /// <summary>
    /// Copies the selected text from the document and places it on the clipboard.
    /// </summary>
    /// <param name="format">One of the <see cref="CopyFormat" /> enumeration values.</param>
    public void Copy(CopyFormat format)
    {
        HelperWinForms.Copy(this, format, true, false, 0, 0);
    }

    /// <summary>
    /// Copies the selected text from the document and places it on the clipboard.
    /// If the selection is empty the current line is copied.
    /// </summary>
    /// <remarks>
    /// If the selection is empty and the current line copied, an extra "MSDEVLineSelect" marker is added to the
    /// clipboard which is then used in <see cref="Paste" /> to paste the whole line before the current line.
    /// </remarks>
    public void CopyAllowLine()
    {
        DirectMessage(SCI_COPYALLOWLINE);
    }

    /// <summary>
    /// Copies the selected text from the document and places it on the clipboard.
    /// If the selection is empty the current line is copied.
    /// </summary>
    /// <param name="format">One of the <see cref="CopyFormat" /> enumeration values.</param>
    /// <remarks>
    /// If the selection is empty and the current line copied, an extra "MSDEVLineSelect" marker is added to the
    /// clipboard which is then used in <see cref="Paste" /> to paste the whole line before the current line.
    /// </remarks>
    public void CopyAllowLine(CopyFormat format)
    {
        HelperWinForms.Copy(this, format, true, true, 0, 0);
    }

    /// <summary>
    /// Copies the specified range of text to the clipboard.
    /// </summary>
    /// <param name="start">The zero-based character position in the document to start copying.</param>
    /// <param name="end">The zero-based character position (exclusive) in the document to stop copying.</param>
    public void CopyRange(int start, int end)
    {
        var textLength = TextLength;
        start = Helpers.Clamp(start, 0, textLength);
        end = Helpers.Clamp(end, 0, textLength);

        // Convert to byte positions
        start = Lines.CharToBytePosition(start);
        end = Lines.CharToBytePosition(end);

        DirectMessage(SCI_COPYRANGE, new IntPtr(start), new IntPtr(end));
    }

    /// <summary>
    /// Copies the specified range of text to the clipboard.
    /// </summary>
    /// <param name="start">The zero-based character position in the document to start copying.</param>
    /// <param name="end">The zero-based character position (exclusive) in the document to stop copying.</param>
    /// <param name="format">One of the <see cref="CopyFormat" /> enumeration values.</param>
    public void CopyRange(int start, int end, CopyFormat format)
    {
        var textLength = TextLength;
        start = Helpers.Clamp(start, 0, textLength);
        end = Helpers.Clamp(end, 0, textLength);
        if (start == end)
            return;

        // Convert to byte positions
        start = Lines.CharToBytePosition(start);
        end = Lines.CharToBytePosition(end);

        HelperWinForms.Copy(this, format, false, false, start, end);
    }

    /// <summary>
    /// Create a new, empty document.
    /// </summary>
    /// <returns>A new <see cref="Document" /> with a reference count of 1.</returns>
    /// <remarks>You are responsible for ensuring the reference count eventually reaches 0 or memory leaks will occur.</remarks>
    public Document CreateDocument()
    {
        var ptr = DirectMessage(SCI_CREATEDOCUMENT);
        return new Document { Value = ptr };
    }

    /// <summary>
    /// Creates an <see cref="ILoader" /> object capable of loading a <see cref="Document" /> on a background (non-UI) thread.
    /// </summary>
    /// <param name="length">The initial number of characters to allocate.</param>
    /// <returns>A new <see cref="ILoader" /> object, or null if the loader could not be created.</returns>
    public ILoader CreateLoader(int length)
    {
        length = Helpers.ClampMin(length, 0);
        var ptr = DirectMessage(SCI_CREATELOADER, new IntPtr(length));
        if (ptr == IntPtr.Zero)
            return null;

        return new Loader(ptr, Encoding);
    }

    /// <summary>
    /// Cuts the selected text from the document and places it on the clipboard.
    /// </summary>
    public void Cut()
    {
        DirectMessage(SCI_CUT);
    }

    /// <summary>
    /// Deletes a range of text from the document.
    /// </summary>
    /// <param name="position">The zero-based character position to start deleting.</param>
    /// <param name="length">The number of characters to delete.</param>
    public void DeleteRange(int position, int length)
    {
        var textLength = TextLength;
        position = Helpers.Clamp(position, 0, textLength);
        length = Helpers.Clamp(length, 0, textLength - position);

        // Convert to byte position/length
        var byteStartPos = Lines.CharToBytePosition(position);
        var byteEndPos = Lines.CharToBytePosition(position + length);

        DirectMessage(SCI_DELETERANGE, new IntPtr(byteStartPos), new IntPtr(byteEndPos - byteStartPos));
    }

    /// <summary>
    /// Retrieves a description of keyword sets supported by the current <see cref="Lexer" />.
    /// </summary>
    /// <returns>A String describing each keyword set separated by line breaks for the current lexer.</returns>
    public unsafe string DescribeKeywordSets()
    {
        var length = DirectMessage(SCI_DESCRIBEKEYWORDSETS).ToInt32();
        var bytes = new byte[length + 1];

        fixed (byte* bp = bytes)
            DirectMessage(SCI_DESCRIBEKEYWORDSETS, IntPtr.Zero, new IntPtr(bp));

        var str = Encoding.ASCII.GetString(bytes, 0, length);
        return str;
    }

    /// <summary>
    /// Retrieves a brief description of the specified property name for the current <see cref="Lexer" />.
    /// </summary>
    /// <param name="name">A property name supported by the current <see cref="Lexer" />.</param>
    /// <returns>A String describing the lexer property name if found; otherwise, String.Empty.</returns>
    /// <remarks>A list of supported property names for the current <see cref="Lexer" /> can be obtained by calling <see cref="PropertyNames" />.</remarks>
    public unsafe string DescribeProperty(string name)
    {
        if (String.IsNullOrEmpty(name))
            return String.Empty;

        var nameBytes = Helpers.GetBytes(name, Encoding.ASCII, zeroTerminated: true);
        fixed (byte* nb = nameBytes)
        {
            var length = DirectMessage(SCI_DESCRIBEPROPERTY, new IntPtr(nb), IntPtr.Zero).ToInt32();
            if (length == 0)
                return string.Empty;

            var descriptionBytes = new byte[length + 1];
            fixed (byte* db = descriptionBytes)
            {
                DirectMessage(SCI_DESCRIBEPROPERTY, new IntPtr(nb), new IntPtr(db));
                return Helpers.GetString(new IntPtr(db), length, Encoding.ASCII);
            }
        }
    }

    /// <inheritdoc />
    public IntPtr SetParameter(int message, IntPtr wParam, IntPtr lParam)
    {
        throw new NotImplementedException();
    }

    public IntPtr DirectMessage(int msg)
    {
        return DirectMessage(msg, IntPtr.Zero, IntPtr.Zero);
    }

    public IntPtr DirectMessage(int msg, IntPtr wParam)
    {
        return DirectMessage(msg, wParam, IntPtr.Zero);
    }

    /// <summary>
    /// Sends the specified message directly to the native Scintilla window,
    /// bypassing any managed APIs.
    /// </summary>
    /// <param name="msg">The message ID.</param>
    /// <param name="wParam">The message <c>wparam</c> field.</param>
    /// <param name="lParam">The message <c>lparam</c> field.</param>
    /// <returns>An <see cref="IntPtr"/> representing the result of the message request.</returns>
    /// <remarks>This API supports the Scintilla infrastructure and is not intended to be used directly from your code.</remarks>
    [EditorBrowsable(EditorBrowsableState.Advanced)]
    public IntPtr DirectMessage(int msg, IntPtr wParam, IntPtr lParam)
    {
        // If the control handle, ptr, direct function, etc... hasn't been created yet, it will be now.
        var result = DirectFunction(SciPointer, msg, wParam, lParam);
        return result;
    }

    /// <inheritdoc />
    public IntPtr DirectMessage(IntPtr scintillaPointer, int message, IntPtr wParam, IntPtr lParam)
    {
        return DirectFunction(scintillaPointer, message, wParam, lParam);
    }

    //private static IntPtr DirectMessage(IntPtr sciPtr, int msg, IntPtr wParam, IntPtr lParam)
    //{
    //    // Like Win32 SendMessage but directly to Scintilla
    //    var result = directFunction(sciPtr, msg, wParam, lParam);
    //    return result;
    //}

    /// <summary>
    /// Releases the unmanaged resources used by the Control and its child controls and optionally releases the managed resources.
    /// </summary>
    /// <param name="disposing">true to release both managed and unmanaged resources; false to release only unmanaged resources.</param>
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            // WM_DESTROY workaround
            if (reparent)
            {
                reparent = false;
                if (IsHandleCreated)
                    DestroyHandle();
            }

            if (fillUpChars != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(fillUpChars);
                fillUpChars = IntPtr.Zero;
            }
        }

        base.Dispose(disposing);
    }

    /// <summary>
    /// Returns the zero-based document line index from the specified display line index.
    /// </summary>
    /// <param name="displayLine">The zero-based display line index.</param>
    /// <returns>The zero-based document line index.</returns>
    /// <seealso cref="Line.DisplayIndex" />
    public int DocLineFromVisible(int displayLine)
    {
        displayLine = Helpers.Clamp(displayLine, 0, VisibleLineCount);
        return DirectMessage(SCI_DOCLINEFROMVISIBLE, new IntPtr(displayLine)).ToInt32();
    }

    /// <summary>
    /// If there are multiple selections, removes the specified selection.
    /// </summary>
    /// <param name="selection">The zero-based selection index.</param>
    /// <seealso cref="Selections" />
    public void DropSelection(int selection)
    {
        selection = Helpers.ClampMin(selection, 0);
        DirectMessage(SCI_DROPSELECTIONN, new IntPtr(selection));
    }

    /// <summary>
    /// Clears any undo or redo history.
    /// </summary>
    /// <remarks>This will also cause <see cref="SetSavePoint" /> to be called but will not raise the <see cref="SavePointReached" /> event.</remarks>
    public void EmptyUndoBuffer()
    {
        DirectMessage(SCI_EMPTYUNDOBUFFER);
    }

    /// <summary>
    /// Marks the end of a set of actions that should be treated as a single undo action.
    /// </summary>
    /// <seealso cref="BeginUndoAction" />
    public void EndUndoAction()
    {
        DirectMessage(SCI_ENDUNDOACTION);
    }

    /// <summary>
    /// Performs the specified <see cref="Scintilla" />command.
    /// </summary>
    /// <param name="sciCommand">The command to perform.</param>
    public void ExecuteCmd(Command sciCommand)
    {
        var cmd = (int)sciCommand;
        DirectMessage(cmd);
    }

    /// <summary>
    /// Performs the specified fold action on the entire document.
    /// </summary>
    /// <param name="action">One of the <see cref="FoldAction" /> enumeration values.</param>
    /// <remarks>When using <see cref="FoldAction.Toggle" /> the first fold header in the document is examined to decide whether to expand or contract.</remarks>
    public void FoldAll(FoldAction action)
    {
        DirectMessage(SCI_FOLDALL, new IntPtr((int)action));
    }

    /// <summary>
    /// Changes the appearance of fold text tags.
    /// </summary>
    /// <param name="style">One of the <see cref="FoldDisplayText" /> enumeration values.</param>
    /// <remarks>The text tag to display on a folded line can be set using <see cref="Line.ToggleFoldShowText" />.</remarks>
    /// <seealso cref="Line.ToggleFoldShowText" />.
    public void FoldDisplayTextSetStyle(FoldDisplayText style)
    {
        DirectMessage(SCI_FOLDDISPLAYTEXTSETSTYLE, new IntPtr((int)style));
    }

    /// <summary>
    /// Frees all allocated sub-styles.
    /// </summary>
    public void FreeSubStyles()
    {
        DirectMessage(SCI_FREESUBSTYLES);
    }

    /// <summary>
    /// Returns the character as the specified document position.
    /// </summary>
    /// <param name="position">The zero-based document position of the character to get.</param>
    /// <returns>The character at the specified <paramref name="position" />.</returns>
    public unsafe int GetCharAt(int position)
    {
        position = Helpers.Clamp(position, 0, TextLength);
        position = Lines.CharToBytePosition(position);

        var nextPosition = DirectMessage(SCI_POSITIONRELATIVE, new IntPtr(position), new IntPtr(1)).ToInt32();
        var length = (nextPosition - position);
        if (length <= 1)
        {
            // Position is at single-byte character
            return DirectMessage(SCI_GETCHARAT, new IntPtr(position)).ToInt32();
        }

        // Position is at multibyte character
        var bytes = new byte[length + 1];
        fixed (byte* bp = bytes)
        {
            Sci_TextRange* range = stackalloc Sci_TextRange[1];
            range->chrg.cpMin = position;
            range->chrg.cpMax = nextPosition;
            range->lpstrText = new IntPtr(bp);

            DirectMessage(SCI_GETTEXTRANGE, IntPtr.Zero, new IntPtr(range));
            var str = Helpers.GetString(new IntPtr(bp), length, Encoding);
            return str[0];
        }
    }

    /// <summary>
    /// Returns the column number of the specified document position, taking the width of tabs into account.
    /// </summary>
    /// <param name="position">The zero-based document position to get the column for.</param>
    /// <returns>The number of columns from the start of the line to the specified document <paramref name="position" />.</returns>
    public int GetColumn(int position)
    {
        position = Helpers.Clamp(position, 0, TextLength);
        position = Lines.CharToBytePosition(position);
        return DirectMessage(SCI_GETCOLUMN, new IntPtr(position)).ToInt32();
    }

    /// <summary>
    /// Returns the last document position likely to be styled correctly.
    /// </summary>
    /// <returns>The zero-based document position of the last styled character.</returns>
    public int GetEndStyled()
    {
        var pos = DirectMessage(SCI_GETENDSTYLED).ToInt32();
        return Lines.ByteToCharPosition(pos);
    }

    private static readonly string scintillaVersion;
    private static readonly string lexillaVersion;

    /// <summary>
    /// Gets the product version of the Scintilla.dll user by the control.
    /// </summary>
    public string ScintillaVersion => scintillaVersion;

    /// <summary>
    /// Gets the product version of the Lexilla.dll user by the control.
    /// </summary>
    public string LexillaVersion => lexillaVersion;

    /// <summary>
    /// Gets the Primary style associated with the given Secondary style.
    /// </summary>
    /// <param name="style">The secondary style</param>
    /// <returns>For a secondary style, return the primary style, else return the argument.</returns>
    public int GetPrimaryStyleFromStyle(int style)
    {
        return DirectMessage(SCI_GETPRIMARYSTYLEFROMSTYLE, new IntPtr(style)).ToInt32();
    }

    /// <summary>
    /// Lookup a property value for the current <see cref="Lexer" />.
    /// </summary>
    /// <param name="name">The property name to lookup.</param>
    /// <returns>
    /// A String representing the property value if found; otherwise, String.Empty.
    /// Any embedded property name macros as described in <see cref="SetProperty" /> will not be replaced (expanded).
    /// </returns>
    /// <seealso cref="GetPropertyExpanded" />
    public unsafe string GetProperty(string name)
    {
        if (String.IsNullOrEmpty(name))
            return String.Empty;

        var nameBytes = Helpers.GetBytes(name, Encoding.ASCII, zeroTerminated: true);
        fixed (byte* nb = nameBytes)
        {
            var length = DirectMessage(SCI_GETPROPERTY, new IntPtr(nb)).ToInt32();
            if (length == 0)
                return String.Empty;

            var valueBytes = new byte[length + 1];
            fixed (byte* vb = valueBytes)
            {
                DirectMessage(SCI_GETPROPERTY, new IntPtr(nb), new IntPtr(vb));
                return Helpers.GetString(new IntPtr(vb), length, Encoding.ASCII);
            }
        }
    }

    /// <summary>
    /// Lookup a property value for the current <see cref="Lexer" /> and expand any embedded property macros.
    /// </summary>
    /// <param name="name">The property name to lookup.</param>
    /// <returns>
    /// A String representing the property value if found; otherwise, String.Empty.
    /// Any embedded property name macros as described in <see cref="SetProperty" /> will be replaced (expanded).
    /// </returns>
    /// <seealso cref="GetProperty" />
    public unsafe string GetPropertyExpanded(string name)
    {
        if (String.IsNullOrEmpty(name))
            return String.Empty;

        var nameBytes = Helpers.GetBytes(name, Encoding.ASCII, zeroTerminated: true);
        fixed (byte* nb = nameBytes)
        {
            var length = DirectMessage(SCI_GETPROPERTYEXPANDED, new IntPtr(nb)).ToInt32();
            if (length == 0)
                return String.Empty;

            var valueBytes = new byte[length + 1];
            fixed (byte* vb = valueBytes)
            {
                DirectMessage(SCI_GETPROPERTYEXPANDED, new IntPtr(nb), new IntPtr(vb));
                return Helpers.GetString(new IntPtr(vb), length, Encoding.ASCII);
            }
        }
    }

    /// <summary>
    /// Lookup a property value for the current <see cref="Lexer" /> and convert it to an integer.
    /// </summary>
    /// <param name="name">The property name to lookup.</param>
    /// <param name="defaultValue">A default value to return if the property name is not found or has no value.</param>
    /// <returns>
    /// An Integer representing the property value if found;
    /// otherwise, <paramref name="defaultValue" /> if not found or the property has no value;
    /// otherwise, 0 if the property is not a number.
    /// </returns>
    public unsafe int GetPropertyInt(string name, int defaultValue)
    {
        if (String.IsNullOrEmpty(name))
            return defaultValue;

        var bytes = Helpers.GetBytes(name, Encoding.ASCII, zeroTerminated: true);
        fixed (byte* bp = bytes)
            return DirectMessage(SCI_GETPROPERTYINT, new IntPtr(bp), new IntPtr(defaultValue)).ToInt32();
    }

    /// <summary>
    /// Gets the style of the specified document position.
    /// </summary>
    /// <param name="position">The zero-based document position of the character to get the style for.</param>
    /// <returns>The zero-based <see cref="Style" /> index used at the specified <paramref name="position" />.</returns>
    public int GetStyleAt(int position)
    {
        position = Helpers.Clamp(position, 0, TextLength);
        position = Lines.CharToBytePosition(position);

        return DirectMessage(SCI_GETSTYLEAT, new IntPtr(position)).ToInt32();
    }

    /// <summary>
    /// Gets the lexer base style of a sub-style.
    /// </summary>
    /// <param name="subStyle">The integer index of the sub-style</param>
    /// <returns>Returns the base style, else returns the argument.</returns>
    public int GetStyleFromSubStyle(int subStyle)
    {
        return DirectMessage(SCI_GETSTYLEFROMSUBSTYLE, new IntPtr(subStyle)).ToInt32();
    }

    /// <summary>
    /// Gets the length of the number of sub-styles allocated for a given lexer base style.
    /// </summary>
    /// <param name="styleBase">The lexer style integer</param>
    /// <returns>Returns the length of the sub-styles allocated for a base style.</returns>
    public int GetSubStylesLength(int styleBase)
    {
        return DirectMessage(SCI_GETSUBSTYLESLENGTH, new IntPtr(styleBase)).ToInt32();
    }

    /// <summary>
    /// Gets the start index of the sub-styles for a given lexer base style.
    /// </summary>
    /// <param name="styleBase">The lexer style integer</param>
    /// <returns>Returns the start of the sub-styles allocated for a base style.</returns>
    public int GetSubStylesStart(int styleBase)
    {
        return DirectMessage(SCI_GETSUBSTYLESSTART, new IntPtr(styleBase)).ToInt32();
    }

    /// <summary>
    /// Returns the capture group text of the most recent regular expression search.
    /// </summary>
    /// <param name="tagNumber">The capture group (1 through 9) to get the text for.</param>
    /// <returns>A String containing the capture group text if it participated in the match; otherwise, an empty string.</returns>
    /// <seealso cref="SearchInTarget" />
    public unsafe string GetTag(int tagNumber)
    {
        tagNumber = Helpers.Clamp(tagNumber, 1, 9);
        var length = DirectMessage(SCI_GETTAG, new IntPtr(tagNumber), IntPtr.Zero).ToInt32();
        if (length <= 0)
            return string.Empty;

        var bytes = new byte[length + 1];
        fixed (byte* bp = bytes)
        {
            DirectMessage(SCI_GETTAG, new IntPtr(tagNumber), new IntPtr(bp));
            return Helpers.GetString(new IntPtr(bp), length, Encoding);
        }
    }

    /// <summary>
    /// Gets a range of text from the document.
    /// </summary>
    /// <param name="position">The zero-based starting character position of the range to get.</param>
    /// <param name="length">The number of characters to get.</param>
    /// <returns>A string representing the text range.</returns>
    public unsafe string GetTextRange(int position, int length)
    {
        var textLength = TextLength;
        position = Helpers.Clamp(position, 0, textLength);
        length = Helpers.Clamp(length, 0, textLength - position);

        // Convert to byte position/length
        var byteStartPos = Lines.CharToBytePosition(position);
        var byteEndPos = Lines.CharToBytePosition(position + length);

        var ptr = DirectMessage(SCI_GETRANGEPOINTER, new IntPtr(byteStartPos), new IntPtr(byteEndPos - byteStartPos));
        if (ptr == IntPtr.Zero)
            return string.Empty;

        return Helpers.GetString(ptr, (byteEndPos - byteStartPos), Encoding);
    }

    /// <summary>
    /// Gets a range of text from the document formatted as Hypertext Markup Language (HTML).
    /// </summary>
    /// <param name="position">The zero-based starting character position of the range to get.</param>
    /// <param name="length">The number of characters to get.</param>
    /// <returns>A string representing the text range formatted as HTML.</returns>
    public string GetTextRangeAsHtml(int position, int length)
    {
        var textLength = TextLength;
        position = Helpers.Clamp(position, 0, textLength);
        length = Helpers.Clamp(length, 0, textLength - position);

        var startBytePos = Lines.CharToBytePosition(position);
        var endBytePos = Lines.CharToBytePosition(position + length);

        return Helpers.GetHtml(this, startBytePos, endBytePos);
    }

    /// <returns>
    /// An object representing the version information of the native Scintilla library.
    /// </returns>
    public FileVersionInfo GetVersionInfo()
    {
        var version = FileVersionInfo.GetVersionInfo(modulePathScintilla);

        return version;
    }

    ///<summary>
    /// Gets the word from the position specified.
    /// </summary>
    /// <param name="position">The zero-based document character position to get the word from.</param>
    /// <returns>The word at the specified position.</returns>
    public string GetWordFromPosition(int position)
    {
        int startPosition = WordStartPosition(position, true);
        int endPosition = WordEndPosition(position, true);
        return GetTextRange(startPosition, endPosition - startPosition);
    }

    /// <summary>
    /// Navigates the caret to the document position specified.
    /// </summary>
    /// <param name="position">The zero-based document character position to navigate to.</param>
    /// <remarks>Any selection is discarded.</remarks>
    public void GotoPosition(int position)
    {
        position = Helpers.Clamp(position, 0, TextLength);
        position = Lines.CharToBytePosition(position);
        DirectMessage(SCI_GOTOPOS, new IntPtr(position));
    }

    /// <summary>
    /// Hides the range of lines specified.
    /// </summary>
    /// <param name="lineStart">The zero-based index of the line range to start hiding.</param>
    /// <param name="lineEnd">The zero-based index of the line range to end hiding.</param>
    /// <seealso cref="ShowLines" />
    /// <seealso cref="Line.Visible" />
    public void HideLines(int lineStart, int lineEnd)
    {
        lineStart = Helpers.Clamp(lineStart, 0, Lines.Count);
        lineEnd = Helpers.Clamp(lineEnd, lineStart, Lines.Count);

        DirectMessage(SCI_HIDELINES, new IntPtr(lineStart), new IntPtr(lineEnd));
    }

    /// <summary>
    /// Returns a bitmap representing the 32 indicators in use at the specified position.
    /// </summary>
    /// <param name="position">The zero-based character position within the document to test.</param>
    /// <returns>A bitmap indicating which of the 32 indicators are in use at the specified <paramref name="position" />.</returns>
    public uint IndicatorAllOnFor(int position)
    {
        position = Helpers.Clamp(position, 0, TextLength);
        position = Lines.CharToBytePosition(position);

        var bitmap = DirectMessage(SCI_INDICATORALLONFOR, new IntPtr(position)).ToInt32();
        return unchecked((uint)bitmap);
    }

    /// <summary>
    /// Removes the <see cref="IndicatorCurrent" /> indicator (and user-defined value) from the specified range of text.
    /// </summary>
    /// <param name="position">The zero-based character position within the document to start clearing.</param>
    /// <param name="length">The number of characters to clear.</param>
    public void IndicatorClearRange(int position, int length)
    {
        var textLength = TextLength;
        position = Helpers.Clamp(position, 0, textLength);
        length = Helpers.Clamp(length, 0, textLength - position);

        var startPos = Lines.CharToBytePosition(position);
        var endPos = Lines.CharToBytePosition(position + length);

        DirectMessage(SCI_INDICATORCLEARRANGE, new IntPtr(startPos), new IntPtr(endPos - startPos));
    }

    /// <summary>
    /// Adds the <see cref="IndicatorCurrent" /> indicator and <see cref="IndicatorValue" /> value to the specified range of text.
    /// </summary>
    /// <param name="position">The zero-based character position within the document to start filling.</param>
    /// <param name="length">The number of characters to fill.</param>
    public void IndicatorFillRange(int position, int length)
    {
        var textLength = TextLength;
        position = Helpers.Clamp(position, 0, textLength);
        length = Helpers.Clamp(length, 0, textLength - position);

        var startPos = Lines.CharToBytePosition(position);
        var endPos = Lines.CharToBytePosition(position + length);

        DirectMessage(SCI_INDICATORFILLRANGE, new IntPtr(startPos), new IntPtr(endPos - startPos));
    }

    private void InitDocument(Eol eolMode = Eol.CrLf, bool useTabs = false, int tabWidth = 4, int indentWidth = 0)
    {
        // Document.h
        // These properties are stored in the Scintilla document, not the control; meaning, when
        // a user changes documents these properties will change. If the user changes to a new
        // document, these properties will reset to defaults. That can cause confusion for our users
        // who would expect their tab settings, for example, to be unchanged based on which document
        // they have selected into the control. This is where we carry forward any of the user's
        // current settings -- and our default overrides -- to a new document.

        DirectMessage(SCI_SETCODEPAGE, new IntPtr(SC_CP_UTF8));
        DirectMessage(SCI_SETUNDOCOLLECTION, new IntPtr(1));
        DirectMessage(SCI_SETEOLMODE, new IntPtr((int)eolMode));
        DirectMessage(SCI_SETUSETABS, useTabs ? new IntPtr(1) : IntPtr.Zero);
        DirectMessage(SCI_SETTABWIDTH, new IntPtr(tabWidth));
        DirectMessage(SCI_SETINDENT, new IntPtr(indentWidth));
    }

    /// <summary>
    /// Inserts text at the specified position.
    /// </summary>
    /// <param name="position">The zero-based character position to insert the text. Specify -1 to use the current caret position.</param>
    /// <param name="text">The text to insert into the document.</param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="position" /> less than zero and not equal to -1. -or-
    /// <paramref name="position" /> is greater than the document length.
    /// </exception>
    /// <remarks>No scrolling is performed.</remarks>
    public unsafe void InsertText(int position, string text)
    {
        if (position < -1)
            throw new ArgumentOutOfRangeException("position", "Position must be greater or equal to zero, or -1.");

        if (position != -1)
        {
            var textLength = TextLength;
            if (position > textLength)
                throw new ArgumentOutOfRangeException("position", "Position cannot exceed document length.");

            position = Lines.CharToBytePosition(position);
        }

        fixed (byte* bp = Helpers.GetBytes(text ?? string.Empty, Encoding, zeroTerminated: true))
            DirectMessage(SCI_INSERTTEXT, new IntPtr(position), new IntPtr(bp));
    }

    /// <summary>
    /// Determines whether the specified <paramref name="start" /> and <paramref name="end" /> positions are
    /// at the beginning and end of a word, respectively.
    /// </summary>
    /// <param name="start">The zero-based document position of the possible word start.</param>
    /// <param name="end">The zero-based document position of the possible word end.</param>
    /// <returns>
    /// true if <paramref name="start" /> and <paramref name="end" /> are at the beginning and end of a word, respectively;
    /// otherwise, false.
    /// </returns>
    /// <remarks>
    /// This method does not check whether there is whitespace in the search range,
    /// only that the <paramref name="start" /> and <paramref name="end" /> are at word boundaries.
    /// </remarks>
    public bool IsRangeWord(int start, int end)
    {
        var textLength = TextLength;
        start = Helpers.Clamp(start, 0, textLength);
        end = Helpers.Clamp(end, 0, textLength);

        start = Lines.CharToBytePosition(start);
        end = Lines.CharToBytePosition(end);

        return (DirectMessage(SCI_ISRANGEWORD, new IntPtr(start), new IntPtr(end)) != IntPtr.Zero);
    }

    /// <summary>
    /// Returns the line that contains the document position specified.
    /// </summary>
    /// <param name="position">The zero-based document character position.</param>
    /// <returns>The zero-based document line index containing the character <paramref name="position" />.</returns>
    public int LineFromPosition(int position)
    {
        position = Helpers.Clamp(position, 0, TextLength);
        return Lines.LineFromCharPosition(position);
    }

    /// <summary>
    /// Scrolls the display the number of lines and columns specified.
    /// </summary>
    /// <param name="lines">The number of lines to scroll.</param>
    /// <param name="columns">The number of columns to scroll.</param>
    /// <remarks>
    /// Negative values scroll in the opposite direction.
    /// A column is the width in pixels of a space character in the <see cref="Style.Default" /> style.
    /// </remarks>
    public void LineScroll(int lines, int columns)
    {
        DirectMessage(SCI_LINESCROLL, new IntPtr(columns), new IntPtr(lines));
    }

    /// <summary>
    /// Loads a <see cref="Scintilla" /> compatible lexer from an external DLL.
    /// </summary>
    /// <param name="path">The path to the external lexer DLL.</param>
    public unsafe void LoadLexerLibrary(string path)
    {
        if (String.IsNullOrEmpty(path))
            return;

        var bytes = Helpers.GetBytes(path, Encoding.Default, zeroTerminated: true);
        fixed (byte* bp = bytes)
            DirectMessage(SCI_LOADLEXERLIBRARY, IntPtr.Zero, new IntPtr(bp));
    }

    /// <summary>
    /// Removes the specified marker from all lines.
    /// </summary>
    /// <param name="marker">The zero-based <see cref="Marker" /> index to remove from all lines, or -1 to remove all markers from all lines.</param>
    public void MarkerDeleteAll(int marker)
    {
        marker = Helpers.Clamp(marker, -1, Markers.Count - 1);
        DirectMessage(SCI_MARKERDELETEALL, new IntPtr(marker));
    }

    /// <summary>
    /// Searches the document for the marker handle and deletes the marker if found.
    /// </summary>
    /// <param name="markerHandle">The <see cref="MarkerHandle" /> created by a previous call to <see cref="Line.MarkerAdd" /> of the marker to delete.</param>
    public void MarkerDeleteHandle(MarkerHandle markerHandle)
    {
        DirectMessage(SCI_MARKERDELETEHANDLE, markerHandle.Value);
    }

    /// <summary>
    /// Enable or disable highlighting of the current folding block.
    /// </summary>
    /// <param name="enabled">true to highlight the current folding block; otherwise, false.</param>
    public void MarkerEnableHighlight(bool enabled)
    {
        var val = (enabled ? new IntPtr(1) : IntPtr.Zero);
        DirectMessage(SCI_MARKERENABLEHIGHLIGHT, val);
    }

    /// <summary>
    /// Searches the document for the marker handle and returns the line number containing the marker if found.
    /// </summary>
    /// <param name="markerHandle">The <see cref="MarkerHandle" /> created by a previous call to <see cref="Line.MarkerAdd" /> of the marker to search for.</param>
    /// <returns>If found, the zero-based line index containing the marker; otherwise, -1.</returns>
    public int MarkerLineFromHandle(MarkerHandle markerHandle)
    {
        return DirectMessage(SCI_MARKERLINEFROMHANDLE, markerHandle.Value).ToInt32();
    }

    /// <summary>
    /// Specifies the long line indicator column number and color when <see cref="EdgeMode" /> is <see cref="EdgeMode.MultiLine" />.
    /// </summary>
    /// <param name="column">The zero-based column number to indicate.</param>
    /// <param name="edgeColor">The color of the vertical long line indicator.</param>
    /// <remarks>A column is defined as the width of a space character in the <see cref="Style.Default" /> style.</remarks>
    /// <seealso cref="MultiEdgeClearAll" />
    public void MultiEdgeAddLine(int column, Color edgeColor)
    {
        column = Helpers.ClampMin(column, 0);
        var colour = ColorTranslator.ToWin32(edgeColor);

        DirectMessage(SCI_MULTIEDGEADDLINE, new IntPtr(column), new IntPtr(colour));
    }

    /// <summary>
    /// Removes all the long line column indicators specified using <seealso cref="MultiEdgeAddLine" />.
    /// </summary>
    /// <seealso cref="MultiEdgeAddLine" />
    public void MultiEdgeClearAll()
    {
        DirectMessage(SCI_MULTIEDGECLEARALL);
    }

    /// <summary>
    /// Searches for all instances of the main selection within the <see cref="TargetStart" /> and <see cref="TargetEnd" />
    /// range and adds any matches to the selection.
    /// </summary>
    /// <remarks>
    /// The <see cref="SearchFlags" /> property is respected when searching, allowing additional
    /// selections to match on different case sensitivity and word search options.
    /// </remarks>
    /// <seealso cref="MultipleSelectAddNext" />
    public void MultipleSelectAddEach()
    {
        DirectMessage(SCI_MULTIPLESELECTADDEACH);
    }

    /// <summary>
    /// Searches for the next instance of the main selection within the <see cref="TargetStart" /> and <see cref="TargetEnd" />
    /// range and adds any match to the selection.
    /// </summary>
    /// <remarks>
    /// The <see cref="SearchFlags" /> property is respected when searching, allowing additional
    /// selections to match on different case sensitivity and word search options.
    /// </remarks>
    /// <seealso cref="MultipleSelectAddNext" />
    public void MultipleSelectAddNext()
    {
        DirectMessage(SCI_MULTIPLESELECTADDNEXT);
    }

    /// <summary>
    /// Raises the <see cref="AutoCCancelled" /> event.
    /// </summary>
    /// <param name="e">An EventArgs that contains the event data.</param>
    protected virtual void OnAutoCCancelled(System.EventArgs e)
    {
        var handler = Events[autoCCancelledEventKey] as EventHandler<System.EventArgs>;
        if (handler != null)
            handler(this, e);
    }

    /// <summary>
    /// Raises the <see cref="AutoCCharDeleted" /> event.
    /// </summary>
    /// <param name="e">An EventArgs that contains the event data.</param>
    protected virtual void OnAutoCCharDeleted(System.EventArgs e)
    {
        var handler = Events[autoCCharDeletedEventKey] as EventHandler<System.EventArgs>;
        if (handler != null)
            handler(this, e);
    }

    /// <summary>
    /// Raises the <see cref="AutoCCompleted" /> event.
    /// </summary>
    /// <param name="e">An <see cref="AutoCSelectionEventArgs" /> that contains the event data.</param>
    protected virtual void OnAutoCCompleted(AutoCSelectionEventArgs e)
    {
        var handler = Events[autoCCompletedEventKey] as EventHandler<AutoCSelectionEventArgs>;
        if (handler != null)
            handler(this, e);
    }

    /// <summary>
    /// Raises the <see cref="AutoCSelection" /> event.
    /// </summary>
    /// <param name="e">An <see cref="AutoCSelectionEventArgs" /> that contains the event data.</param>
    protected virtual void OnAutoCSelection(AutoCSelectionEventArgs e)
    {
        var handler = Events[autoCSelectionEventKey] as EventHandler<AutoCSelectionEventArgs>;
        if (handler != null)
            handler(this, e);
    }

    /// <summary>
    /// Raises the <see cref="BeforeDelete" /> event.
    /// </summary>
    /// <param name="e">A <see cref="BeforeModificationEventArgs" /> that contains the event data.</param>
    protected virtual void OnBeforeDelete(BeforeModificationEventArgs e)
    {
        var handler = Events[beforeDeleteEventKey] as EventHandler<BeforeModificationEventArgs>;
        if (handler != null)
            handler(this, e);
    }

    /// <summary>
    /// Raises the <see cref="BeforeInsert" /> event.
    /// </summary>
    /// <param name="e">A <see cref="BeforeModificationEventArgs" /> that contains the event data.</param>
    protected virtual void OnBeforeInsert(BeforeModificationEventArgs e)
    {
        var handler = Events[beforeInsertEventKey] as EventHandler<BeforeModificationEventArgs>;
        if (handler != null)
            handler(this, e);
    }

    /// <summary>
    /// Raises the <see cref="BorderStyleChanged" /> event.
    /// </summary>
    /// <param name="e">An EventArgs that contains the event data.</param>
    protected virtual void OnBorderStyleChanged(System.EventArgs e)
    {
        var handler = Events[borderStyleChangedEventKey] as EventHandler;
        if (handler != null)
            handler(this, e);
    }

    /// <summary>
    /// Raises the <see cref="ChangeAnnotation" /> event.
    /// </summary>
    /// <param name="e">A <see cref="ChangeAnnotationEventArgs" /> that contains the event data.</param>
    protected virtual void OnChangeAnnotation(ChangeAnnotationEventArgs e)
    {
        var handler = Events[changeAnnotationEventKey] as EventHandler<ChangeAnnotationEventArgs>;
        if (handler != null)
            handler(this, e);
    }

    /// <summary>
    /// Raises the <see cref="CharAdded" /> event.
    /// </summary>
    /// <param name="e">A <see cref="CharAddedEventArgs" /> that contains the event data.</param>
    protected virtual void OnCharAdded(CharAddedEventArgs e)
    {
        var handler = Events[charAddedEventKey] as EventHandler<CharAddedEventArgs>;
        if (handler != null)
            handler(this, e);
    }

    /// <summary>
    /// Raises the <see cref="Delete" /> event.
    /// </summary>
    /// <param name="e">A <see cref="ModificationEventArgs" /> that contains the event data.</param>
    protected virtual void OnDelete(ModificationEventArgs e)
    {
        var handler = Events[deleteEventKey] as EventHandler<ModificationEventArgs>;
        if (handler != null)
            handler(this, e);
    }

    /// <summary>
    /// Raises the <see cref="DoubleClick" /> event.
    /// </summary>
    /// <param name="e">A <see cref="DoubleClickEventArgs" /> that contains the event data.</param>
    protected virtual void OnDoubleClick(DoubleClickEventArgs e)
    {
        var handler = Events[doubleClickEventKey] as EventHandler<DoubleClickEventArgs>;
        if (handler != null)
            handler(this, e);
    }

    /// <summary>
    /// Raises the <see cref="DwellEnd" /> event.
    /// </summary>
    /// <param name="e">A <see cref="DwellEventArgs" /> that contains the event data.</param>
    protected virtual void OnDwellEnd(DwellEventArgs e)
    {
        var handler = Events[dwellEndEventKey] as EventHandler<DwellEventArgs>;
        if (handler != null)
            handler(this, e);
    }

    /// <summary>
    /// Raises the <see cref="DwellStart" /> event.
    /// </summary>
    /// <param name="e">A <see cref="DwellEventArgs" /> that contains the event data.</param>
    protected virtual void OnDwellStart(DwellEventArgs e)
    {
        var handler = Events[dwellStartEventKey] as EventHandler<DwellEventArgs>;
        if (handler != null)
            handler(this, e);
    }

    /// <summary>
    /// Raises the <see cref="CallTipClick" /> event.
    /// </summary>
    /// <param name="e">A <see cref="CallTipClickEventArgs" /> that contains the event data.</param>
    protected virtual void OnCallTipClick(CallTipClickEventArgs e)
    {
        var handler = Events[callTipClickEventKey] as EventHandler<CallTipClickEventArgs>;
        if (handler != null)
            handler(this, e);
    }

    /// <summary>
    /// Raises the HandleCreated event.
    /// </summary>
    /// <param name="e">An EventArgs that contains the event data.</param>
    protected override unsafe void OnHandleCreated(EventArgs e)
    {
        // Set more intelligent defaults...
        InitDocument();

        // I would like to see all of my text please
        DirectMessage(SCI_SETSCROLLWIDTH, new IntPtr(1));
        DirectMessage(SCI_SETSCROLLWIDTHTRACKING, new IntPtr(1));

        //hide all default margins
        foreach (var margin in Margins)
        {
            margin.Width = 0;
        }

        // Enable support for the call tip style and tabs
        DirectMessage(SCI_CALLTIPUSESTYLE, new IntPtr(16));

        // Reset the valid "word chars" to work around a bug? in Scintilla which includes those below plus non-printable (beyond ASCII 127) characters
        var bytes = Helpers.GetBytes("abcdefghijklmnopqrstuvwxyz_ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789", Encoding.ASCII, zeroTerminated: true);
        fixed (byte* bp = bytes)
            DirectMessage(SCI_SETWORDCHARS, IntPtr.Zero, new IntPtr(bp));

        // Native Scintilla uses the WM_CREATE message to register itself as an
        // IDropTarget... beating Windows Forms to the punch. There are many possible
        // ways to solve this, but my favorite is to revoke drag and drop from the
        // native Scintilla control before base.OnHandleCreated does the standard
        // processing of AllowDrop.
        NativeMethods.RevokeDragDrop(Handle);

        base.OnHandleCreated(e);
    }

    /// <summary>
    /// Raises the <see cref="HotspotClick" /> event.
    /// </summary>
    /// <param name="e">A <see cref="HotspotClickEventArgs" /> that contains the event data.</param>
    protected virtual void OnHotspotClick(HotspotClickEventArgs e)
    {
        var handler = Events[hotspotClickEventKey] as EventHandler<HotspotClickEventArgs>;
        if (handler != null)
            handler(this, e);
    }

    /// <summary>
    /// Raises the <see cref="HotspotDoubleClick" /> event.
    /// </summary>
    /// <param name="e">A <see cref="HotspotClickEventArgs" /> that contains the event data.</param>
    protected virtual void OnHotspotDoubleClick(HotspotClickEventArgs e)
    {
        var handler = Events[hotspotDoubleClickEventKey] as EventHandler<HotspotClickEventArgs>;
        if (handler != null)
            handler(this, e);
    }

    /// <summary>
    /// Raises the <see cref="HotspotReleaseClick" /> event.
    /// </summary>
    /// <param name="e">A <see cref="HotspotClickEventArgs" /> that contains the event data.</param>
    protected virtual void OnHotspotReleaseClick(HotspotClickEventArgs e)
    {
        var handler = Events[hotspotReleaseClickEventKey] as EventHandler<HotspotClickEventArgs>;
        if (handler != null)
            handler(this, e);
    }

    /// <summary>
    /// Raises the <see cref="IndicatorClick" /> event.
    /// </summary>
    /// <param name="e">An <see cref="IndicatorClickEventArgs" /> that contains the event data.</param>
    protected virtual void OnIndicatorClick(IndicatorClickEventArgs e)
    {
        var handler = Events[indicatorClickEventKey] as EventHandler<IndicatorClickEventArgs>;
        if (handler != null)
            handler(this, e);
    }

    /// <summary>
    /// Raises the <see cref="IndicatorRelease" /> event.
    /// </summary>
    /// <param name="e">An <see cref="IndicatorReleaseEventArgs" /> that contains the event data.</param>
    protected virtual void OnIndicatorRelease(IndicatorReleaseEventArgs e)
    {
        var handler = Events[indicatorReleaseEventKey] as EventHandler<IndicatorReleaseEventArgs>;
        if (handler != null)
            handler(this, e);
    }

    /// <summary>
    /// Raises the <see cref="Insert" /> event.
    /// </summary>
    /// <param name="e">A <see cref="ModificationEventArgs" /> that contains the event data.</param>
    protected virtual void OnInsert(ModificationEventArgs e)
    {
        var handler = Events[insertEventKey] as EventHandler<ModificationEventArgs>;
        if (handler != null)
            handler(this, e);
    }

    /// <summary>
    /// Raises the <see cref="InsertCheck" /> event.
    /// </summary>
    /// <param name="e">An <see cref="InsertCheckEventArgs" /> that contains the event data.</param>
    protected virtual void OnInsertCheck(InsertCheckEventArgs e)
    {
        var handler = Events[insertCheckEventKey] as EventHandler<InsertCheckEventArgs>;
        if (handler != null)
            handler(this, e);
    }

    /// <summary>
    /// Raises the <see cref="MarginClick" /> event.
    /// </summary>
    /// <param name="e">A <see cref="MarginClickEventArgs" /> that contains the event data.</param>
    protected virtual void OnMarginClick(MarginClickEventArgs e)
    {
        var handler = Events[marginClickEventKey] as EventHandler<MarginClickEventArgs>;
        if (handler != null)
            handler(this, e);
    }

    /// <summary>
    /// Raises the <see cref="MarginRightClick" /> event.
    /// </summary>
    /// <param name="e">A <see cref="MarginClickEventArgs" /> that contains the event data.</param>
    protected virtual void OnMarginRightClick(MarginClickEventArgs e)
    {
        var handler = Events[marginRightClickEventKey] as EventHandler<MarginClickEventArgs>;
        if (handler != null)
            handler(this, e);
    }

    /// <summary>
    /// Raises the <see cref="ModifyAttempt" /> event.
    /// </summary>
    /// <param name="e">An EventArgs that contains the event data.</param>
    protected virtual void OnModifyAttempt(EventArgs e)
    {
        var handler = Events[modifyAttemptEventKey] as EventHandler<EventArgs>;
        if (handler != null)
            handler(this, e);
    }

    /// <summary>
    /// Raises the MouseUp event.
    /// </summary>
    /// <param name="e">A MouseEventArgs that contains the event data.</param>
    protected override void OnMouseUp(MouseEventArgs e)
    {
        // Borrowed this from TextBoxBase.OnMouseUp
        if (!doubleClick)
        {
            OnClick(e);
            OnMouseClick(e);
        }
        else
        {
            var doubleE = new MouseEventArgs(e.Button, 2, e.X, e.Y, e.Delta);
            OnDoubleClick(doubleE);
            OnMouseDoubleClick(doubleE);
            doubleClick = false;
        }

        base.OnMouseUp(e);
    }

    /// <summary>
    /// Raises the <see cref="NeedShown" /> event.
    /// </summary>
    /// <param name="e">A <see cref="NeedShownEventArgs" /> that contains the event data.</param>
    protected virtual void OnNeedShown(NeedShownEventArgs e)
    {
        var handler = Events[needShownEventKey] as EventHandler<NeedShownEventArgs>;
        if (handler != null)
            handler(this, e);
    }

    /// <summary>
    /// Raises the <see cref="Painted" /> event.
    /// </summary>
    /// <param name="e">An EventArgs that contains the event data.</param>
    protected virtual void OnPainted(EventArgs e)
    {
        var handler = Events[paintedEventKey] as EventHandler<EventArgs>;
        if (handler != null)
            handler(this, e);
    }

    /// <summary>
    /// Raises the <see cref="SavePointLeft" /> event.
    /// </summary>
    /// <param name="e">An EventArgs that contains the event data.</param>
    protected virtual void OnSavePointLeft(EventArgs e)
    {
        var handler = Events[savePointLeftEventKey] as EventHandler<EventArgs>;
        if (handler != null)
            handler(this, e);
    }

    /// <summary>
    /// Raises the <see cref="SavePointReached" /> event.
    /// </summary>
    /// <param name="e">An EventArgs that contains the event data.</param>
    protected virtual void OnSavePointReached(EventArgs e)
    {
        var handler = Events[savePointReachedEventKey] as EventHandler<EventArgs>;
        if (handler != null)
            handler(this, e);
    }

    /// <summary>
    /// Raises the <see cref="StyleNeeded" /> event.
    /// </summary>
    /// <param name="e">A <see cref="StyleNeededEventArgs" /> that contains the event data.</param>
    protected virtual void OnStyleNeeded(StyleNeededEventArgs e)
    {
        var handler = Events[styleNeededEventKey] as EventHandler<StyleNeededEventArgs>;
        if (handler != null)
            handler(this, e);
    }

    /// <summary>
    /// Raises the <see cref="UpdateUi" /> event.
    /// </summary>
    /// <param name="e">An <see cref="UpdateUIEventArgs" /> that contains the event data.</param>
    protected virtual void OnUpdateUI(UpdateUIEventArgs e)
    {
        EventHandler<UpdateUIEventArgs> handler = Events[updateUIEventKey] as EventHandler<UpdateUIEventArgs>;
        if (handler != null)
            handler(this, e);
    }

    /// <summary>
    /// Raises the <see cref="ZoomChanged" /> event.
    /// </summary>
    /// <param name="e">An EventArgs that contains the event data.</param>
    protected virtual void OnZoomChanged(EventArgs e)
    {
        var handler = Events[zoomChangedEventKey] as EventHandler<EventArgs>;
        if (handler != null)
            handler(this, e);
    }

    /// <summary>
    /// Pastes the contents of the clipboard into the current selection.
    /// </summary>
    public void Paste()
    {
        DirectMessage(SCI_PASTE);
    }

    /// <summary>
    /// Returns the X display pixel location of the specified document position.
    /// </summary>
    /// <param name="pos">The zero-based document character position.</param>
    /// <returns>The x-coordinate of the specified <paramref name="pos" /> within the client rectangle of the control.</returns>
    public int PointXFromPosition(int pos)
    {
        pos = Helpers.Clamp(pos, 0, TextLength);
        pos = Lines.CharToBytePosition(pos);
        return DirectMessage(SCI_POINTXFROMPOSITION, IntPtr.Zero, new IntPtr(pos)).ToInt32();
    }

    /// <summary>
    /// Returns the Y display pixel location of the specified document position.
    /// </summary>
    /// <param name="pos">The zero-based document character position.</param>
    /// <returns>The y-coordinate of the specified <paramref name="pos" /> within the client rectangle of the control.</returns>
    public int PointYFromPosition(int pos)
    {
        pos = Helpers.Clamp(pos, 0, TextLength);
        pos = Lines.CharToBytePosition(pos);
        return DirectMessage(SCI_POINTYFROMPOSITION, IntPtr.Zero, new IntPtr(pos)).ToInt32();
    }

    /// <summary>
    /// Retrieves a list of property names that can be set for the current <see cref="Lexer" />.
    /// </summary>
    /// <returns>A String of property names separated by line breaks.</returns>
    public unsafe string PropertyNames()
    {
        var length = DirectMessage(SCI_PROPERTYNAMES).ToInt32();
        if (length == 0)
            return string.Empty;

        var bytes = new byte[length + 1];
        fixed (byte* bp = bytes)
        {
            DirectMessage(SCI_PROPERTYNAMES, IntPtr.Zero, new IntPtr(bp));
            return Helpers.GetString(new IntPtr(bp), length, Encoding.ASCII);
        }
    }

    /// <summary>
    /// Retrieves the data type of the specified property name for the current <see cref="Lexer" />.
    /// </summary>
    /// <param name="name">A property name supported by the current <see cref="Lexer" />.</param>
    /// <returns>One of the <see cref="PropertyType" /> enumeration values. The default is <see cref="bool" />.</returns>
    /// <remarks>A list of supported property names for the current <see cref="Lexer" /> can be obtained by calling <see cref="PropertyNames" />.</remarks>
    public unsafe PropertyType PropertyType(string name)
    {
        if (String.IsNullOrEmpty(name))
            return global::Scintilla.NET.Abstractions.Enumerations.PropertyType.Boolean;

        var bytes = Helpers.GetBytes(name, Encoding.ASCII, zeroTerminated: true);
        fixed (byte* bp = bytes)
            return (PropertyType)DirectMessage(SCI_PROPERTYTYPE, new IntPtr(bp));
    }

    /// <summary>
    /// Redoes the effect of an <see cref="Undo" /> operation.
    /// </summary>
    public void Redo()
    {
        DirectMessage(SCI_REDO);
    }

    /// <summary>
    /// Maps the specified image to a type identifer for use in an autocompletion list.
    /// </summary>
    /// <param name="type">The numeric identifier for this image.</param>
    /// <param name="image">The Bitmap to use in an autocompletion list.</param>
    /// <remarks>
    /// The <paramref name="image" /> registered can be referenced by its <paramref name="type" /> identifer in an autocompletion
    /// list by suffixing a word with the <see cref="AutoCTypeSeparator" /> character and the <paramref name="type" /> value. e.g.
    /// "int?2 long?3 short?1" etc....
    /// </remarks>
    /// <seealso cref="AutoCTypeSeparator" />
    public unsafe void RegisterRgbaImage(int type, Bitmap image)
    {
        // TODO Clamp type?
        if (image == null)
            return;

        DirectMessage(SCI_RGBAIMAGESETWIDTH, new IntPtr(image.Width));
        DirectMessage(SCI_RGBAIMAGESETHEIGHT, new IntPtr(image.Height));

        var bytes = Helpers.BitmapToArgb(image);
        fixed (byte* bp = bytes)
            DirectMessage(SCI_REGISTERRGBAIMAGE, new IntPtr(type), new IntPtr(bp));
    }

    /// <summary>
    /// Decreases the reference count of the specified document by 1.
    /// </summary>
    /// <param name="document">
    /// The document reference count to decrease.
    /// When a document's reference count reaches 0 it is destroyed and any associated memory released.
    /// </param>
    public void ReleaseDocument(Document document)
    {
        var ptr = document.Value;
        DirectMessage(SCI_RELEASEDOCUMENT, IntPtr.Zero, ptr);
    }

    /// <summary>
    /// Replaces the current selection with the specified text.
    /// </summary>
    /// <param name="text">The text that should replace the current selection.</param>
    /// <remarks>
    /// If there is not a current selection, the text will be inserted at the current caret position.
    /// Following the operation the caret is placed at the end of the inserted text and scrolled into view.
    /// </remarks>
    public unsafe void ReplaceSelection(string text)
    {
        fixed (byte* bp = Helpers.GetBytes(text ?? string.Empty, Encoding, zeroTerminated: true))
            DirectMessage(SCI_REPLACESEL, IntPtr.Zero, new IntPtr(bp));
    }

    /// <summary>
    /// Replaces the target defined by <see cref="TargetStart" /> and <see cref="TargetEnd" /> with the specified <paramref name="text" />.
    /// </summary>
    /// <param name="text">The text that will replace the current target.</param>
    /// <returns>The length of the replaced text.</returns>
    /// <remarks>
    /// The <see cref="TargetStart" /> and <see cref="TargetEnd" /> properties will be updated to the start and end positions of the replaced text.
    /// The recommended way to delete text in the document is to set the target range to be removed and replace the target with an empty string.
    /// </remarks>
    public unsafe int ReplaceTarget(string text)
    {
        if (text == null)
            text = string.Empty;

        var bytes = Helpers.GetBytes(text, Encoding, false);
        fixed (byte* bp = bytes)
            DirectMessage(SCI_REPLACETARGET, new IntPtr(bytes.Length), new IntPtr(bp));

        return text.Length;
    }

    /// <summary>
    /// Replaces the target text defined by <see cref="TargetStart" /> and <see cref="TargetEnd" /> with the specified value after first substituting
    /// "\1" through "\9" macros in the <paramref name="text" /> with the most recent regular expression capture groups.
    /// </summary>
    /// <param name="text">The text containing "\n" macros that will be substituted with the most recent regular expression capture groups and then replace the current target.</param>
    /// <returns>The length of the replaced text.</returns>
    /// <remarks>
    /// The "\0" macro will be substituted by the entire matched text from the most recent search.
    /// The <see cref="TargetStart" /> and <see cref="TargetEnd" /> properties will be updated to the start and end positions of the replaced text.
    /// </remarks>
    /// <seealso cref="GetTag" />
    public unsafe int ReplaceTargetRe(string text)
    {
        var bytes = Helpers.GetBytes(text ?? string.Empty, Encoding, false);
        fixed (byte* bp = bytes)
            DirectMessage(SCI_REPLACETARGETRE, new IntPtr(bytes.Length), new IntPtr(bp));

        return Math.Abs(TargetEnd - TargetStart);
    }

    private void ResetAdditionalCaretForeColor()
    {
        AdditionalCaretForeColor = Color.FromArgb(127, 127, 127);
    }

    /// <summary>
    /// Makes the next selection the main selection.
    /// </summary>
    public void RotateSelection()
    {
        DirectMessage(SCI_ROTATESELECTION);
    }

    private void ScnDoubleClick(ref SCNotification scn)
    {
        var keys = Keys.Modifiers & (Keys)(scn.modifiers << 16);
        var eventArgs = new DoubleClickEventArgs(this, keys, scn.position.ToInt32(), scn.line.ToInt32());
        OnDoubleClick(eventArgs);
    }

    private void ScnHotspotClick(ref SCNotification scn)
    {
        var keys = Keys.Modifiers & (Keys)(scn.modifiers << 16);
        var eventArgs = new HotspotClickEventArgs(this, keys, scn.position.ToInt32());
        switch (scn.nmhdr.code)
        {
            case SCN_HOTSPOTCLICK:
                OnHotspotClick(eventArgs);
                break;

            case SCN_HOTSPOTDOUBLECLICK:
                OnHotspotDoubleClick(eventArgs);
                break;

            case SCN_HOTSPOTRELEASECLICK:
                OnHotspotReleaseClick(eventArgs);
                break;
        }
    }

    private void ScnIndicatorClick(ref SCNotification scn)
    {
        switch (scn.nmhdr.code)
        {
            case SCN_INDICATORCLICK:
                var keys = Keys.Modifiers & (Keys)(scn.modifiers << 16);
                OnIndicatorClick(new IndicatorClickEventArgs(this, keys, scn.position.ToInt32()));
                break;

            case SCN_INDICATORRELEASE:
                OnIndicatorRelease(new IndicatorReleaseEventArgs(this, scn.position.ToInt32()));
                break;
        }
    }

    private void ScnMarginClick(ref SCNotification scn)
    {
        var keys = Keys.Modifiers & (Keys)(scn.modifiers << 16);
        var eventArgs = new MarginClickEventArgs(this, keys, scn.position.ToInt32(), scn.margin);

        if (scn.nmhdr.code == SCN_MARGINCLICK)
            OnMarginClick(eventArgs);
        else
            OnMarginRightClick(eventArgs);
    }

    private void ScnModified(ref SCNotification scn)
    {
        // The InsertCheck, BeforeInsert, BeforeDelete, Insert, and Delete events can all potentially require
        // the same conversions: byte to char position, char* to string, etc.... To avoid doing the same work
        // multiple times we share that data between events.

        if ((scn.modificationType & SC_MOD_INSERTCHECK) > 0)
        {
            var eventArgs = new InsertCheckEventArgs(this, scn.position.ToInt32(), scn.length.ToInt32(), scn.text);
            OnInsertCheck(eventArgs);

            cachedPosition = eventArgs.CachedPosition;
            cachedText = eventArgs.CachedText;
        }

        const int sourceMask = (SC_PERFORMED_USER | SC_PERFORMED_UNDO | SC_PERFORMED_REDO);

        if ((scn.modificationType & (SC_MOD_BEFOREDELETE | SC_MOD_BEFOREINSERT)) > 0)
        {
            var source = (ModificationSource)(scn.modificationType & sourceMask);
            var eventArgs = new BeforeModificationEventArgs(this, source, scn.position.ToInt32(), scn.length.ToInt32(), scn.text);

            eventArgs.CachedPosition = cachedPosition;
            eventArgs.CachedText = cachedText;

            if ((scn.modificationType & SC_MOD_BEFOREINSERT) > 0)
            {
                OnBeforeInsert(eventArgs);
            }
            else
            {
                OnBeforeDelete(eventArgs);
            }

            cachedPosition = eventArgs.CachedPosition;
            cachedText = eventArgs.CachedText;
        }

        if ((scn.modificationType & (SC_MOD_DELETETEXT | SC_MOD_INSERTTEXT)) > 0)
        {
            var source = (ModificationSource)(scn.modificationType & sourceMask);
            var eventArgs = new ModificationEventArgs(this, source, scn.position.ToInt32(), scn.length.ToInt32(), scn.text, scn.linesAdded.ToInt32());

            eventArgs.CachedPosition = cachedPosition;
            eventArgs.CachedText = cachedText;

            if ((scn.modificationType & SC_MOD_INSERTTEXT) > 0)
            {
                OnInsert(eventArgs);
            }
            else
            {
                OnDelete(eventArgs);
            }

            // Always clear the cache
            cachedPosition = null;
            cachedText = null;

            // For backward compatibility.... Of course this means that we'll raise two
            // TextChanged events for replace (insert/delete) operations, but that's life.
            OnTextChanged(EventArgs.Empty);
        }

        if ((scn.modificationType & SC_MOD_CHANGEANNOTATION) > 0)
        {
            var eventArgs = new ChangeAnnotationEventArgs(scn.line.ToInt32());
            OnChangeAnnotation(eventArgs);
        }
    }

    /// <summary>
    /// Scrolls the current position into view, if it is not already visible.
    /// </summary>
    public void ScrollCaret()
    {
        DirectMessage(SCI_SCROLLCARET);
    }

    /// <summary>
    /// Scrolls the specified range into view.
    /// </summary>
    /// <param name="start">The zero-based document start position to scroll to.</param>
    /// <param name="end">
    /// The zero-based document end position to scroll to if doing so does not cause the <paramref name="start" />
    /// position to scroll out of view.
    /// </param>
    /// <remarks>This may be used to make a search match visible.</remarks>
    public void ScrollRange(int start, int end)
    {
        var textLength = TextLength;
        start = Helpers.Clamp(start, 0, textLength);
        end = Helpers.Clamp(end, 0, textLength);

        // Convert to byte positions
        start = Lines.CharToBytePosition(start);
        end = Lines.CharToBytePosition(end);

        // The arguments would  seem reverse from Scintilla documentation
        // but empirical  evidence suggests this is correct....
        DirectMessage(SCI_SCROLLRANGE, new IntPtr(start), new IntPtr(end));
    }

    /// <summary>
    /// Searches for the first occurrence of the specified text in the target defined by <see cref="TargetStart" /> and <see cref="TargetEnd" />.
    /// </summary>
    /// <param name="text">The text to search for. The interpretation of the text (i.e. whether it is a regular expression) is defined by the <see cref="SearchFlags" /> property.</param>
    /// <returns>The zero-based start position of the matched text within the document if successful; otherwise, -1.</returns>
    /// <remarks>
    /// If successful, the <see cref="TargetStart" /> and <see cref="TargetEnd" /> properties will be updated to the start and end positions of the matched text.
    /// Searching can be performed in reverse using a <see cref="TargetStart" /> greater than the <see cref="TargetEnd" />.
    /// </remarks>
    public unsafe int SearchInTarget(string text)
    {
        int bytePos = 0;
        var bytes = Helpers.GetBytes(text ?? string.Empty, Encoding, zeroTerminated: false);
        fixed (byte* bp = bytes)
            bytePos = DirectMessage(SCI_SEARCHINTARGET, new IntPtr(bytes.Length), new IntPtr(bp)).ToInt32();

        if (bytePos == -1)
            return bytePos;

        return Lines.ByteToCharPosition(bytePos);
    }

    /// <summary>
    /// Selects all the text in the document.
    /// </summary>
    /// <remarks>The current position is not scrolled into view.</remarks>
    public void SelectAll()
    {
        DirectMessage(SCI_SELECTALL);
    }

    /// <summary>
    /// Sets the background color of additional selections.
    /// </summary>
    /// <param name="color">Additional selections background color.</param>
    /// <remarks>Calling <see cref="SetSelectionBackColor" /> will reset the <paramref name="color" /> specified.</remarks>
    public void SetAdditionalSelBack(Color color)
    {
        var colour = ColorTranslator.ToWin32(color);
        DirectMessage(SCI_SETADDITIONALSELBACK, new IntPtr(colour));
    }

    /// <summary>
    /// Sets the foreground color of additional selections.
    /// </summary>
    /// <param name="color">Additional selections foreground color.</param>
    /// <remarks>Calling <see cref="SetSelectionForeColor" /> will reset the <paramref name="color" /> specified.</remarks>
    public void SetAdditionalSelFore(Color color)
    {
        var colour = ColorTranslator.ToWin32(color);
        DirectMessage(SCI_SETADDITIONALSELFORE, new IntPtr(colour));
    }

    /// <summary>
    /// Removes any selection and places the caret at the specified position.
    /// </summary>
    /// <param name="pos">The zero-based document position to place the caret at.</param>
    /// <remarks>The caret is not scrolled into view.</remarks>
    public void SetEmptySelection(int pos)
    {
        pos = Helpers.Clamp(pos, 0, TextLength);
        pos = Lines.CharToBytePosition(pos);
        DirectMessage(SCI_SETEMPTYSELECTION, new IntPtr(pos));
    }

    /// <summary>
    /// Sets additional options for displaying folds.
    /// </summary>
    /// <param name="flags">A bitwise combination of the <see cref="FoldFlags" /> enumeration.</param>
    public void SetFoldFlags(FoldFlags flags)
    {
        DirectMessage(SCI_SETFOLDFLAGS, new IntPtr((int)flags));
    }

    /// <summary>
    /// Sets a global override to the fold margin color.
    /// </summary>
    /// <param name="use">true to override the fold margin color; otherwise, false.</param>
    /// <param name="color">The global fold margin color.</param>
    /// <seealso cref="SetFoldMarginHighlightColor" />
    public void SetFoldMarginColor(bool use, Color color)
    {
        var colour = ColorTranslator.ToWin32(color);
        var useFoldMarginColour = (use ? new IntPtr(1) : IntPtr.Zero);

        DirectMessage(SCI_SETFOLDMARGINCOLOUR, useFoldMarginColour, new IntPtr(colour));
    }

    /// <summary>
    /// Sets a global override to the fold margin highlight color.
    /// </summary>
    /// <param name="use">true to override the fold margin highlight color; otherwise, false.</param>
    /// <param name="color">The global fold margin highlight color.</param>
    /// <seealso cref="SetFoldMarginColor" />
    public void SetFoldMarginHighlightColor(bool use, Color color)
    {
        var colour = ColorTranslator.ToWin32(color);
        var useFoldMarginHighlightColour = (use ? new IntPtr(1) : IntPtr.Zero);

        DirectMessage(SCI_SETFOLDMARGINHICOLOUR, useFoldMarginHighlightColour, new IntPtr(colour));
    }

    /// <summary>
    /// Similar to <see cref="SetKeywords" /> but for sub-styles.
    /// </summary>
    /// <param name="style">The sub-style integer index</param>
    /// <param name="identifiers">A list of words separated by whitespace (space, tab, '\n', '\r') characters.</param>
    public unsafe void SetIdentifiers(int style, string identifiers)
    {
        var baseStyle = GetStyleFromSubStyle(style);
        var min = GetSubStylesStart(baseStyle);
        var length = GetSubStylesLength(baseStyle);
        var max = (length > 0) ? min + length - 1 : min;

        style = Helpers.Clamp(style, min, max);
        var bytes = Helpers.GetBytes(identifiers ?? string.Empty, Encoding.ASCII, zeroTerminated: true);

        fixed (byte* bp = bytes)
            DirectMessage(SCI_SETIDENTIFIERS, new IntPtr(style), new IntPtr(bp));
    }

    /// <summary>
    /// Updates a keyword set used by the current <see cref="Lexer" />.
    /// </summary>
    /// <param name="set">The zero-based index of the keyword set to update.</param>
    /// <param name="keywords">
    /// A list of keywords pertaining to the current <see cref="Lexer" /> separated by whitespace (space, tab, '\n', '\r') characters.
    /// </param>
    /// <remarks>The keywords specified will be styled according to the current <see cref="Lexer" />.</remarks>
    /// <seealso cref="DescribeKeywordSets" />
    public unsafe void SetKeywords(int set, string keywords)
    {
        set = Helpers.Clamp(set, 0, KEYWORDSET_MAX);
        var bytes = Helpers.GetBytes(keywords ?? string.Empty, Encoding.ASCII, zeroTerminated: true);

        fixed (byte* bp = bytes)
            DirectMessage(SCI_SETKEYWORDS, new IntPtr(set), new IntPtr(bp));
    }

    /// <summary>
    /// Sets the application-wide behavior for destroying <see cref="Scintilla" /> controls.
    /// </summary>
    /// <param name="reparent">
    /// true to reparent Scintilla controls to message-only windows when destroyed rather than actually destroying the control handle; otherwise, false.
    /// The default is true.
    /// </param>
    /// <remarks>This method must be called prior to the first <see cref="Scintilla" /> control being created.</remarks>
    public static void SetDestroyHandleBehavior(bool reparent)
    {
        // WM_DESTROY workaround
        if (Scintilla.reparentAll == null)
        {
            Scintilla.reparentAll = reparent;
        }
    }

    /// <summary>
    /// Passes the specified property name-value pair to the current <see cref="Lexer" />.
    /// </summary>
    /// <param name="name">The property name to set.</param>
    /// <param name="value">
    /// The property value. Values can refer to other property names using the syntax $(name), where 'name' is another property
    /// name for the current <see cref="Lexer" />. When the property value is retrieved by a call to <see cref="GetPropertyExpanded" />
    /// the embedded property name macro will be replaced (expanded) with that current property value.
    /// </param>
    /// <remarks>Property names are case-sensitive.</remarks>
    public unsafe void SetProperty(string name, string value)
    {
        if (String.IsNullOrEmpty(name))
            return;

        var nameBytes = Helpers.GetBytes(name, Encoding.ASCII, zeroTerminated: true);
        var valueBytes = Helpers.GetBytes(value ?? string.Empty, Encoding.ASCII, zeroTerminated: true);

        fixed (byte* nb = nameBytes)
        fixed (byte* vb = valueBytes)
        {
            DirectMessage(SCI_SETPROPERTY, new IntPtr(nb), new IntPtr(vb));
        }
    }

    /// <summary>
    /// Marks the document as unmodified.
    /// </summary>
    /// <seealso cref="Modified" />
    public void SetSavePoint()
    {
        DirectMessage(SCI_SETSAVEPOINT);
    }

    /// <summary>
    /// Sets the anchor and current position.
    /// </summary>
    /// <param name="anchorPos">The zero-based document position to start the selection.</param>
    /// <param name="currentPos">The zero-based document position to end the selection.</param>
    /// <remarks>
    /// A negative value for <paramref name="currentPos" /> signifies the end of the document.
    /// A negative value for <paramref name="anchorPos" /> signifies no selection (i.e. sets the <paramref name="anchorPos" />
    /// to the same position as the <paramref name="currentPos" />).
    /// The current position is scrolled into view following this operation.
    /// </remarks>
    public void SetSel(int anchorPos, int currentPos)
    {
        if (anchorPos == currentPos)
        {
            // Optimization so that we don't have to translate the anchor position
            // when we can instead just pass -1 and have Scintilla handle it.
            anchorPos = -1;
        }

        var textLength = TextLength;

        if (anchorPos >= 0)
        {
            anchorPos = Helpers.Clamp(anchorPos, 0, textLength);
            anchorPos = Lines.CharToBytePosition(anchorPos);
        }

        if (currentPos >= 0)
        {
            currentPos = Helpers.Clamp(currentPos, 0, textLength);
            currentPos = Lines.CharToBytePosition(currentPos);
        }

        DirectMessage(SCI_SETSEL, new IntPtr(anchorPos), new IntPtr(currentPos));
    }

    /// <summary>
    /// Sets a single selection from anchor to caret.
    /// </summary>
    /// <param name="caret">The zero-based document position to end the selection.</param>
    /// <param name="anchor">The zero-based document position to start the selection.</param>
    public void SetSelection(int caret, int anchor)
    {
        var textLength = TextLength;

        caret = Helpers.Clamp(caret, 0, textLength);
        anchor = Helpers.Clamp(anchor, 0, textLength);

        caret = Lines.CharToBytePosition(caret);
        anchor = Lines.CharToBytePosition(anchor);

        DirectMessage(SCI_SETSELECTION, new IntPtr(caret), new IntPtr(anchor));
    }

    /// <summary>
    /// Sets a global override to the selection background color.
    /// </summary>
    /// <param name="use">true to override the selection background color; otherwise, false.</param>
    /// <param name="color">The global selection background color.</param>
    /// <seealso cref="SetSelectionForeColor" />
    public void SetSelectionBackColor(bool use, Color color)
    {
        var colour = ColorTranslator.ToWin32(color);
        var useSelectionForeColour = (use ? new IntPtr(1) : IntPtr.Zero);

        DirectMessage(SCI_SETSELBACK, useSelectionForeColour, new IntPtr(colour));
    }

    /// <summary>
    /// Sets a global override to the selection foreground color.
    /// </summary>
    /// <param name="use">true to override the selection foreground color; otherwise, false.</param>
    /// <param name="color">The global selection foreground color.</param>
    /// <seealso cref="SetSelectionBackColor" />
    public void SetSelectionForeColor(bool use, Color color)
    {
        var colour = ColorTranslator.ToWin32(color);
        var useSelectionForeColour = (use ? new IntPtr(1) : IntPtr.Zero);

        DirectMessage(SCI_SETSELFORE, useSelectionForeColour, new IntPtr(colour));
    }

    /// <summary>
    /// Gets or sets the layer where the text selection will be painted. Default value is <see cref="Layer.Base"/>
    /// </summary>
    [DefaultValue(Layer.Base)]
    [Category("Selection")]
    [Description("The layer where the text selection will be painted.")]
    public Layer SelectionLayer
    {
        get
        {
            return (Layer)DirectMessage(SCI_GETSELECTIONLAYER).ToInt32();
        }
        set
        {
            int layer = (int)value;
            DirectMessage(SCI_SETSELECTIONLAYER, new IntPtr(layer), IntPtr.Zero);
        }
    }

    /// <summary>
    /// Styles the specified length of characters.
    /// </summary>
    /// <param name="length">The number of characters to style.</param>
    /// <param name="style">The <see cref="Style" /> definition index to assign each character.</param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="length" /> or <paramref name="style" /> is less than zero. -or-
    /// The sum of a preceeding call to <see cref="StartStyling" /> or <see name="SetStyling" /> and <paramref name="length" /> is greater than the document length. -or-
    /// <paramref name="style" /> is greater than or equal to the number of style definitions.
    /// </exception>
    /// <remarks>
    /// The styling position is advanced by <paramref name="length" /> after each call allowing multiple
    /// calls to <see cref="SetStyling" /> for a single call to <see cref="StartStyling" />.
    /// </remarks>
    /// <seealso cref="StartStyling" />
    public void SetStyling(int length, int style)
    {
        var textLength = TextLength;

        if (length < 0)
            throw new ArgumentOutOfRangeException("length", "Length cannot be less than zero.");
        if (stylingPosition + length > textLength)
            throw new ArgumentOutOfRangeException("length", "Position and length must refer to a range within the document.");
        if (style < 0 || style >= Styles.Count)
            throw new ArgumentOutOfRangeException("style", "Style must be non-negative and less than the size of the collection.");

        var endPos = stylingPosition + length;
        var endBytePos = Lines.CharToBytePosition(endPos);
        DirectMessage(SCI_SETSTYLING, new IntPtr(endBytePos - stylingBytePosition), new IntPtr(style));

        // Track this for the next call
        stylingPosition = endPos;
        stylingBytePosition = endBytePos;
    }

    /// <summary>
    /// Sets the <see cref="TargetStart" /> and <see cref="TargetEnd" /> properties in a single call.
    /// </summary>
    /// <param name="start">The zero-based character position within the document to start a search or replace operation.</param>
    /// <param name="end">The zero-based character position within the document to end a search or replace operation.</param>
    /// <seealso cref="TargetStart" />
    /// <seealso cref="TargetEnd" />
    public void SetTargetRange(int start, int end)
    {
        var textLength = TextLength;
        start = Helpers.Clamp(start, 0, textLength);
        end = Helpers.Clamp(end, 0, textLength);

        start = Lines.CharToBytePosition(start);
        end = Lines.CharToBytePosition(end);

        DirectMessage(SCI_SETTARGETRANGE, new IntPtr(start), new IntPtr(end));
    }

    /// <summary>
    /// Sets a global override to the whitespace background color.
    /// </summary>
    /// <param name="use">true to override the whitespace background color; otherwise, false.</param>
    /// <param name="color">The global whitespace background color.</param>
    /// <remarks>When not overridden globally, the whitespace background color is determined by the current lexer.</remarks>
    /// <seealso cref="ViewWhitespace" />
    /// <seealso cref="SetWhitespaceForeColor" />
    public void SetWhitespaceBackColor(bool use, Color color)
    {
        var colour = ColorTranslator.ToWin32(color);
        var useWhitespaceBackColour = (use ? new IntPtr(1) : IntPtr.Zero);

        DirectMessage(SCI_SETWHITESPACEBACK, useWhitespaceBackColour, new IntPtr(colour));
    }

    /// <summary>
    /// Sets a global override to the whitespace foreground color.
    /// </summary>
    /// <param name="use">true to override the whitespace foreground color; otherwise, false.</param>
    /// <param name="color">The global whitespace foreground color.</param>
    /// <remarks>When not overridden globally, the whitespace foreground color is determined by the current lexer.</remarks>
    /// <seealso cref="ViewWhitespace" />
    /// <seealso cref="SetWhitespaceBackColor" />
    public void SetWhitespaceForeColor(bool use, Color color)
    {
        var colour = ColorTranslator.ToWin32(color);
        var useWhitespaceForeColour = (use ? new IntPtr(1) : IntPtr.Zero);

        DirectMessage(SCI_SETWHITESPACEFORE, useWhitespaceForeColour, new IntPtr(colour));
    }

    private bool ShouldSerializeAdditionalCaretForeColor()
    {
        return AdditionalCaretForeColor != Color.FromArgb(127, 127, 127);
    }

    /// <summary>
    /// Shows the range of lines specified.
    /// </summary>
    /// <param name="lineStart">The zero-based index of the line range to start showing.</param>
    /// <param name="lineEnd">The zero-based index of the line range to end showing.</param>
    /// <seealso cref="HideLines" />
    /// <seealso cref="Line.Visible" />
    public void ShowLines(int lineStart, int lineEnd)
    {
        lineStart = Helpers.Clamp(lineStart, 0, Lines.Count);
        lineEnd = Helpers.Clamp(lineEnd, lineStart, Lines.Count);

        DirectMessage(SCI_SHOWLINES, new IntPtr(lineStart), new IntPtr(lineEnd));
    }

    /// <summary>
    /// Prepares for styling by setting the styling <paramref name="position" /> to start at.
    /// </summary>
    /// <param name="position">The zero-based character position in the document to start styling.</param>
    /// <remarks>
    /// After preparing the document for styling, use successive calls to <see cref="SetStyling" />
    /// to style the document.
    /// </remarks>
    /// <seealso cref="SetStyling" />
    public void StartStyling(int position)
    {
        position = Helpers.Clamp(position, 0, TextLength);
        var pos = Lines.CharToBytePosition(position);
        DirectMessage(SCI_STARTSTYLING, new IntPtr(pos));

        // Track this so we can validate calls to SetStyling
        stylingPosition = position;
        stylingBytePosition = pos;
    }

    /// <summary>
    /// Resets all style properties to those currently configured for the <see cref="Style.Default" /> style.
    /// </summary>
    /// <seealso cref="StyleResetDefault" />
    public void StyleClearAll()
    {
        DirectMessage(SCI_STYLECLEARALL);
    }

    /// <summary>
    /// Resets the <see cref="Style.Default" /> style to its initial state.
    /// </summary>
    /// <seealso cref="StyleClearAll" />
    public void StyleResetDefault()
    {
        DirectMessage(SCI_STYLERESETDEFAULT);
    }

    /// <summary>
    /// Moves the caret to the opposite end of the main selection.
    /// </summary>
    public void SwapMainAnchorCaret()
    {
        DirectMessage(SCI_SWAPMAINANCHORCARET);
    }

    /// <summary>
    /// Sets the <see cref="TargetStart" /> and <see cref="TargetEnd" /> to the start and end positions of the selection.
    /// </summary>
    /// <seealso cref="TargetWholeDocument" />
    public void TargetFromSelection()
    {
        DirectMessage(SCI_TARGETFROMSELECTION);
    }

    /// <summary>
    /// Sets the <see cref="TargetStart" /> and <see cref="TargetEnd" /> to the start and end positions of the document.
    /// </summary>
    /// <seealso cref="TargetFromSelection" />
    public void TargetWholeDocument()
    {
        DirectMessage(SCI_TARGETWHOLEDOCUMENT);
    }

    /// <summary>
    /// Measures the width in pixels of the specified string when rendered in the specified style.
    /// </summary>
    /// <param name="style">The index of the <see cref="Style" /> to use when rendering the text to measure.</param>
    /// <param name="text">The text to measure.</param>
    /// <returns>The width in pixels.</returns>
    public unsafe int TextWidth(int style, string text)
    {
        style = Helpers.Clamp(style, 0, Styles.Count - 1);
        var bytes = Helpers.GetBytes(text ?? string.Empty, Encoding, zeroTerminated: true);

        fixed (byte* bp = bytes)
        {
            return DirectMessage(SCI_TEXTWIDTH, new IntPtr(style), new IntPtr(bp)).ToInt32();
        }
    }

    /// <summary>
    /// Undoes the previous action.
    /// </summary>
    public void Undo()
    {
        DirectMessage(SCI_UNDO);
    }

    /// <summary>
    /// Determines whether to show the right-click context menu.
    /// </summary>
    /// <param name="enablePopup">true to enable the popup window; otherwise, false.</param>
    /// <seealso cref="UsePopup(PopupMode)" />
    public void UsePopup(bool enablePopup)
    {
        // NOTE: The behavior of UsePopup has changed in v3.7.1, however, this approach is still valid
        var bEnablePopup = (enablePopup ? new IntPtr(1) : IntPtr.Zero);
        DirectMessage(SCI_USEPOPUP, bEnablePopup);
    }

    /// <summary>
    /// Determines the conditions for displaying the standard right-click context menu.
    /// </summary>
    /// <param name="popupMode">One of the <seealso cref="PopupMode" /> enumeration values.</param>
    public void UsePopup(PopupMode popupMode)
    {
        DirectMessage(SCI_USEPOPUP, new IntPtr((int)popupMode));
    }

    private void WmDestroy(ref Message m)
    {
        // WM_DESTROY workaround
        if (reparent && IsHandleCreated)
        {
            // In some circumstances it's possible for the control's window handle to be destroyed
            // and recreated during the life of the control. I have no idea why Windows Forms was coded
            // this way but that creates an issue for us because most/all of our control state is stored
            // in the native Scintilla control (i.e. Handle) and to destroy it will bork us. So, rather
            // than destroying the handle as requested, we "reparent" ourselves to a message-only
            // (invisible) window to keep our handle alive. It doesn't appear that this causes any
            // issues to Windows Forms because it is completely unaware of it. When a control goes through
            // its regular (re)create handle process one of the steps is to assign the parent and so our
            // temporary bait-and-switch gets reconciled again automatically. Our Dispose method ensures
            // that we truly get destroyed when the time is right.

            NativeMethods.SetParent(Handle, new IntPtr(HWND_MESSAGE));
            m.Result = IntPtr.Zero;
            return;
        }

        base.WndProc(ref m);
    }

    private void WmReflectNotify(ref Message m)
    {
        // A standard Windows notification and a Scintilla notification header are compatible
        SCNotification scn = (SCNotification)Marshal.PtrToStructure(m.LParam, typeof(SCNotification));
        if (scn.nmhdr.code >= SCN_STYLENEEDED && scn.nmhdr.code <= SCN_AUTOCCOMPLETED)
        {
            var handler = Events[scNotificationEventKey] as EventHandler<SCNotificationEventArgs>;
            if (handler != null)
                handler(this, new SCNotificationEventArgs(scn));

            switch (scn.nmhdr.code)
            {
                case SCN_PAINTED:
                    OnPainted(EventArgs.Empty);
                    break;

                case SCN_MODIFIED:
                    ScnModified(ref scn);
                    break;

                case SCN_MODIFYATTEMPTRO:
                    OnModifyAttempt(EventArgs.Empty);
                    break;

                case SCN_STYLENEEDED:
                    OnStyleNeeded(new StyleNeededEventArgs(this, scn.position.ToInt32()));
                    break;

                case SCN_SAVEPOINTLEFT:
                    OnSavePointLeft(EventArgs.Empty);
                    break;

                case SCN_SAVEPOINTREACHED:
                    OnSavePointReached(EventArgs.Empty);
                    break;

                case SCN_MARGINCLICK:
                case SCN_MARGINRIGHTCLICK:
                    ScnMarginClick(ref scn);
                    break;

                case SCN_UPDATEUI:
                    OnUpdateUI(new UpdateUIEventArgs((UpdateChange)scn.updated));
                    break;

                case SCN_CHARADDED:
                    OnCharAdded(new CharAddedEventArgs(scn.ch));
                    break;

                case SCN_AUTOCSELECTION:
                    OnAutoCSelection(new AutoCSelectionEventArgs(this, scn.position.ToInt32(), scn.text, scn.ch, (ListCompletionMethod)scn.listCompletionMethod));
                    break;

                case SCN_AUTOCCOMPLETED:
                    OnAutoCCompleted(new AutoCSelectionEventArgs(this, scn.position.ToInt32(), scn.text, scn.ch, (ListCompletionMethod)scn.listCompletionMethod));
                    break;

                case SCN_AUTOCCANCELLED:
                    OnAutoCCancelled(EventArgs.Empty);
                    break;

                case SCN_AUTOCCHARDELETED:
                    OnAutoCCharDeleted(EventArgs.Empty);
                    break;

                case SCN_DWELLSTART:
                    OnDwellStart(new DwellEventArgs(this, scn.position.ToInt32(), scn.x, scn.y));
                    break;

                case SCN_DWELLEND:
                    OnDwellEnd(new DwellEventArgs(this, scn.position.ToInt32(), scn.x, scn.y));
                    break;

                case SCN_DOUBLECLICK:
                    ScnDoubleClick(ref scn);
                    break;

                case SCN_NEEDSHOWN:
                    OnNeedShown(new NeedShownEventArgs(this, scn.position.ToInt32(), scn.length.ToInt32()));
                    break;

                case SCN_HOTSPOTCLICK:
                case SCN_HOTSPOTDOUBLECLICK:
                case SCN_HOTSPOTRELEASECLICK:
                    ScnHotspotClick(ref scn);
                    break;

                case SCN_INDICATORCLICK:
                case SCN_INDICATORRELEASE:
                    ScnIndicatorClick(ref scn);
                    break;

                case SCN_ZOOM:
                    OnZoomChanged(EventArgs.Empty);
                    break;

                case SCN_CALLTIPCLICK:
                    OnCallTipClick(new CallTipClickEventArgs(this, (CallTipClickType)scn.position.ToInt32()));
                    // scn.position: 1 = Up Arrow, 2 = DownArrow: 0 = Elsewhere
                    break;

                default:
                    // Not our notification
                    base.WndProc(ref m);
                    break;
            }
        }
    }

    /// <summary>
    /// Processes Windows messages.
    /// </summary>
    /// <param name="m">The Windows Message to process.</param>
    protected override void WndProc(ref Message m)
    {
        switch (m.Msg)
        {
            case (WM_REFLECT + WM_NOTIFY):
                WmReflectNotify(ref m);
                break;

            case WM_SETCURSOR:
                DefWndProc(ref m);
                break;

            case WM_LBUTTONDBLCLK:
            case WM_RBUTTONDBLCLK:
            case WM_MBUTTONDBLCLK:
            case WM_XBUTTONDBLCLK:
                doubleClick = true;
                goto default;

            case WM_DESTROY:
                WmDestroy(ref m);
                break;

            default:
                base.WndProc(ref m);
                break;
        }
    }

    /// <summary>
    /// Returns the position where a word ends, searching forward from the position specified.
    /// </summary>
    /// <param name="position">The zero-based document position to start searching from.</param>
    /// <param name="onlyWordCharacters">
    /// true to stop searching at the first non-word character regardless of whether the search started at a word or non-word character.
    /// false to use the first character in the search as a word or non-word indicator and then search for that word or non-word boundary.
    /// </param>
    /// <returns>The zero-based document postion of the word boundary.</returns>
    /// <seealso cref="WordStartPosition" />
    public int WordEndPosition(int position, bool onlyWordCharacters)
    {
        var onlyWordChars = (onlyWordCharacters ? new IntPtr(1) : IntPtr.Zero);
        position = Helpers.Clamp(position, 0, TextLength);
        position = Lines.CharToBytePosition(position);
        position = DirectMessage(SCI_WORDENDPOSITION, new IntPtr(position), onlyWordChars).ToInt32();
        return Lines.ByteToCharPosition(position);
    }

    /// <summary>
    /// Returns the position where a word starts, searching backward from the position specified.
    /// </summary>
    /// <param name="position">The zero-based document position to start searching from.</param>
    /// <param name="onlyWordCharacters">
    /// true to stop searching at the first non-word character regardless of whether the search started at a word or non-word character.
    /// false to use the first character in the search as a word or non-word indicator and then search for that word or non-word boundary.
    /// </param>
    /// <returns>The zero-based document postion of the word boundary.</returns>
    /// <seealso cref="WordEndPosition" />
    public int WordStartPosition(int position, bool onlyWordCharacters)
    {
        var onlyWordChars = (onlyWordCharacters ? new IntPtr(1) : IntPtr.Zero);
        position = Helpers.Clamp(position, 0, TextLength);
        position = Lines.CharToBytePosition(position);
        position = DirectMessage(SCI_WORDSTARTPOSITION, new IntPtr(position), onlyWordChars).ToInt32();
        return Lines.ByteToCharPosition(position);
    }

    /// <summary>
    /// Increases the zoom factor by 1 until it reaches 20 points.
    /// </summary>
    /// <seealso cref="Zoom" />
    public void ZoomIn()
    {
        DirectMessage(SCI_ZOOMIN);
    }

    /// <summary>
    /// Decreases the zoom factor by 1 until it reaches -10 points.
    /// </summary>
    /// <seealso cref="Zoom" />
    public void ZoomOut()
    {
        DirectMessage(SCI_ZOOMOUT);
    }

    /// <summary>
    /// Sets the representation for a specified character string.
    /// </summary>
    /// <param name="encodedString">The encoded string. I.e. the Ohm character: Ω = \u2126.</param>
    /// <param name="representationString">The representation string for the <paramref name="encodedString"/>. I.e. "OHM".</param>
    /// <remarks>The <see cref="ViewWhitespace"/> must be set to <see cref="WhitespaceMode.VisibleAlways"/> for this to work.</remarks>
    public unsafe void SetRepresentation(string encodedString, string representationString)
    {
        var bytesEncoded = Helpers.GetBytes(encodedString, Encoding, zeroTerminated: true);
        var bytesRepresentation = Helpers.GetBytes(representationString, Encoding, zeroTerminated: true);
        fixed (byte* bpEncoded = bytesEncoded)
        {
            fixed (byte* bpRepresentation = bytesRepresentation)
            {
                DirectMessage(SCI_SETREPRESENTATION, new IntPtr(bpEncoded), new IntPtr(bpRepresentation));
            }
        }
    }

    /// <summary>
    /// Sets the representation for a specified character string.
    /// </summary>
    /// <param name="encodedString">The encoded string. I.e. the Ohm character: Ω = \u2126.</param>
    /// <returns>The representation string for the <paramref name="encodedString"/>. I.e. "OHM".</returns>
    public unsafe string GetRepresentation(string encodedString)
    {
        var bytesEncoded = Helpers.GetBytes(encodedString, Encoding, zeroTerminated: true);

        fixed (byte* bpEncoded = bytesEncoded)
        {
            var length = DirectMessage(SCI_GETREPRESENTATION, new IntPtr(bpEncoded), IntPtr.Zero)
                .ToInt32();
            var bytesRepresentation = new byte[length + 1];
            fixed (byte* bpRepresentation = bytesRepresentation)
            {
                DirectMessage(SCI_GETREPRESENTATION, new IntPtr(bpEncoded), new IntPtr(bpRepresentation));
                return Helpers.GetString(new IntPtr(bpRepresentation), length, Encoding);
            }
        }
    }

    /// <summary>
    /// Clears the representation from a specified character string.
    /// </summary>
    /// <param name="encodedString">The encoded string. I.e. the Ohm character: Ω = \u2126.</param>
    public unsafe void ClearRepresentation(string encodedString)
    {
        var bytesEncoded = Helpers.GetBytes(encodedString, Encoding, zeroTerminated: true);
        fixed (byte* bpEncoded = bytesEncoded)
        {
            DirectMessage(SCI_CLEARREPRESENTATION, new IntPtr(bpEncoded), IntPtr.Zero);
        }
    }
    #endregion Methods

    #region Properties
    /// <summary>
    /// Gets or sets the bi-directionality of the Scintilla control.
    /// </summary>
    /// <value>The bi-directionality of the Scintilla control.</value>
    [Category("Behaviour")]
    [Description("The bi-directionality of the Scintilla control.")]
    public BiDirectionalDisplayType BiDirectionality
    {
        get => (BiDirectionalDisplayType) DirectMessage(SCI_GETBIDIRECTIONAL).ToInt32();

        set
        {
            if (value != BiDirectionalDisplayType.Disabled)
            {
                var technology = DirectMessage(SCI_GETTECHNOLOGY).ToInt32();
                if (technology == SC_TECHNOLOGY_DEFAULT)
                {
                    DirectMessage(SCI_SETTECHNOLOGY, new IntPtr(SC_TECHNOLOGY_DIRECTWRITE));
                }
            }

            DirectMessage(SCI_SETBIDIRECTIONAL, new IntPtr((int)value));
        }
    }

    /// <summary>
    /// Gets or sets a value indicating whether the reading layout is from right to left.
    /// </summary>
    /// <value><c>true</c> if reading layout is from right to left; otherwise, <c>false</c>.</value>
    [Category("Behaviour")]
    [Description("A value indicating whether the reading layout is from right to left.")]
    public bool UseRightToLeftReadingLayout
    {
        get
        {
            if (!IsHandleCreated)
            {
                return false;
            }

            long exStyle = Handle.GetWindowLongPtr(WinApiHelpers.GWL_EXSTYLE).ToInt64();
                
            return exStyle == (exStyle | WinApiHelpers.WS_EX_LAYOUTRTL);
        }
        set
        {
            if (!IsHandleCreated)
            {
                return;
            }

            long exStyle = Handle.GetWindowLongPtr(WinApiHelpers.GWL_EXSTYLE).ToInt64();

            if (value)
            {
                var technology = DirectMessage(SCI_GETTECHNOLOGY).ToInt32();
                if (technology != SC_TECHNOLOGY_DEFAULT)
                {
                    DirectMessage(SCI_SETTECHNOLOGY, new IntPtr(SC_TECHNOLOGY_DEFAULT));
                }

                exStyle |= WinApiHelpers.WS_EX_LAYOUTRTL;
            }
            else
            {
                exStyle &= ~WinApiHelpers.WS_EX_LAYOUTRTL;
            }
            Handle.SetWindowLongPtr(WinApiHelpers.GWL_EXSTYLE, new IntPtr(exStyle));
            DirectMessage(SCI_SETFOCUS, new IntPtr(1)); // needs focus to update
        }
    }

    /// <summary>
    /// Gets or sets the caret foreground color for additional selections.
    /// </summary>
    /// <returns>The caret foreground color in additional selections. The default is (127, 127, 127).</returns>
    [Category("Multiple Selection")]
    [Description("The additional caret foreground color.")]
    public Color AdditionalCaretForeColor
    {
        get
        {
            var color = DirectMessage(SCI_GETADDITIONALCARETFORE).ToInt32();
            return ColorTranslator.FromWin32(color);
        }
        set
        {
            var color = ColorTranslator.ToWin32(value);
            DirectMessage(SCI_SETADDITIONALCARETFORE, new IntPtr(color));
        }
    }

    /// <summary>
    /// Gets or sets whether the carets in additional selections will blink.
    /// </summary>
    /// <returns>true if additional selection carets should blink; otherwise, false. The default is true.</returns>
    [DefaultValue(true)]
    [Category("Multiple Selection")]
    [Description("Whether the carets in additional selections should blink.")]
    public bool AdditionalCaretsBlink
    {
        get
        {
            return DirectMessage(SCI_GETADDITIONALCARETSBLINK) != IntPtr.Zero;
        }
        set
        {
            var additionalCaretsBlink = (value ? new IntPtr(1) : IntPtr.Zero);
            DirectMessage(SCI_SETADDITIONALCARETSBLINK, additionalCaretsBlink);
        }
    }

    /// <summary>
    /// Gets or sets whether the carets in additional selections are visible.
    /// </summary>
    /// <returns>true if additional selection carets are visible; otherwise, false. The default is true.</returns>
    [DefaultValue(true)]
    [Category("Multiple Selection")]
    [Description("Whether the carets in additional selections are visible.")]
    public bool AdditionalCaretsVisible
    {
        get
        {
            return DirectMessage(SCI_GETADDITIONALCARETSVISIBLE) != IntPtr.Zero;
        }
        set
        {
            var additionalCaretsBlink = (value ? new IntPtr(1) : IntPtr.Zero);
            DirectMessage(SCI_SETADDITIONALCARETSVISIBLE, additionalCaretsBlink);
        }
    }

    /// <summary>
    /// Gets or sets the alpha transparency of additional multiple selections.
    /// </summary>
    /// <returns>
    /// The alpha transparency ranging from 0 (completely transparent) to 255 (completely opaque).
    /// The value 256 will disable alpha transparency. The default is 256.
    /// </returns>
    [DefaultValue(256)]
    [Category("Multiple Selection")]
    [Description("The transparency of additional selections.")]
    public int AdditionalSelAlpha
    {
        get
        {
            return DirectMessage(SCI_GETADDITIONALSELALPHA).ToInt32();
        }
        set
        {
            value = Helpers.Clamp(value, 0, SC_ALPHA_NOALPHA);
            DirectMessage(SCI_SETADDITIONALSELALPHA, new IntPtr(value));
        }
    }

    /// <summary>
    /// Gets or sets whether additional typing affects multiple selections.
    /// </summary>
    /// <returns>true if typing will affect multiple selections instead of just the main selection; otherwise, false. The default is false.</returns>
    [DefaultValue(false)]
    [Category("Multiple Selection")]
    [Description("Whether typing, backspace, or delete works with multiple selection simultaneously.")]
    public bool AdditionalSelectionTyping
    {
        get
        {
            return DirectMessage(SCI_GETADDITIONALSELECTIONTYPING) != IntPtr.Zero;
        }
        set
        {
            var additionalSelectionTyping = (value ? new IntPtr(1) : IntPtr.Zero);
            DirectMessage(SCI_SETADDITIONALSELECTIONTYPING, additionalSelectionTyping);
        }
    }

    /// <summary>
    /// Gets or sets the current anchor position.
    /// </summary>
    /// <returns>The zero-based character position of the anchor.</returns>
    /// <remarks>
    /// Setting the current anchor position will create a selection between it and the <see cref="CurrentPosition" />.
    /// The caret is not scrolled into view.
    /// </remarks>
    /// <seealso cref="ScrollCaret" />
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int AnchorPosition
    {
        get
        {
            var bytePos = DirectMessage(SCI_GETANCHOR).ToInt32();
            return Lines.ByteToCharPosition(bytePos);
        }
        set
        {
            value = Helpers.Clamp(value, 0, TextLength);
            var bytePos = Lines.CharToBytePosition(value);
            DirectMessage(SCI_SETANCHOR, new IntPtr(bytePos));
        }
    }

    /// <summary>
    /// Gets or sets the display of annotations.
    /// </summary>
    /// <returns>One of the <see cref="Annotation" /> enumeration values. The default is <see cref="Annotation.Hidden" />.</returns>
    [DefaultValue(Annotation.Hidden)]
    [Category("Appearance")]
    [Description("Display and location of annotations.")]
    public Annotation AnnotationVisible
    {
        get
        {
            return (Annotation)DirectMessage(SCI_ANNOTATIONGETVISIBLE).ToInt32();
        }
        set
        {
            var visible = (int)value;
            DirectMessage(SCI_ANNOTATIONSETVISIBLE, new IntPtr(visible));
        }
    }

    /// <summary>
    /// Gets a value indicating whether there is an autocompletion list displayed.
    /// </summary>
    /// <returns>true if there is an active autocompletion list; otherwise, false.</returns>
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool AutoCActive
    {
        get
        {
            return DirectMessage(SCI_AUTOCACTIVE) != IntPtr.Zero;
        }
    }

    /// <summary>
    /// Gets or sets whether to automatically cancel autocompletion when there are no viable matches.
    /// </summary>
    /// <returns>
    /// true to automatically cancel autocompletion when there is no possible match; otherwise, false.
    /// The default is true.
    /// </returns>
    [DefaultValue(true)]
    [Category("Autocompletion")]
    [Description("Whether to automatically cancel autocompletion when no match is possible.")]
    public bool AutoCAutoHide
    {
        get
        {
            return DirectMessage(SCI_AUTOCGETAUTOHIDE) != IntPtr.Zero;
        }
        set
        {
            var autoHide = (value ? new IntPtr(1) : IntPtr.Zero);
            DirectMessage(SCI_AUTOCSETAUTOHIDE, autoHide);
        }
    }

    /// <summary>
    /// Gets or sets whether to cancel an autocompletion if the caret moves from its initial location,
    /// or is allowed to move to the word start.
    /// </summary>
    /// <returns>
    /// true to cancel autocompletion when the caret moves.
    /// false to allow the caret to move to the beginning of the word without cancelling autocompletion.
    /// </returns>
    [DefaultValue(true)]
    [Category("Autocompletion")]
    [Description("Whether to cancel an autocompletion if the caret moves from its initial location, or is allowed to move to the word start.")]
    public bool AutoCCancelAtStart
    {
        get
        {
            return DirectMessage(SCI_AUTOCGETCANCELATSTART) != IntPtr.Zero;
        }
        set
        {
            var cancel = (value ? new IntPtr(1) : IntPtr.Zero);
            DirectMessage(SCI_AUTOCSETCANCELATSTART, cancel);
        }
    }

    /// <summary>
    /// Gets the index of the current autocompletion list selection.
    /// </summary>
    /// <returns>The zero-based index of the current autocompletion selection.</returns>
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int AutoCCurrent
    {
        get
        {
            return DirectMessage(SCI_AUTOCGETCURRENT).ToInt32();
        }
    }

    /// <summary>
    /// Gets or sets whether to automatically select an item when it is the only one in an autocompletion list.
    /// </summary>
    /// <returns>
    /// true to automatically choose the only autocompletion item and not display the list; otherwise, false.
    /// The default is false.
    /// </returns>
    [DefaultValue(false)]
    [Category("Autocompletion")]
    [Description("Whether to automatically choose an autocompletion item when it is the only one in the list.")]
    public bool AutoCChooseSingle
    {
        get
        {
            return DirectMessage(SCI_AUTOCGETCHOOSESINGLE) != IntPtr.Zero;
        }
        set
        {
            var chooseSingle = (value ? new IntPtr(1) : IntPtr.Zero);
            DirectMessage(SCI_AUTOCSETCHOOSESINGLE, chooseSingle);
        }
    }

    /// <summary>
    /// Gets or sets whether to delete any word characters following the caret after an autocompletion.
    /// </summary>
    /// <returns>
    /// true to delete any word characters following the caret after autocompletion; otherwise, false.
    /// The default is false.</returns>
    [DefaultValue(false)]
    [Category("Autocompletion")]
    [Description("Whether to delete any existing word characters following the caret after autocompletion.")]
    public bool AutoCDropRestOfWord
    {
        get
        {
            return DirectMessage(SCI_AUTOCGETDROPRESTOFWORD) != IntPtr.Zero;
        }
        set
        {
            var dropRestOfWord = (value ? new IntPtr(1) : IntPtr.Zero);
            DirectMessage(SCI_AUTOCSETDROPRESTOFWORD, dropRestOfWord);
        }
    }

    /// <summary>
    /// Gets or sets whether matching characters to an autocompletion list is case-insensitive.
    /// </summary>
    /// <returns>true to use case-insensitive matching; otherwise, false. The default is false.</returns>
    [DefaultValue(false)]
    [Category("Autocompletion")]
    [Description("Whether autocompletion word matching can ignore case.")]
    public bool AutoCIgnoreCase
    {
        get
        {
            return DirectMessage(SCI_AUTOCGETIGNORECASE) != IntPtr.Zero;
        }
        set
        {
            var ignoreCase = (value ? new IntPtr(1) : IntPtr.Zero);
            DirectMessage(SCI_AUTOCSETIGNORECASE, ignoreCase);
        }
    }

    /// <summary>
    /// Gets or sets the maximum height of the autocompletion list measured in rows.
    /// </summary>
    /// <returns>The max number of rows to display in an autocompletion window. The default is 5.</returns>
    /// <remarks>If there are more items in the list than max rows, a vertical scrollbar is shown.</remarks>
    [DefaultValue(5)]
    [Category("Autocompletion")]
    [Description("The maximum number of rows to display in an autocompletion list.")]
    public int AutoCMaxHeight
    {
        get
        {
            return DirectMessage(SCI_AUTOCGETMAXHEIGHT).ToInt32();
        }
        set
        {
            value = Helpers.ClampMin(value, 0);
            DirectMessage(SCI_AUTOCSETMAXHEIGHT, new IntPtr(value));
        }
    }

    /// <summary>
    /// Gets or sets the width in characters of the autocompletion list.
    /// </summary>
    /// <returns>
    /// The width of the autocompletion list expressed in characters, or 0 to automatically set the width
    /// to the longest item. The default is 0.
    /// </returns>
    /// <remarks>Any items that cannot be fully displayed will be indicated with ellipsis.</remarks>
    [DefaultValue(0)]
    [Category("Autocompletion")]
    [Description("The width of the autocompletion list measured in characters.")]
    public int AutoCMaxWidth
    {
        get
        {
            return DirectMessage(SCI_AUTOCGETMAXWIDTH).ToInt32();
        }
        set
        {
            value = Helpers.ClampMin(value, 0);
            DirectMessage(SCI_AUTOCSETMAXWIDTH, new IntPtr(value));
        }
    }

    /// <summary>
    /// Gets or sets the autocompletion list sort order to expect when calling <see cref="AutoCShow" />.
    /// </summary>
    /// <returns>One of the <see cref="Order" /> enumeration values. The default is <see cref="Order.Presorted" />.</returns>
    [DefaultValue(Order.Presorted)]
    [Category("Autocompletion")]
    [Description("The order of words in an autocompletion list.")]
    public Order AutoCOrder
    {
        get
        {
            return (Order)DirectMessage(SCI_AUTOCGETORDER).ToInt32();
        }
        set
        {
            var order = (int)value;
            DirectMessage(SCI_AUTOCSETORDER, new IntPtr(order));
        }
    }

    /// <summary>
    /// Gets the document position at the time <see cref="AutoCShow" /> was called.
    /// </summary>
    /// <returns>The zero-based document position at the time <see cref="AutoCShow" /> was called.</returns>
    /// <seealso cref="AutoCShow" />
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int AutoCPosStart
    {
        get
        {
            var pos = DirectMessage(SCI_AUTOCPOSSTART).ToInt32();
            pos = Lines.ByteToCharPosition(pos);

            return pos;
        }
    }

    /// <summary>
    /// Gets or sets the delimiter character used to separate words in an autocompletion list.
    /// </summary>
    /// <returns>The separator character used when calling <see cref="AutoCShow" />. The default is the space character.</returns>
    /// <remarks>The <paramref name="value" /> specified should be limited to printable ASCII characters.</remarks>
    [DefaultValue(' ')]
    [Category("Autocompletion")]
    [Description("The autocompletion list word delimiter. The default is a space character.")]
    public Char AutoCSeparator
    {
        get
        {
            var separator = DirectMessage(SCI_AUTOCGETSEPARATOR).ToInt32();
            return (Char)separator;
        }
        set
        {
            // The autocompletion separator character is stored as a byte within Scintilla,
            // not a character. Thus it's possible for a user to supply a character that does
            // not fit within a single byte. The likelyhood of this, however, seems so remote that
            // I'm willing to risk a possible conversion error to provide a better user experience.
            var separator = (byte)value;
            DirectMessage(SCI_AUTOCSETSEPARATOR, new IntPtr(separator));
        }
    }

    /// <summary>
    /// Gets or sets the delimiter character used to separate words and image type identifiers in an autocompletion list.
    /// </summary>
    /// <returns>The separator character used to reference an image registered with <see cref="RegisterRgbaImage" />. The default is '?'.</returns>
    /// <remarks>The <paramref name="value" /> specified should be limited to printable ASCII characters.</remarks>
    [DefaultValue('?')]
    [Category("Autocompletion")]
    [Description("The autocompletion list image type delimiter.")]
    public Char AutoCTypeSeparator
    {
        get
        {
            var separatorCharacter = DirectMessage(SCI_AUTOCGETTYPESEPARATOR).ToInt32();
            return (Char)separatorCharacter;
        }
        set
        {
            // The autocompletion type separator character is stored as a byte within Scintilla,
            // not a character. Thus it's possible for a user to supply a character that does
            // not fit within a single byte. The likelyhood of this, however, seems so remote that
            // I'm willing to risk a possible conversion error to provide a better user experience.
            var separatorCharacter = (byte)value;
            DirectMessage(SCI_AUTOCSETTYPESEPARATOR, new IntPtr(separatorCharacter));
        }
    }

    /// <summary>
    /// Gets or sets the automatic folding flags.
    /// </summary>
    /// <returns>
    /// A bitwise combination of the <see cref="Abstractions.Enumerations.AutomaticFold" /> enumeration.
    /// The default is <see cref="Abstractions.Enumerations.AutomaticFold.None" />.
    /// </returns>
    [DefaultValue(AutomaticFold.None)]
    [Category("Behavior")]
    [Description("Options for allowing the control to automatically handle folding.")]
    [TypeConverter(typeof(FlagsEnumConverter))]
    public AutomaticFold AutomaticFold
    {
        get
        {
            return (AutomaticFold)DirectMessage(SCI_GETAUTOMATICFOLD);
        }
        set
        {
            var automaticFold = (int)value;
            DirectMessage(SCI_SETAUTOMATICFOLD, new IntPtr(automaticFold));
        }
    }

    /// <summary>
    /// Not supported.
    /// </summary>
    [Browsable(false)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public override Color BackColor
    {
        get
        {
            return base.BackColor;
        }
        set
        {
            base.BackColor = value;
        }
    }

    /// <summary>
    /// Not supported.
    /// </summary>
    [Browsable(false)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public override Image BackgroundImage
    {
        get
        {
            return base.BackgroundImage;
        }
        set
        {
            base.BackgroundImage = value;
        }
    }

    /// <summary>
    /// Not supported.
    /// </summary>
    [Browsable(false)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public override ImageLayout BackgroundImageLayout
    {
        get
        {
            return base.BackgroundImageLayout;
        }
        set
        {
            base.BackgroundImageLayout = value;
        }
    }

    /// <summary>
    /// Gets or sets whether backspace deletes a character, or unindents.
    /// </summary>
    /// <returns>Whether backspace deletes a character, (false) or unindents (true).</returns>
    [DefaultValue(false)]
    [Category("Indentation")]
    [Description("Determines whether backspace deletes a character, or unindents.")]
    public bool BackspaceUnIndents
    {
        get
        {
            return (DirectMessage(SCI_GETBACKSPACEUNINDENTS) != IntPtr.Zero);
        }
        set
        {
            var ptr = (value ? new IntPtr(1) : IntPtr.Zero);
            DirectMessage(SCI_SETBACKSPACEUNINDENTS, ptr);
        }
    }

    /// <summary>
    /// Gets or sets the border type of the <see cref="Scintilla" /> control.
    /// </summary>
    /// <returns>A BorderStyle enumeration value that represents the border type of the control. The default is Fixed3D.</returns>
    /// <exception cref="InvalidEnumArgumentException">A value that is not within the range of valid values for the enumeration was assigned to the property.</exception>
    [DefaultValue(BorderStyle.Fixed3D)]
    [Category("Appearance")]
    [Description("Indicates whether the control should have a border.")]
    public BorderStyle BorderStyle
    {
        get
        {
            return borderStyle;
        }
        set
        {
            if (borderStyle != value)
            {
                if (!Enum.IsDefined(typeof(BorderStyle), value))
                    throw new InvalidEnumArgumentException("value", (int)value, typeof(BorderStyle));

                borderStyle = value;
                UpdateStyles();
                OnBorderStyleChanged(EventArgs.Empty);
            }
        }
    }

    /// <summary>
    /// Gets or sets whether drawing is double-buffered.
    /// </summary>
    /// <returns>
    /// true to draw each line into an offscreen bitmap first before copying it to the screen; otherwise, false.
    /// The default is true.
    /// </returns>
    /// <remarks>Disabling buffer can improve performance but will cause flickering.</remarks>
    [DefaultValue(true)]
    [Category("Misc")]
    [Description("Determines whether drawing is double-buffered.")]
    public bool BufferedDraw
    {
        get
        {
            return (DirectMessage(SCI_GETBUFFEREDDRAW) != IntPtr.Zero);
        }
        set
        {
            var isBuffered = (value ? new IntPtr(1) : IntPtr.Zero);
            DirectMessage(SCI_SETBUFFEREDDRAW, isBuffered);
        }
    }

    /*
    /// <summary>
    /// Gets or sets the current position of a call tip.
    /// </summary>
    /// <returns>The zero-based document position indicated when <see cref="CallTipShow" /> was called to display a call tip.</returns>
    [Browsable(false)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public int CallTipPosStart
    {
        get
        {
            var pos = DirectMessage(SCI_CALLTIPPOSSTART).ToInt32();
            if (pos < 0)
                return pos;

            return Lines.ByteToCharPosition(pos);
        }
        set
        {
            value = Helpers.Clamp(value, 0, TextLength);
            value = Lines.CharToBytePosition(value);
            DirectMessage(SCI_CALLTIPSETPOSSTART, new IntPtr(value));
        }
    }
    */

    /// <summary>
    /// Gets a value indicating whether there is a call tip window displayed.
    /// </summary>
    /// <returns>true if there is an active call tip window; otherwise, false.</returns>
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool CallTipActive
    {
        get
        {
            return DirectMessage(SCI_CALLTIPACTIVE) != IntPtr.Zero;
        }
    }

    /// <summary>
    /// Gets a value indicating whether there is text on the clipboard that can be pasted into the document.
    /// </summary>
    /// <returns>true when there is text on the clipboard to paste; otherwise, false.</returns>
    /// <remarks>The document cannot be <see cref="ReadOnly" />  and the selection cannot contain protected text.</remarks>
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool CanPaste
    {
        get
        {
            return (DirectMessage(SCI_CANPASTE) != IntPtr.Zero);
        }
    }

    /// <summary>
    /// Gets a value indicating whether there is an undo action to redo.
    /// </summary>
    /// <returns>true when there is something to redo; otherwise, false.</returns>
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool CanRedo
    {
        get
        {
            return (DirectMessage(SCI_CANREDO) != IntPtr.Zero);
        }
    }

    /// <summary>
    /// Gets a value indicating whether there is an action to undo.
    /// </summary>
    /// <returns>true when there is something to undo; otherwise, false.</returns>
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool CanUndo
    {
        get
        {
            return (DirectMessage(SCI_CANUNDO) != IntPtr.Zero);
        }
    }

    /// <summary>
    /// Gets or sets the caret foreground color.
    /// </summary>
    /// <returns>The caret foreground color. The default is black.</returns>
    [DefaultValue(typeof(Color), "Black")]
    [Category("Caret")]
    [Description("The caret foreground color.")]
    public Color CaretForeColor
    {
        get
        {
            var color = DirectMessage(SCI_GETCARETFORE).ToInt32();
            return ColorTranslator.FromWin32(color);
        }
        set
        {
            var color = ColorTranslator.ToWin32(value);
            DirectMessage(SCI_SETCARETFORE, new IntPtr(color));
        }
    }

    /// <summary>
    /// Gets or sets the caret line background color.
    /// </summary>
    /// <returns>The caret line background color. The default is yellow.</returns>
    [DefaultValue(typeof(Color), "Yellow")]
    [Category("Caret")]
    [Description("The background color of the current line.")]
    public Color CaretLineBackColor
    {
        get
        {
            var color = DirectMessage(SCI_GETCARETLINEBACK).ToInt32();
            return ColorTranslator.FromWin32(color);
        }
        set
        {
            var color = ColorTranslator.ToWin32(value);
            DirectMessage(SCI_SETCARETLINEBACK, new IntPtr(color));
        }
    }

    /// <summary>
    /// Gets or sets the alpha transparency of the <see cref="CaretLineBackColor" />.
    /// </summary>
    /// <returns>
    /// The alpha transparency ranging from 0 (completely transparent) to 255 (completely opaque).
    /// The value 256 will disable alpha transparency. The default is 256.
    /// </returns>
    [DefaultValue(256)]
    [Category("Caret")]
    [Description("The transparency of the current line background color.")]
    public int CaretLineBackColorAlpha
    {
        get
        {
            return DirectMessage(SCI_GETCARETLINEBACKALPHA).ToInt32();
        }
        set
        {
            value = Helpers.Clamp(value, 0, SC_ALPHA_NOALPHA);
            DirectMessage(SCI_SETCARETLINEBACKALPHA, new IntPtr(value));
        }
    }

    /// <summary>
    /// Gets or sets the width of the caret line frame.
    /// </summary>
    /// <returns><see cref="CaretLineVisible" /> must be set to true. A value of 0 disables the frame. The default is 0.</returns>
    [DefaultValue(0)]
    [Category("Caret")]
    [Description("The Width of the current line frame.")]
    public int CaretLineFrame
    {
        get
        {
            return DirectMessage(SCI_GETCARETLINEFRAME).ToInt32();
        }
        set
        {
            value = Helpers.ClampMin(value, 0);
            DirectMessage(SCI_SETCARETLINEFRAME, new IntPtr(value));
        }
    }

    /// <summary>
    /// Gets or sets whether the caret line is visible (highlighted).
    /// </summary>
    /// <returns>true if the caret line is visible; otherwise, false. The default is false.</returns>
    [DefaultValue(false)]
    [Category("Caret")]
    [Description("Determines whether to highlight the current caret line.")]
    public bool CaretLineVisible
    {
        get
        {
            return (DirectMessage(SCI_GETCARETLINEVISIBLE) != IntPtr.Zero);
        }
        set
        {
            var visible = (value ? new IntPtr(1) : IntPtr.Zero);
            DirectMessage(SCI_SETCARETLINEVISIBLE, visible);
        }
    }

    /// <summary>
    /// Gets or sets whether the caret line is always visible even when the window is not in focus.
    /// </summary>
    /// <returns>true if the caret line is always visible; otherwise, false. The default is false.</returns>
    [DefaultValue(false)]
    [Category("Caret")]
    [Description("Determines whether the caret line always visible even when the window is not in focus..")]
    public bool CaretLineVisibleAlways
    {
        get
        {
            return (DirectMessage(SCI_GETCARETLINEVISIBLEALWAYS) != IntPtr.Zero);
        }
        set
        {
            var visibleAlways = (value ? new IntPtr(1) : IntPtr.Zero);
            DirectMessage(SCI_SETCARETLINEVISIBLEALWAYS, visibleAlways);
        }
    }

    /// <summary>
    /// Gets or sets the layer where the line caret will be painted. Default value is <see cref="Layer.Base"/>
    /// </summary>
    [DefaultValue(Layer.Base)]
    [Category("Caret")]
    [Description("The layer where the line caret will be painted.")]
    public Layer CaretLineLayer
    {
        get
        {
            return (Layer)DirectMessage(SCI_GETCARETLINELAYER).ToInt32();
        }
        set
        {
            int layer = (int)value;
            DirectMessage(SCI_SETCARETLINELAYER, new IntPtr(layer));
        }
    }

    /// <summary>
    /// Gets or sets the caret blink rate in milliseconds.
    /// </summary>
    /// <returns>The caret blink rate measured in milliseconds. The default is 530.</returns>
    /// <remarks>A value of 0 will stop the caret blinking.</remarks>
    [DefaultValue(530)]
    [Category("Caret")]
    [Description("The caret blink rate in milliseconds.")]
    public int CaretPeriod
    {
        get
        {
            return DirectMessage(SCI_GETCARETPERIOD).ToInt32();
        }
        set
        {
            value = Helpers.ClampMin(value, 0);
            DirectMessage(SCI_SETCARETPERIOD, new IntPtr(value));
        }
    }

    /// <summary>
    /// Gets or sets the caret display style.
    /// </summary>
    /// <returns>
    /// One of the <see cref="Abstractions.Enumerations.CaretStyle" /> enumeration values.
    /// The default is <see cref="Line" />.
    /// </returns>
    [DefaultValue(CaretStyle.Line)]
    [Category("Caret")]
    [Description("The caret display style.")]
    public CaretStyle CaretStyle
    {
        get
        {
            return (CaretStyle)DirectMessage(SCI_GETCARETSTYLE).ToInt32();
        }
        set
        {
            var style = (int)value;
            DirectMessage(SCI_SETCARETSTYLE, new IntPtr(style));
        }
    }

    /// <summary>
    /// Gets or sets the width in pixels of the caret.
    /// </summary>
    /// <returns>The width of the caret in pixels. The default is 1 pixel.</returns>
    /// <remarks>
    /// The caret width can only be set to a value of 0, 1, 2 or 3 pixels and is only effective
    /// when the <see cref="CaretStyle" /> property is set to <see cref="Line" />.
    /// </remarks>
    [DefaultValue(1)]
    [Category("Caret")]
    [Description("The width of the caret line measured in pixels (between 0 and 3).")]
    public int CaretWidth
    {
        get
        {
            return DirectMessage(SCI_GETCARETWIDTH).ToInt32();
        }
        set
        {
            value = Helpers.Clamp(value, 0, 3);
            DirectMessage(SCI_SETCARETWIDTH, new IntPtr(value));
        }
    }

    [DllImport("Scintilla.dll", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode, EntryPoint = "Scintilla_DirectFunction")]
    private static extern IntPtr DirectFunction(IntPtr sciPtr, int msg, IntPtr wParam, IntPtr lParam);

    /// <summary>
    /// Gets the required creation parameters when the control handle is created.
    /// </summary>
    /// <returns>A CreateParams that contains the required creation parameters when the handle to the control is created.</returns>
    protected override CreateParams CreateParams
    {
        get
        {
            if (moduleHandle == IntPtr.Zero)
            {
                // Load the native Scintilla library
                moduleHandle = NativeMethods.LoadLibrary(modulePathScintilla);
                lexillaHandle = NativeMethods.LoadLibrary(modulePathLexilla);

                if (moduleHandle == IntPtr.Zero)
                {
                    var message = string.Format(CultureInfo.InvariantCulture, "Could not load the Scintilla module at the path '{0}'.", modulePathScintilla);
                    throw new Win32Exception(message, new Win32Exception()); // Calls GetLastError
                }

                // For some reason the 32-bit DLL has weird export names.
                var is32Bit = IntPtr.Size == 4;

                // Self-compiled DLLs required this:
                //var exportName = is32Bit
                //    ? "_Scintilla_DirectFunction@16"
                //    : nameof(Scintilla_DirectFunction);
                    
                // Native DLL:
                var exportName = nameof(NativeMethods.Scintilla_DirectFunction);

                // Get the native Scintilla direct function -- the only function the library exports
                var directFunctionPointer = NativeMethods.GetProcAddress(new HandleRef(this, moduleHandle), exportName);
                if (directFunctionPointer == IntPtr.Zero)
                {
                    var message = "The Scintilla module has no export for the 'Scintilla_DirectFunction' procedure.";
                    throw new Win32Exception(message, new Win32Exception()); // Calls GetLastError
                }

                // Get the native Lexilla.dll methods
                lexilla = new Lexilla(lexillaHandle);

                // Create a managed callback
                //directFunction = (NativeMethods.Scintilla_DirectFunction)Marshal.GetDelegateForFunctionPointer(
                //    directFunctionPointer,
                //    typeof(NativeMethods.Scintilla_DirectFunction));
            }

            CreateParams cp = base.CreateParams;
            cp.ClassName = "Scintilla";

            // The border effect is achieved through a native Windows style
            cp.ExStyle &= (~WS_EX_CLIENTEDGE);
            cp.Style &= (~WS_BORDER);
            switch (borderStyle)
            {
                case BorderStyle.Fixed3D:
                    cp.ExStyle |= WS_EX_CLIENTEDGE;
                    break;
                case BorderStyle.FixedSingle:
                    cp.Style |= WS_BORDER;
                    break;
            }

            return cp;
        }
    }

    /// <summary>
    /// Gets the current line index.
    /// </summary>
    /// <returns>The zero-based line index containing the <see cref="CurrentPosition" />.</returns>
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int CurrentLine
    {
        get
        {
            var currentPos = DirectMessage(SCI_GETCURRENTPOS).ToInt32();
            var line = DirectMessage(SCI_LINEFROMPOSITION, new IntPtr(currentPos)).ToInt32();
            return line;
        }
    }

    /// <summary>
    /// Gets or sets the current caret position.
    /// </summary>
    /// <returns>The zero-based character position of the caret.</returns>
    /// <remarks>
    /// Setting the current caret position will create a selection between it and the current <see cref="AnchorPosition" />.
    /// The caret is not scrolled into view.
    /// </remarks>
    /// <seealso cref="ScrollCaret" />
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int CurrentPosition
    {
        get
        {
            var bytePos = DirectMessage(SCI_GETCURRENTPOS).ToInt32();
            return Lines.ByteToCharPosition(bytePos);
        }
        set
        {
            value = Helpers.Clamp(value, 0, TextLength);
            var bytePos = Lines.CharToBytePosition(value);
            DirectMessage(SCI_SETCURRENTPOS, new IntPtr(bytePos));
        }
    }

    /// <summary>
    /// Not supported.
    /// </summary>
    [Browsable(false)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public override Cursor Cursor
    {
        get
        {
            return base.Cursor;
        }
        set
        {
            base.Cursor = value;
        }
    }

    /// <summary>
    /// Gets or sets the default cursor for the control.
    /// </summary>
    /// <returns>An object of type Cursor representing the current default cursor.</returns>
    protected override Cursor DefaultCursor
    {
        get
        {
            return Cursors.IBeam;
        }
    }

    /// <summary>
    /// Gets the default size of the control.
    /// </summary>
    /// <returns>The default Size of the control.</returns>
    protected override Size DefaultSize
    {
        get
        {
            // I've discovered that using a DefaultSize property other than 'empty' triggers a flaw (IMO)
            // in Windows Forms that will cause CreateParams to be called in the base constructor.
            // That's too early. It makes it impossible to use the Site or DesignMode properties during
            // handle creation because they haven't been set yet. Since we don't currently depend on those
            // properties it's okay, but if we need them this is the place to start fixing things.

            return new Size(200, 100);
        }
    }

    /// <summary>
    /// Gets a value indicating the start index of the secondary styles.
    /// </summary>
    /// <returns>Returns the distance between a primary style and its corresponding secondary style.</returns>
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int DistanceToSecondaryStyles
    {
        get
        {
            return DirectMessage(SCI_DISTANCETOSECONDARYSTYLES).ToInt32();
        }
    }

    /// <summary>
    /// Gets or sets the current document used by the control.
    /// </summary>
    /// <returns>The current <see cref="Document" />.</returns>
    /// <remarks>
    /// Setting this property is equivalent to calling <see cref="ReleaseDocument" /> on the current document, and
    /// calling <see cref="CreateDocument" /> if the new <paramref name="value" /> is <see cref="Abstractions.Structs.Document.Empty" /> or
    /// <see cref="AddRefDocument" /> if the new <paramref name="value" /> is not <see cref="Abstractions.Structs.Document.Empty" />.
    /// </remarks>
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Document Document
    {
        get
        {
            var ptr = DirectMessage(SCI_GETDOCPOINTER);
            return new Document { Value = ptr };
        }
        set
        {
            var eolMode = EolMode;
            var useTabs = UseTabs;
            var tabWidth = TabWidth;
            var indentWidth = IndentWidth;

            var ptr = value.Value;
            DirectMessage(SCI_SETDOCPOINTER, IntPtr.Zero, ptr);

            // Carry over properties to new document
            InitDocument(eolMode, useTabs, tabWidth, indentWidth);

            // Rebuild the line cache
            Lines.RebuildLineData();
        }
    }

    /// <summary>
    /// Gets or sets the background color to use when indicating long lines with
    /// <see cref="VisualStyleElement.TrayNotify.Background" />.
    /// </summary>
    /// <returns>The background Color. The default is Silver.</returns>
    [DefaultValue(typeof(Color), "Silver")]
    [Category("Long Lines")]
    [Description("The background color to use when indicating long lines.")]
    public Color EdgeColor
    {
        get
        {
            var color = DirectMessage(SCI_GETEDGECOLOUR).ToInt32();
            return ColorTranslator.FromWin32(color);
        }
        set
        {
            var color = ColorTranslator.ToWin32(value);
            DirectMessage(SCI_SETEDGECOLOUR, new IntPtr(color));
        }
    }

    /// <summary>
    /// Gets or sets the column number at which to begin indicating long lines.
    /// </summary>
    /// <returns>The number of columns in a long line. The default is 0.</returns>
    /// <remarks>
    /// When using <see cref="Line"/>, a column is defined as the width of a space character in the <see cref="Style.Default" /> style.
    /// When using <see cref="VisualStyleElement.TrayNotify.Background" /> a column is equal to a character (including tabs).
    /// </remarks>
    [DefaultValue(0)]
    [Category("Long Lines")]
    [Description("The number of columns at which to display long line indicators.")]
    public int EdgeColumn
    {
        get
        {
            return DirectMessage(SCI_GETEDGECOLUMN).ToInt32();
        }
        set
        {
            value = Helpers.ClampMin(value, 0);
            DirectMessage(SCI_SETEDGECOLUMN, new IntPtr(value));
        }
    }

    /// <summary>
    /// Gets or sets the mode for indicating long lines.
    /// </summary>
    /// <returns>
    /// One of the <see cref="Abstractions.Enumerations.EdgeMode" /> enumeration values.
    /// The default is <see cref="Abstractions.Enumerations.EdgeMode.None" />.
    /// </returns>
    [DefaultValue(EdgeMode.None)]
    [Category("Long Lines")]
    [Description("Determines how long lines are indicated.")]
    public EdgeMode EdgeMode
    {
        get
        {
            return (EdgeMode)DirectMessage(SCI_GETEDGEMODE);
        }
        set
        {
            var edgeMode = (int)value;
            DirectMessage(SCI_SETEDGEMODE, new IntPtr(edgeMode));
        }
    }

    public Encoding Encoding
    {
        get
        {
            // Should always be UTF-8 unless someone has done an end run around us
            int codePage = (int)DirectMessage(SCI_GETCODEPAGE);
            return (codePage == 0 ? Encoding.Default : Encoding.GetEncoding(codePage));
        }
    }

    /// <summary>
    /// Gets or sets whether vertical scrolling ends at the last line or can scroll past.
    /// </summary>
    /// <returns>true if the maximum vertical scroll position ends at the last line; otherwise, false. The default is true.</returns>
    [DefaultValue(true)]
    [Category("Scrolling")]
    [Description("Determines whether the maximum vertical scroll position ends at the last line or can scroll past.")]
    public bool EndAtLastLine
    {
        get
        {
            return (DirectMessage(SCI_GETENDATLASTLINE) != IntPtr.Zero);
        }
        set
        {
            var endAtLastLine = (value ? new IntPtr(1) : IntPtr.Zero);
            DirectMessage(SCI_SETENDATLASTLINE, endAtLastLine);
        }
    }

    /// <summary>
    /// Gets or sets the end-of-line mode, or rather, the characters added into
    /// the document when the user presses the Enter key.
    /// </summary>
    /// <returns>One of the <see cref="Eol" /> enumeration values. The default is <see cref="Eol.CrLf" />.</returns>
    [DefaultValue(Eol.CrLf)]
    [Category("Line Endings")]
    [Description("Determines the characters added into the document when the user presses the Enter key.")]
    public Eol EolMode
    {
        get
        {
            return (Eol)DirectMessage(SCI_GETEOLMODE);
        }
        set
        {
            var eolMode = (int)value;
            DirectMessage(SCI_SETEOLMODE, new IntPtr(eolMode));
        }
    }

    /// <summary>
    /// Gets or sets the amount of whitespace added to the ascent (top) of each line.
    /// </summary>
    /// <returns>The extra line ascent. The default is zero.</returns>
    [DefaultValue(0)]
    [Category("Whitespace")]
    [Description("Extra whitespace added to the ascent (top) of each line.")]
    public int ExtraAscent
    {
        get
        {
            return DirectMessage(SCI_GETEXTRAASCENT).ToInt32();
        }
        set
        {
            DirectMessage(SCI_SETEXTRAASCENT, new IntPtr(value));
        }
    }

    /// <summary>
    /// Gets or sets the amount of whitespace added to the descent (bottom) of each line.
    /// </summary>
    /// <returns>The extra line descent. The default is zero.</returns>
    [DefaultValue(0)]
    [Category("Whitespace")]
    [Description("Extra whitespace added to the descent (bottom) of each line.")]
    public int ExtraDescent
    {
        get
        {
            return DirectMessage(SCI_GETEXTRADESCENT).ToInt32();
        }
        set
        {
            DirectMessage(SCI_SETEXTRADESCENT, new IntPtr(value));
        }
    }

    /// <summary>
    /// Gets or sets the first visible line on screen.
    /// </summary>
    /// <returns>The zero-based index of the first visible screen line.</returns>
    /// <remarks>The value is a visible line, not a document line.</remarks>
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int FirstVisibleLine
    {
        get
        {
            return DirectMessage(SCI_GETFIRSTVISIBLELINE).ToInt32();
        }
        set
        {
            value = Helpers.ClampMin(value, 0);
            DirectMessage(SCI_SETFIRSTVISIBLELINE, new IntPtr(value));
        }
    }

    /// <summary>
    /// Gets or sets the font of the text displayed by the control.
    /// </summary>
    /// <returns>The <see cref="T:System.Drawing.Font" /> to apply to the text displayed by the control. The default is the value of the <see cref="P:System.Windows.Forms.Control.DefaultFont" /> property.</returns>
    [Category("Appearance")]
    [Description("The font of the text displayed by the control.")]
    public override Font Font
    {
        get
        {
            if (!IsHandleCreated)
            {
                return base.Font;
            }

            var defaultFontStyle = Styles[Style.Default];

            var fontStyle = defaultFontStyle.Bold ? FontStyle.Bold : FontStyle.Regular;

            if (defaultFontStyle.Italic)
            {
                fontStyle |= FontStyle.Italic;
            }

            if (defaultFontStyle.Underline)
            {
                fontStyle |= FontStyle.Underline;
            }

            return new Font(defaultFontStyle.Font, defaultFontStyle.SizeF, fontStyle);
        }

        set
        {
            var defaultFontStyle = Styles[Style.Default];
            defaultFontStyle.Font = value.Name;
            defaultFontStyle.SizeF = value.Size;
            defaultFontStyle.Bold = value.Bold;
            defaultFontStyle.Italic = value.Italic;
            defaultFontStyle.Underline = value.Underline;
            base.Font = value;
        }
    }

    /// <summary>
    /// Gets or sets font quality (anti-aliasing method) used to render fonts.
    /// </summary>
    /// <returns>
    /// One of the <see cref="Abstractions.Enumerations.FontQuality" /> enumeration values.
    /// The default is <see cref="Abstractions.Enumerations.FontQuality.Default" />.
    /// </returns>
    [DefaultValue(FontQuality.Default)]
    [Category("Misc")]
    [Description("Specifies the anti-aliasing method to use when rendering fonts.")]
    public FontQuality FontQuality
    {
        get
        {
            return (FontQuality)DirectMessage(SCI_GETFONTQUALITY);
        }
        set
        {
            var fontQuality = (int)value;
            DirectMessage(SCI_SETFONTQUALITY, new IntPtr(fontQuality));
        }
    }

    /// <summary>
    /// Not supported.
    /// </summary>
    [Browsable(false)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public override Color ForeColor
    {
        get
        {
            return base.ForeColor;
        }
        set
        {
            base.ForeColor = value;
        }
    }

    /// <summary>
    /// Gets or sets the column number of the indentation guide to highlight.
    /// </summary>
    /// <returns>The column number of the indentation guide to highlight or 0 if disabled.</returns>
    /// <remarks>Guides are highlighted in the <see cref="Style.BraceLight" /> style. Column numbers can be determined by calling <see cref="GetColumn" />.</remarks>
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int HighlightGuide
    {
        get
        {
            return DirectMessage(SCI_GETHIGHLIGHTGUIDE).ToInt32();
        }
        set
        {
            value = Helpers.ClampMin(value, 0);
            DirectMessage(SCI_SETHIGHLIGHTGUIDE, new IntPtr(value));
        }
    }

    /// <summary>
    /// Gets or sets whether to display the horizontal scroll bar.
    /// </summary>
    /// <returns>true to display the horizontal scroll bar when needed; otherwise, false. The default is true.</returns>
    [DefaultValue(true)]
    [Category("Scrolling")]
    [Description("Determines whether to show the horizontal scroll bar if needed.")]
    public bool HScrollBar
    {
        get
        {
            return (DirectMessage(SCI_GETHSCROLLBAR) != IntPtr.Zero);
        }
        set
        {
            var visible = (value ? new IntPtr(1) : IntPtr.Zero);
            DirectMessage(SCI_SETHSCROLLBAR, visible);
        }
    }

    /// <summary>
    /// Gets or sets the strategy used to perform styling using application idle time.
    /// </summary>
    /// <returns>
    /// One of the <see cref="Abstractions.Enumerations.IdleStyling" /> enumeration values.
    /// The default is <see cref="Abstractions.Enumerations.IdleStyling.None" />.
    /// </returns>
    [DefaultValue(IdleStyling.None)]
    [Category("Misc")]
    [Description("Specifies how to use application idle time for styling.")]
    public IdleStyling IdleStyling
    {
        get
        {
            return (IdleStyling)DirectMessage(SCI_GETIDLESTYLING);
        }
        set
        {
            var idleStyling = (int)value;
            DirectMessage(SCI_SETIDLESTYLING, new IntPtr(idleStyling));
        }
    }

    /// <summary>
    /// Gets or sets the size of indentation in terms of space characters.
    /// </summary>
    /// <returns>The indentation size measured in characters. The default is 0.</returns>
    /// <remarks> A value of 0 will make the indent width the same as the tab width.</remarks>
    [DefaultValue(0)]
    [Category("Indentation")]
    [Description("The indentation size in characters or 0 to make it the same as the tab width.")]
    public int IndentWidth
    {
        get
        {
            return DirectMessage(SCI_GETINDENT).ToInt32();
        }
        set
        {
            value = Helpers.ClampMin(value, 0);
            DirectMessage(SCI_SETINDENT, new IntPtr(value));
        }
    }

    /// <summary>
    /// Gets or sets whether to display indentation guides.
    /// </summary>
    /// <returns>One of the <see cref="IndentView" /> enumeration values. The default is <see cref="IndentView.None" />.</returns>
    /// <remarks>The <see cref="Style.IndentGuide" /> style can be used to specify the foreground and background color of indentation guides.</remarks>
    [DefaultValue(IndentView.None)]
    [Category("Indentation")]
    [Description("Indicates whether indentation guides are displayed.")]
    public IndentView IndentationGuides
    {
        get
        {
            return (IndentView)DirectMessage(SCI_GETINDENTATIONGUIDES);
        }
        set
        {
            var indentView = (int)value;
            DirectMessage(SCI_SETINDENTATIONGUIDES, new IntPtr(indentView));
        }
    }

    /// <summary>
    /// Gets or sets the indicator used in a subsequent call to <see cref="IndicatorFillRange" /> or <see cref="IndicatorClearRange" />.
    /// </summary>
    /// <returns>The zero-based indicator index to apply when calling <see cref="IndicatorFillRange" /> or remove when calling <see cref="IndicatorClearRange" />.</returns>
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int IndicatorCurrent
    {
        get
        {
            return DirectMessage(SCI_GETINDICATORCURRENT).ToInt32();
        }
        set
        {
            value = Helpers.Clamp(value, 0, Indicators.Count - 1);
            DirectMessage(SCI_SETINDICATORCURRENT, new IntPtr(value));
        }
    }

    /// <summary>
    /// Gets a collection of objects for working with indicators.
    /// </summary>
    /// <returns>A collection of <see cref="Indicator" /> objects.</returns>
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public IndicatorCollection Indicators { get; private set; }

    /// <summary>
    /// Gets or sets the user-defined value used in a subsequent call to <see cref="IndicatorFillRange" />.
    /// </summary>
    /// <returns>The indicator value to apply when calling <see cref="IndicatorFillRange" />.</returns>
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int IndicatorValue
    {
        get
        {
            return DirectMessage(SCI_GETINDICATORVALUE).ToInt32();
        }
        set
        {
            DirectMessage(SCI_SETINDICATORVALUE, new IntPtr(value));
        }
    }

    /// <summary>
    /// This is used by clients that have complex focus requirements such as having their own window
    /// that gets the real focus but with the need to indicate that Scintilla has the logical focus.
    /// </summary>
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool InternalFocusFlag
    {
        get
        {
            return (DirectMessage(SCI_GETFOCUS) != IntPtr.Zero);
        }
        set
        {
            var focus = (value ? new IntPtr(1) : IntPtr.Zero);
            DirectMessage(SCI_SETFOCUS, focus);
        }
    }

    private string lexerName;

    /// <summary>
    /// Gets or sets the name of the lexer.
    /// </summary>
    /// <value>The name of the lexer.</value>
    /// <exception cref="InvalidOperationException">Lexer with the name of 'Value' was not found.</exception>
    [Category("Lexing")]
    public string LexerName
    {
        get => lexerName;

        set
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                lexerName = value;

                return;
            }

            if (!SetLexerByName(value))
            {
                throw new InvalidOperationException(@$"Lexer with the name of '{value}' was not found.");
            }

            lexerName = value;
        }
    }

    /// <summary>
    /// Gets or sets the current lexer.
    /// </summary>
    /// <returns>One of the <see cref="Lexer" /> enumeration values. The default is <see cref="Container" />.</returns>
    /// <exception cref="InvalidOperationException">
    /// No lexer name was found with the specified value.
    /// </exception>
    /// <remarks>This property will get more obsolete as time passes as the Scintilla v.5+ now uses strings to define lexers. The Lexer enumeration is not maintained.</remarks>
    [DefaultValue(Lexer.NotFound)]
    [Category("Lexing")]
    [Description("The current lexer.")]
    [Obsolete("This property will get more obsolete as time passes as the Scintilla v.5+ now uses strings to define lexers. Please use the LexerName property instead.")]
    public Lexer Lexer
    {
        get
        {
            if (string.IsNullOrWhiteSpace(lexerName))
            {
                return Lexer.NotFound;
            }


            if (NameConstantMap.ContainsValue(lexerName))
            {
                return (Lexer) NameConstantMap.First(f => f.Value == lexerName).Key;
            }

            return Lexer.NotFound;
        }
        set
        {
            if (value == Lexer.NotFound)
            {
                return;
            }

            var lexer = (int)value;

            if (NameConstantMap.ContainsKey(lexer))
            {
                lexerName = NameConstantMap.First(f => f.Key == lexer).Value;

                if (string.IsNullOrEmpty(lexerName))
                {
                    throw new InvalidOperationException(@"No lexer name was found with the specified value.");
                }
            }
            else
            {
                throw new InvalidOperationException(@"No lexer name was found with the specified value.");
            }


            SetLexerByName(lexerName);
        }
    }

    /// <summary>
    /// Gets or sets the current lexer by name.
    /// </summary>
    /// <returns>A String representing the current lexer.</returns>
    /// <remarks>Lexer names are case-sensitive.</remarks>
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public unsafe string LexerLanguage
    {
        get
        {
            var length = DirectMessage(SCI_GETLEXERLANGUAGE).ToInt32();
            if (length == 0)
                return string.Empty;

            var bytes = new byte[length + 1];
            fixed (byte* bp = bytes)
            {
                DirectMessage(SCI_GETLEXERLANGUAGE, IntPtr.Zero, new IntPtr(bp));
                return Helpers.GetString(new IntPtr(bp), length, Encoding.ASCII);
            }
        }
        set
        {
            if (string.IsNullOrEmpty(value))
            {
                DirectMessage(SCI_SETLEXERLANGUAGE, IntPtr.Zero, IntPtr.Zero);
            }
            else
            {
                var bytes = Helpers.GetBytes(value, Encoding.ASCII, zeroTerminated: true);
                fixed (byte* bp = bytes)
                    DirectMessage(SCI_SETLEXERLANGUAGE, IntPtr.Zero, new IntPtr(bp));
            }
        }
    }

    /// <summary>
    /// Gets the combined result of the <see cref="LineEndTypesSupported" /> and <see cref="LineEndTypesAllowed" />
    /// properties to report the line end types actively being interpreted.
    /// </summary>
    /// <returns>A bitwise combination of the <see cref="LineEndType" /> enumeration.</returns>
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public LineEndType LineEndTypesActive
    {
        get
        {
            return (LineEndType)DirectMessage(SCI_GETLINEENDTYPESACTIVE);
        }
    }

    /// <summary>
    /// Gets or sets the line ending types interpreted by the <see cref="Scintilla" /> control.
    /// </summary>
    /// <returns>
    /// A bitwise combination of the <see cref="LineEndType" /> enumeration.
    /// The default is <see cref="LineEndType.Default" />.
    /// </returns>
    /// <remarks>The line ending types allowed must also be supported by the current lexer to be effective.</remarks>
    [DefaultValue(LineEndType.Default)]
    [Category("Line Endings")]
    [Description("Line endings types interpreted by the control.")]
    [TypeConverter(typeof(FlagsEnumConverter))]
    public LineEndType LineEndTypesAllowed
    {
        get
        {
            return (LineEndType)DirectMessage(SCI_GETLINEENDTYPESALLOWED);
        }
        set
        {
            var lineEndBitsSet = (int)value;
            DirectMessage(SCI_SETLINEENDTYPESALLOWED, new IntPtr(lineEndBitsSet));
        }
    }

    /// <summary>
    /// Gets the different types of line ends supported by the current lexer.
    /// </summary>
    /// <returns>A bitwise combination of the <see cref="LineEndType" /> enumeration.</returns>
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public LineEndType LineEndTypesSupported
    {
        get
        {
            return (LineEndType)DirectMessage(SCI_GETLINEENDTYPESSUPPORTED);
        }
    }

    /// <summary>
    /// Gets a collection representing lines of text in the <see cref="Scintilla" /> control.
    /// </summary>
    /// <returns>A collection of text lines.</returns>
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public LineCollection Lines { get; private set; }

    /// <summary>
    /// Gets the number of lines that can be shown on screen given a constant
    /// line height and the space available.
    /// </summary>
    /// <returns>
    /// The number of screen lines which could be displayed (including any partial lines).
    /// </returns>
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int LinesOnScreen
    {
        get
        {
            return DirectMessage(SCI_LINESONSCREEN).ToInt32();
        }
    }

    /// <summary>
    /// Gets or sets the main selection when their are multiple selections.
    /// </summary>
    /// <returns>The zero-based main selection index.</returns>
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int MainSelection
    {
        get
        {
            return DirectMessage(SCI_GETMAINSELECTION).ToInt32();
        }
        set
        {
            value = Helpers.ClampMin(value, 0);
            DirectMessage(SCI_SETMAINSELECTION, new IntPtr(value));
        }
    }

    /// <summary>
    /// Gets a collection representing margins in a <see cref="Scintilla" /> control.
    /// </summary>
    /// <returns>A collection of margins.</returns>
    [Category("Collections")]
    [Description("The margins collection.")]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Content)]
    [TypeConverter(typeof(ExpandableObjectConverter))]
    public MarginCollection Margins { get; private set; }

    /// <summary>
    /// Gets a collection representing markers in a <see cref="Scintilla" /> control.
    /// </summary>
    /// <returns>A collection of markers.</returns>
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public MarkerCollection Markers { get; private set; }

    /// <summary>
    /// Gets a value indicating whether the document has been modified (is dirty)
    /// since the last call to <see cref="SetSavePoint" />.
    /// </summary>
    /// <returns>true if the document has been modified; otherwise, false.</returns>
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool Modified
    {
        get
        {
            return (DirectMessage(SCI_GETMODIFY) != IntPtr.Zero);
        }
    }

    /// <summary>
    /// Gets or sets the time in milliseconds the mouse must linger to generate a <see cref="DwellStart" /> event.
    /// </summary>
    /// <returns>
    /// The time in milliseconds the mouse must linger to generate a <see cref="DwellStart" /> event
    /// or <see cref="Scintilla.TimeForever" /> if dwell events are disabled.
    /// </returns>
    [DefaultValue(ApiConstants.TimeForever)]
    [Category("Behavior")]
    [Description("The time in milliseconds the mouse must linger to generate a dwell start event. A value of 10000000 disables dwell events.")]
    public int MouseDwellTime
    {
        get
        {
            return DirectMessage(SCI_GETMOUSEDWELLTIME).ToInt32();
        }
        set
        {
            value = Helpers.ClampMin(value, 0);
            DirectMessage(SCI_SETMOUSEDWELLTIME, new IntPtr(value));
        }
    }

    /// <summary>
    /// Gets or sets the ability to switch to rectangular selection mode while making a selection with the mouse.
    /// </summary>
    /// <returns>
    /// true if the current mouse selection can be switched to a rectangular selection by pressing the ALT key; otherwise, false.
    /// The default is false.
    /// </returns>
    [DefaultValue(false)]
    [Category("Multiple Selection")]
    [Description("Enable or disable the ability to switch to rectangular selection mode while making a selection with the mouse.")]
    public bool MouseSelectionRectangularSwitch
    {
        get
        {
            return DirectMessage(SCI_GETMOUSESELECTIONRECTANGULARSWITCH) != IntPtr.Zero;
        }
        set
        {
            var mouseSelectionRectangularSwitch = (value ? new IntPtr(1) : IntPtr.Zero);
            DirectMessage(SCI_SETMOUSESELECTIONRECTANGULARSWITCH, mouseSelectionRectangularSwitch);
        }
    }

    // The MouseWheelCaptures property doesn't seem to work correctly in Windows Forms so hiding for now...
    // P.S. I'm avoiding the MouseDownCaptures property (SCI_SETMOUSEDOWNCAPTURES & SCI_GETMOUSEDOWNCAPTURES) for the same reason... I don't expect it to work in Windows Forms.

    /* 
    /// <summary>
    /// Gets or sets whether to respond to mouse wheel messages if the control has focus but the mouse is not currently over the control.
    /// </summary>
    /// <returns>
    /// true to respond to mouse wheel messages even when the mouse is not currently over the control; otherwise, false.
    /// The default is true.
    /// </returns>
    /// <remarks>Scintilla will still react to the mouse wheel if the mouse pointer is over the editor window.</remarks>
    [DefaultValue(true)]
    [Category("Mouse")]
    [Description("Enable or disable mouse wheel support when the mouse is outside the control bounds, but the control still has focus.")]
    public bool MouseWheelCaptures
    {
        get
        {
            return DirectMessage(SCI_GETMOUSEWHEELCAPTURES) != IntPtr.Zero;
        }
        set
        {
            var mouseWheelCaptures = (value ? new IntPtr(1) : IntPtr.Zero);
            DirectMessage(SCI_SETMOUSEWHEELCAPTURES, mouseWheelCaptures);
        }
    }
    */

    /// <summary>
    /// Gets or sets whether multiple selection is enabled.
    /// </summary>
    /// <returns>
    /// true if multiple selections can be made by holding the CTRL key and dragging the mouse; otherwise, false.
    /// The default is false.
    /// </returns>
    [DefaultValue(false)]
    [Category("Multiple Selection")]
    [Description("Enable or disable multiple selection with the CTRL key.")]
    public bool MultipleSelection
    {
        get
        {
            return DirectMessage(SCI_GETMULTIPLESELECTION) != IntPtr.Zero;
        }
        set
        {
            var multipleSelection = (value ? new IntPtr(1) : IntPtr.Zero);
            DirectMessage(SCI_SETMULTIPLESELECTION, multipleSelection);
        }
    }

    /// <summary>
    /// Gets or sets the behavior when pasting text into multiple selections.
    /// </summary>
    /// <returns>One of the <see cref="MultiPaste" /> enumeration values. The default is <see cref="global::Scintilla.NET.Abstractions.Enumerations.MultiPaste.Once" />.</returns>
    [DefaultValue(MultiPaste.Once)]
    [Category("Multiple Selection")]
    [Description("Determines how pasted text is applied to multiple selections.")]
    public MultiPaste MultiPaste
    {
        get
        {
            return (MultiPaste)DirectMessage(SCI_GETMULTIPASTE);
        }
        set
        {
            var multiPaste = (int)value;
            DirectMessage(SCI_SETMULTIPASTE, new IntPtr(multiPaste));
        }
    }

    /// <summary>
    /// Gets or sets whether to write over text rather than insert it.
    /// </summary>
    /// <return>true to write over text; otherwise, false. The default is false.</return>
    [DefaultValue(false)]
    [Category("Behavior")]
    [Description("Puts the caret into overtype mode.")]
    public bool OverType
    {
        get
        {
            return (DirectMessage(SCI_GETOVERTYPE) != IntPtr.Zero);
        }
        set
        {
            var overtype = (value ? new IntPtr(1) : IntPtr.Zero);
            DirectMessage(SCI_SETOVERTYPE, overtype);
        }
    }

    /// <summary>
    /// Not supported.
    /// </summary>
    [Browsable(false)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public new Padding Padding
    {
        get
        {
            return base.Padding;
        }
        set
        {
            base.Padding = value;
        }
    }

    /// <summary>
    /// Gets or sets whether line endings in pasted text are convereted to the document <see cref="EolMode" />.
    /// </summary>
    /// <returns>true to convert line endings in pasted text; otherwise, false. The default is true.</returns>
    [DefaultValue(true)]
    [Category("Line Endings")]
    [Description("Whether line endings in pasted text are converted to match the document end-of-line mode.")]
    public bool PasteConvertEndings
    {
        get
        {
            return (DirectMessage(SCI_GETPASTECONVERTENDINGS) != IntPtr.Zero);
        }
        set
        {
            var convert = (value ? new IntPtr(1) : IntPtr.Zero);
            DirectMessage(SCI_SETPASTECONVERTENDINGS, convert);
        }
    }

    /// <summary>
    /// Gets or sets the number of phases used when drawing.
    /// </summary>
    /// <returns>One of the <see cref="Phases" /> enumeration values. The default is <see cref="Phases.Two" />.</returns>
    [DefaultValue(Phases.Two)]
    [Category("Misc")]
    [Description("Adjusts the number of phases used when drawing.")]
    public Phases PhasesDraw
    {
        get
        {
            return (Phases)DirectMessage(SCI_GETPHASESDRAW);
        }
        set
        {
            var phases = (int)value;
            DirectMessage(SCI_SETPHASESDRAW, new IntPtr(phases));
        }
    }

    /// <summary>
    /// Gets or sets whether the document is read-only.
    /// </summary>
    /// <returns>true if the document is read-only; otherwise, false. The default is false.</returns>
    /// <seealso cref="ModifyAttempt" />
    [DefaultValue(false)]
    [Category("Behavior")]
    [Description("Controls whether the document text can be modified.")]
    public bool ReadOnly
    {
        get
        {
            return (DirectMessage(SCI_GETREADONLY) != IntPtr.Zero);
        }
        set
        {
            var readOnly = (value ? new IntPtr(1) : IntPtr.Zero);
            DirectMessage(SCI_SETREADONLY, readOnly);
        }
    }

    /// <summary>
    /// Gets or sets the anchor position of the rectangular selection.
    /// </summary>
    /// <returns>The zero-based document position of the rectangular selection anchor.</returns>
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int RectangularSelectionAnchor
    {
        get
        {
            var pos = DirectMessage(SCI_GETRECTANGULARSELECTIONANCHOR).ToInt32();
            if (pos <= 0)
                return pos;

            return Lines.ByteToCharPosition(pos);
        }
        set
        {
            value = Helpers.Clamp(value, 0, TextLength);
            value = Lines.CharToBytePosition(value);
            DirectMessage(SCI_SETRECTANGULARSELECTIONANCHOR, new IntPtr(value));
        }
    }

    /// <summary>
    /// Gets or sets the amount of anchor virtual space in a rectangular selection.
    /// </summary>
    /// <returns>The amount of virtual space past the end of the line offsetting the rectangular selection anchor.</returns>
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int RectangularSelectionAnchorVirtualSpace
    {
        get
        {
            return DirectMessage(SCI_GETRECTANGULARSELECTIONANCHORVIRTUALSPACE).ToInt32();
        }
        set
        {
            value = Helpers.ClampMin(value, 0);
            DirectMessage(SCI_SETRECTANGULARSELECTIONANCHORVIRTUALSPACE, new IntPtr(value));
        }
    }

    /// <summary>
    /// Gets or sets the caret position of the rectangular selection.
    /// </summary>
    /// <returns>The zero-based document position of the rectangular selection caret.</returns>
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int RectangularSelectionCaret
    {
        get
        {
            var pos = DirectMessage(SCI_GETRECTANGULARSELECTIONCARET).ToInt32();
            if (pos <= 0)
                return 0;

            return Lines.ByteToCharPosition(pos);
        }
        set
        {
            value = Helpers.Clamp(value, 0, TextLength);
            value = Lines.CharToBytePosition(value);
            DirectMessage(SCI_SETRECTANGULARSELECTIONCARET, new IntPtr(value));
        }
    }

    /// <summary>
    /// Gets or sets the amount of caret virtual space in a rectangular selection.
    /// </summary>
    /// <returns>The amount of virtual space past the end of the line offsetting the rectangular selection caret.</returns>
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int RectangularSelectionCaretVirtualSpace
    {
        get
        {
            return DirectMessage(SCI_GETRECTANGULARSELECTIONCARETVIRTUALSPACE).ToInt32();
        }
        set
        {
            value = Helpers.ClampMin(value, 0);
            DirectMessage(SCI_SETRECTANGULARSELECTIONCARETVIRTUALSPACE, new IntPtr(value));
        }
    }

    public IntPtr SciPointer
    {
        get
        {
            // Enforce illegal cross-thread calls the way the Handle property does
            if (Control.CheckForIllegalCrossThreadCalls && InvokeRequired)
            {
                string message = string.Format(CultureInfo.InvariantCulture, "Control '{0}' accessed from a thread other than the thread it was created on.", Name);
                throw new InvalidOperationException(message);
            }

            if (sciPtr == IntPtr.Zero)
            {
                // Get a pointer to the native Scintilla object (i.e. C++ 'this') to use with the
                // direct function. This will happen for each Scintilla control instance.
                sciPtr = NativeMethods.SendMessage(new HandleRef(this, Handle), SCI_GETDIRECTPOINTER, IntPtr.Zero, IntPtr.Zero);
            }

            return sciPtr;
        }
    }

    /// <summary>
    /// Gets or sets the range of the horizontal scroll bar.
    /// </summary>
    /// <returns>The range in pixels of the horizontal scroll bar. The default is 2000.</returns>
    /// <remarks>The width will automatically increase as needed when <see cref="ScrollWidthTracking" /> is enabled.</remarks>
    [DefaultValue(2000)]
    [Category("Scrolling")]
    [Description("The range in pixels of the horizontal scroll bar.")]
    public int ScrollWidth
    {
        get
        {
            return DirectMessage(SCI_GETSCROLLWIDTH).ToInt32();
        }
        set
        {
            DirectMessage(SCI_SETSCROLLWIDTH, new IntPtr(value));
        }
    }

    /// <summary>
    /// Gets or sets whether the <see cref="ScrollWidth" /> is automatically increased as needed.
    /// </summary>
    /// <returns>
    /// true to automatically increase the horizontal scroll width as needed; otherwise, false.
    /// The default is true.
    /// </returns>
    [DefaultValue(true)]
    [Category("Scrolling")]
    [Description("Determines whether to increase the horizontal scroll width as needed.")]
    public bool ScrollWidthTracking
    {
        get
        {
            return (DirectMessage(SCI_GETSCROLLWIDTHTRACKING) != IntPtr.Zero);
        }
        set
        {
            var tracking = (value ? new IntPtr(1) : IntPtr.Zero);
            DirectMessage(SCI_SETSCROLLWIDTHTRACKING, tracking);
        }
    }

    /// <summary>
    /// Gets or sets the search flags used when searching text.
    /// </summary>
    /// <returns>A bitwise combination of <see cref="global::Scintilla.NET.Abstractions.Enumerations.SearchFlags" /> values. The default is <see cref="global::Scintilla.NET.Abstractions.Enumerations.SearchFlags.None" />.</returns>
    /// <seealso cref="SearchInTarget" />
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public SearchFlags SearchFlags
    {
        get
        {
            return (SearchFlags)DirectMessage(SCI_GETSEARCHFLAGS).ToInt32();
        }
        set
        {
            var searchFlags = (int)value;
            DirectMessage(SCI_SETSEARCHFLAGS, new IntPtr(searchFlags));
        }
    }

    /// <summary>
    /// Gets the selected text.
    /// </summary>
    /// <returns>The selected text if there is any; otherwise, an empty string.</returns>
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public unsafe string SelectedText
    {
        get
        {
            // NOTE: For some reason the length returned by this API includes the terminating NULL
            var length = DirectMessage(SCI_GETSELTEXT).ToInt32();

            if (length <= 0)
                return string.Empty;

            var bytes = new byte[length + 1];
            fixed (byte* bp = bytes)
            {
                DirectMessage(SCI_GETSELTEXT, IntPtr.Zero, new IntPtr(bp));
                return Helpers.GetString(new IntPtr(bp), length, Encoding);
            }
        }
    }

    /// <summary>
    /// Gets or sets the end position of the selection.
    /// </summary>
    /// <returns>The zero-based document position where the selection ends.</returns>
    /// <remarks>
    /// When getting this property, the return value is <code>Math.Max(<see cref="AnchorPosition" />, <see cref="CurrentPosition" />)</code>.
    /// When setting this property, <see cref="CurrentPosition" /> is set to the value specified and <see cref="AnchorPosition" /> set to <code>Math.Min(<see cref="AnchorPosition" />, <paramref name="value" />)</code>.
    /// The caret is not scrolled into view.
    /// </remarks>
    /// <seealso cref="SelectionStart" />
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int SelectionEnd
    {
        get
        {
            var pos = DirectMessage(SCI_GETSELECTIONEND).ToInt32();
            return Lines.ByteToCharPosition(pos);
        }
        set
        {
            value = Helpers.Clamp(value, 0, TextLength);
            value = Lines.CharToBytePosition(value);
            DirectMessage(SCI_SETSELECTIONEND, new IntPtr(value));
        }
    }

    /// <summary>
    /// Gets or sets whether to fill past the end of a line with the selection background color.
    /// </summary>
    /// <returns>true to fill past the end of the line; otherwise, false. The default is false.</returns>
    [DefaultValue(false)]
    [Category("Selection")]
    [Description("Determines whether a selection should fill past the end of the line.")]
    public bool SelectionEolFilled
    {
        get
        {
            return (DirectMessage(SCI_GETSELEOLFILLED) != IntPtr.Zero);
        }
        set
        {
            var eolFilled = (value ? new IntPtr(1) : IntPtr.Zero);
            DirectMessage(SCI_SETSELEOLFILLED, eolFilled);
        }
    }

    /// <summary>
    /// Gets a collection representing multiple selections in a <see cref="Scintilla" /> control.
    /// </summary>
    /// <returns>A collection of selections.</returns>
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public SelectionCollection Selections { get; private set; }

    /// <summary>
    /// Gets or sets the start position of the selection.
    /// </summary>
    /// <returns>The zero-based document position where the selection starts.</returns>
    /// <remarks>
    /// When getting this property, the return value is <code>Math.Min(<see cref="AnchorPosition" />, <see cref="CurrentPosition" />)</code>.
    /// When setting this property, <see cref="AnchorPosition" /> is set to the value specified and <see cref="CurrentPosition" /> set to <code>Math.Max(<see cref="CurrentPosition" />, <paramref name="value" />)</code>.
    /// The caret is not scrolled into view.
    /// </remarks>
    /// <seealso cref="SelectionEnd" />
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int SelectionStart
    {
        get
        {
            var pos = DirectMessage(SCI_GETSELECTIONSTART).ToInt32();
            return Lines.ByteToCharPosition(pos);
        }
        set
        {
            value = Helpers.Clamp(value, 0, TextLength);
            value = Lines.CharToBytePosition(value);
            DirectMessage(SCI_SETSELECTIONSTART, new IntPtr(value));
        }
    }

    /// <summary>
    /// Gets or sets the last internal error code used by Scintilla.
    /// </summary>
    /// <returns>
    /// One of the <see cref="Status" /> enumeration values.
    /// The default is <see cref="global::Scintilla.NET.Abstractions.Enumerations.Status.Ok" />.
    /// </returns>
    /// <remarks>The status can be reset by setting the property to <see cref="global::Scintilla.NET.Abstractions.Enumerations.Status.Ok" />.</remarks>
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Status Status
    {
        get
        {
            return (Status)DirectMessage(SCI_GETSTATUS);
        }
        set
        {
            var status = (int)value;
            DirectMessage(SCI_SETSTATUS, new IntPtr(status));
        }
    }

    /// <summary>
    /// Gets a collection representing style definitions in a <see cref="Scintilla" /> control.
    /// </summary>
    /// <returns>A collection of style definitions.</returns>
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public StyleCollection Styles { get; private set; }

    /// <summary>
    /// Gets or sets how tab characters are represented when whitespace is visible.
    /// </summary>
    /// <returns>
    /// One of the <see cref="global::Scintilla.NET.Abstractions.Enumerations.TabDrawMode" /> enumeration values.
    /// The default is <see cref="global::Scintilla.NET.Abstractions.Enumerations.TabDrawMode.LongArrow" />.
    /// </returns>
    /// <seealso cref="ViewWhitespace" />
    [DefaultValue(TabDrawMode.LongArrow)]
    [Category("Whitespace")]
    [Description("Style of visible tab characters.")]
    public TabDrawMode TabDrawMode
    {
        get
        {
            return (TabDrawMode)DirectMessage(SCI_GETTABDRAWMODE);
        }
        set
        {
            var tabDrawMode = (int)value;
            DirectMessage(SCI_SETTABDRAWMODE, new IntPtr(tabDrawMode));
        }
    }

    /// <summary>
    /// Gets or sets whether tab inserts a tab character, or indents.
    /// </summary>
    /// <returns>Whether tab inserts a tab character (false), or indents (true).</returns>
    [DefaultValue(false)]
    [Category("Indentation")]
    [Description("Determines whether tab inserts a tab character, or indents.")]
    public bool TabIndents
    {
        get
        {
            return (DirectMessage(SCI_GETTABINDENTS) != IntPtr.Zero);
        }
        set
        {
            var ptr = (value ? new IntPtr(1) : IntPtr.Zero);
            DirectMessage(SCI_SETTABINDENTS, ptr);
        }
    }

    /// <summary>
    /// Gets or sets the width of a tab as a multiple of a space character.
    /// </summary>
    /// <returns>The width of a tab measured in characters. The default is 4.</returns>
    [DefaultValue(4)]
    [Category("Indentation")]
    [Description("The tab size in characters.")]
    public int TabWidth
    {
        get
        {
            return DirectMessage(SCI_GETTABWIDTH).ToInt32();
        }
        set
        {
            DirectMessage(SCI_SETTABWIDTH, new IntPtr(value));
        }
    }

    /// <summary>
    /// Gets or sets the end position used when performing a search or replace.
    /// </summary>
    /// <returns>The zero-based character position within the document to end a search or replace operation.</returns>
    /// <seealso cref="TargetStart"/>
    /// <seealso cref="SearchInTarget" />
    /// <seealso cref="ReplaceTarget" />
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int TargetEnd
    {
        get
        {
            // The position can become stale and point to a place outside of the document so we must clamp it
            var bytePos = Helpers.Clamp(DirectMessage(SCI_GETTARGETEND).ToInt32(), 0, DirectMessage(SCI_GETTEXTLENGTH).ToInt32());
            return Lines.ByteToCharPosition(bytePos);
        }
        set
        {
            value = Helpers.Clamp(value, 0, TextLength);
            value = Lines.CharToBytePosition(value);
            DirectMessage(SCI_SETTARGETEND, new IntPtr(value));
        }
    }

    /// <summary>
    /// Gets or sets the start position used when performing a search or replace.
    /// </summary>
    /// <returns>The zero-based character position within the document to start a search or replace operation.</returns>
    /// <seealso cref="TargetEnd"/>
    /// <seealso cref="SearchInTarget" />
    /// <seealso cref="ReplaceTarget" />
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int TargetStart
    {
        get
        {
            // The position can become stale and point to a place outside of the document so we must clamp it
            var bytePos = Helpers.Clamp(DirectMessage(SCI_GETTARGETSTART).ToInt32(), 0, DirectMessage(SCI_GETTEXTLENGTH).ToInt32());
            return Lines.ByteToCharPosition(bytePos);
        }
        set
        {
            value = Helpers.Clamp(value, 0, TextLength);
            value = Lines.CharToBytePosition(value);
            DirectMessage(SCI_SETTARGETSTART, new IntPtr(value));
        }
    }

    /// <summary>
    /// Gets the current target text.
    /// </summary>
    /// <returns>A String representing the text between <see cref="TargetStart" /> and <see cref="TargetEnd" />.</returns>
    /// <remarks>Targets which have a start position equal or greater to the end position will return an empty String.</remarks>
    /// <seealso cref="TargetStart" />
    /// <seealso cref="TargetEnd" />
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public unsafe string TargetText
    {
        get
        {
            var length = DirectMessage(SCI_GETTARGETTEXT).ToInt32();
            if (length == 0)
                return string.Empty;

            var bytes = new byte[length + 1];
            fixed (byte* bp = bytes)
            {
                DirectMessage(SCI_GETTARGETTEXT, IntPtr.Zero, new IntPtr(bp));
                return Helpers.GetString(new IntPtr(bp), length, Encoding);
            }
        }
    }

    /// <summary>
    /// Gets or sets the rendering technology used.
    /// </summary>
    /// <returns>
    /// One of the <see cref="Technology" /> enumeration values.
    /// The default is <see cref="global::Scintilla.NET.Abstractions.Enumerations.Technology.Default" />.
    /// </returns>
    [DefaultValue(Technology.Default)]
    [Category("Misc")]
    [Description("The rendering technology used to draw text.")]
    public Technology Technology
    {
        get
        {
            return (Technology)DirectMessage(SCI_GETTECHNOLOGY);
        }
        set
        {
            var technology = (int)value;
            DirectMessage(SCI_SETTECHNOLOGY, new IntPtr(technology));
        }
    }

    /// <summary>
    /// Gets or sets the current document text in the <see cref="Scintilla" /> control.
    /// </summary>
    /// <returns>The text displayed in the control.</returns>
    /// <remarks>Depending on the length of text get or set, this operation can be expensive.</remarks>
    [Editor("System.ComponentModel.Design.MultilineStringEditor, System.Design", typeof(UITypeEditor))]
    [Description("The text associated with this control.")]
    [Category("Appearance")]
    public override unsafe string Text
    {
        get
        {
            var length = DirectMessage(SCI_GETTEXTLENGTH).ToInt32();
            var ptr = DirectMessage(SCI_GETRANGEPOINTER, new IntPtr(0), new IntPtr(length));
            if (ptr == IntPtr.Zero)
            {
                return string.Empty;
            }

            // Assumption is that moving the gap will always be equal to or less expensive
            // than using one of the APIs which requires an intermediate buffer.
            var text = new string((sbyte*)ptr, 0, length, Encoding);
            return text;
        }
        set
        {
            var previousReadOnly = DesignMode ? ReadOnly : false;

            // Allow Text property change in read-only mode when the designer is active.
            if (previousReadOnly && DesignMode)
            {
                DirectMessage(SCI_SETREADONLY, IntPtr.Zero);
            }

            if (string.IsNullOrEmpty(value))
            {
                DirectMessage(SCI_CLEARALL);
            }
            else if (value.Contains("\0"))
            {
                DirectMessage(SCI_CLEARALL);
                AppendText(value);
            }
            else
            {
                fixed (byte* bp = Helpers.GetBytes(value, Encoding, zeroTerminated: true)) 
                    DirectMessage(SCI_SETTEXT, IntPtr.Zero, new IntPtr(bp));
            }

            // Allow Text property change in read-only mode when the designer is active.
            if (previousReadOnly && DesignMode)
            {
                DirectMessage(SCI_SETREADONLY, new IntPtr(1));
            }
        }
    }

    /// <summary>
    /// Gets the length of the text in the control.
    /// </summary>
    /// <returns>The number of characters in the document.</returns>
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int TextLength => Lines.TextLength;

    /// <summary>
    /// Gets or sets whether to use a mixture of tabs and spaces for indentation or purely spaces.
    /// </summary>
    /// <returns>true to use tab characters; otherwise, false. The default is true.</returns>
    [DefaultValue(false)]
    [Category("Indentation")]
    [Description("Determines whether indentation allows tab characters or purely space characters.")]
    public bool UseTabs
    {
        get
        {
            return (DirectMessage(SCI_GETUSETABS) != IntPtr.Zero);
        }
        set
        {
            var useTabs = (value ? new IntPtr(1) : IntPtr.Zero);
            DirectMessage(SCI_SETUSETABS, useTabs);
        }
    }

    /// <summary>
    /// Gets or sets a value indicating whether to use the wait cursor for the current control.
    /// </summary>
    /// <returns>true to use the wait cursor for the current control; otherwise, false. The default is false.</returns>
    public new bool UseWaitCursor
    {
        get
        {
            return base.UseWaitCursor;
        }
        set
        {
            base.UseWaitCursor = value;
            var cursor = (value ? SC_CURSORWAIT : SC_CURSORNORMAL);
            DirectMessage(SCI_SETCURSOR, new IntPtr(cursor));
        }
    }

    /// <summary>
    /// Gets or sets the visibility of end-of-line characters.
    /// </summary>
    /// <returns>true to display end-of-line characters; otherwise, false. The default is false.</returns>
    [DefaultValue(false)]
    [Category("Line Endings")]
    [Description("Display end-of-line characters.")]
    public bool ViewEol
    {
        get
        {
            return DirectMessage(SCI_GETVIEWEOL) != IntPtr.Zero;
        }
        set
        {
            var visible = (value ? new IntPtr(1) : IntPtr.Zero);
            DirectMessage(SCI_SETVIEWEOL, visible);
        }
    }

    /// <summary>
    /// Gets or sets how to display whitespace characters.
    /// </summary>
    /// <returns>One of the <see cref="WhitespaceMode" /> enumeration values. The default is <see cref="WhitespaceMode.Invisible" />.</returns>
    /// <seealso cref="SetWhitespaceForeColor" />
    /// <seealso cref="SetWhitespaceBackColor" />
    [DefaultValue(WhitespaceMode.Invisible)]
    [Category("Whitespace")]
    [Description("Options for displaying whitespace characters.")]
    public WhitespaceMode ViewWhitespace
    {
        get
        {
            return (WhitespaceMode)DirectMessage(SCI_GETVIEWWS);
        }
        set
        {
            var wsMode = (int)value;
            DirectMessage(SCI_SETVIEWWS, new IntPtr(wsMode));
        }
    }

    /// <summary>
    /// Gets or sets the ability for the caret to move into an area beyond the end of each line, otherwise known as virtual space.
    /// </summary>
    /// <returns>
    /// A bitwise combination of the <see cref="VirtualSpace" /> enumeration.
    /// The default is <see cref="VirtualSpace.None" />.
    /// </returns>
    [DefaultValue(VirtualSpace.None)]
    [Category("Behavior")]
    [Description("Options for allowing the caret to move beyond the end of each line.")]
    [TypeConverter(typeof(FlagsEnumConverter))]
    public VirtualSpace VirtualSpaceOptions
    {
        get
        {
            return (VirtualSpace)DirectMessage(SCI_GETVIRTUALSPACEOPTIONS);
        }
        set
        {
            var virtualSpace = (int)value;
            DirectMessage(SCI_SETVIRTUALSPACEOPTIONS, new IntPtr(virtualSpace));
        }
    }

    /// <summary>
    /// Gets or sets whether to display the vertical scroll bar.
    /// </summary>
    /// <returns>true to display the vertical scroll bar when needed; otherwise, false. The default is true.</returns>
    [DefaultValue(true)]
    [Category("Scrolling")]
    [Description("Determines whether to show the vertical scroll bar when needed.")]
    public bool VScrollBar
    {
        get
        {
            return (DirectMessage(SCI_GETVSCROLLBAR) != IntPtr.Zero);
        }
        set
        {
            var visible = (value ? new IntPtr(1) : IntPtr.Zero);
            DirectMessage(SCI_SETVSCROLLBAR, visible);
        }
    }

    private int VisibleLineCount
    {
        get
        {
            bool wordWrapDisabled = WrapMode == WrapMode.None;
            bool allLinesVisible = Lines.AllLinesVisible;

            if (wordWrapDisabled && allLinesVisible)
            {
                return Lines.Count;
            }

            int count = 0;
            for (int i = 0; i < Lines.Count; i++)
            {
                if (allLinesVisible || this.Lines[i].Visible)
                {
                    count += wordWrapDisabled ? 1 : this.Lines[i].WrapCount;
                }
            }

            return count;
        }
    }

    /// <summary>
    /// Gets or sets the size of the dots used to mark whitespace.
    /// </summary>
    /// <returns>The size of the dots used to mark whitespace. The default is 1.</returns>
    /// <seealso cref="ViewWhitespace" />
    [DefaultValue(1)]
    [Category("Whitespace")]
    [Description("The size of whitespace dots.")]
    public int WhitespaceSize
    {
        get
        {
            return DirectMessage(SCI_GETWHITESPACESIZE).ToInt32();
        }
        set
        {
            DirectMessage(SCI_SETWHITESPACESIZE, new IntPtr(value));
        }
    }

    /// <summary>
    /// Gets or sets the characters considered 'word' characters when using any word-based logic.
    /// </summary>
    /// <returns>A string of word characters.</returns>
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public unsafe string WordChars
    {
        get
        {
            var length = DirectMessage(SCI_GETWORDCHARS, IntPtr.Zero, IntPtr.Zero).ToInt32();
            var bytes = new byte[length + 1];
            fixed (byte* bp = bytes)
            {
                DirectMessage(SCI_GETWORDCHARS, IntPtr.Zero, new IntPtr(bp));
                return Helpers.GetString(new IntPtr(bp), length, Encoding.ASCII);
            }
        }
        set
        {
            if (value == null)
            {
                DirectMessage(SCI_SETWORDCHARS, IntPtr.Zero, IntPtr.Zero);
                return;
            }

            // Scintilla stores each of the characters specified in a char array which it then
            // uses as a lookup for word matching logic. Thus, any multibyte chars wouldn't work.
            var bytes = Helpers.GetBytes(value, Encoding.ASCII, zeroTerminated: true);
            fixed (byte* bp = bytes)
                DirectMessage(SCI_SETWORDCHARS, IntPtr.Zero, new IntPtr(bp));
        }
    }

    /// <summary>
    /// Gets or sets the line wrapping indent mode.
    /// </summary>
    /// <returns>
    /// One of the <see cref="global::Scintilla.NET.Abstractions.Enumerations.WrapIndentMode" /> enumeration values.
    /// The default is <see cref="global::Scintilla.NET.Abstractions.Enumerations.WrapIndentMode.Fixed" />.
    /// </returns>
    [DefaultValue(WrapIndentMode.Fixed)]
    [Category("Line Wrapping")]
    [Description("Determines how wrapped sublines are indented.")]
    public WrapIndentMode WrapIndentMode
    {
        get
        {
            return (WrapIndentMode)DirectMessage(SCI_GETWRAPINDENTMODE);
        }
        set
        {
            var wrapIndentMode = (int)value;
            DirectMessage(SCI_SETWRAPINDENTMODE, new IntPtr(wrapIndentMode));
        }
    }

    /// <summary>
    /// Gets or sets the line wrapping mode.
    /// </summary>
    /// <returns>
    /// One of the <see cref="Scintilla.NET.Abstractions.Enumerations.WrapMode" /> enumeration values.
    /// The default is <see cref="Scintilla.NET.Abstractions.Enumerations.WrapMode.Word" />.
    /// </returns>
    [DefaultValue(WrapMode.Word)]
    [Category("Line Wrapping")]
    [Description("The line wrapping strategy.")]
    public WrapMode WrapMode
    {
        get
        {
            return (WrapMode)DirectMessage(SCI_GETWRAPMODE);
        }
        set
        {
            var wrapMode = (int)value;
            DirectMessage(SCI_SETWRAPMODE, new IntPtr(wrapMode));
        }
    }

    /// <summary>
    /// Gets or sets the indented size in pixels of wrapped sublines.
    /// </summary>
    /// <returns>The indented size of wrapped sublines measured in pixels. The default is 0.</returns>
    /// <remarks>
    /// Setting <see cref="WrapVisualFlags" /> to <see cref="Abstractions.Enumerations.WrapVisualFlags.Start" /> will add an
    /// additional 1 pixel to the value specified.
    /// </remarks>
    [DefaultValue(0)]
    [Category("Line Wrapping")]
    [Description("The amount of pixels to indent wrapped sublines.")]
    public int WrapStartIndent
    {
        get
        {
            return DirectMessage(SCI_GETWRAPSTARTINDENT).ToInt32();
        }
        set
        {
            value = Helpers.ClampMin(value, 0);
            DirectMessage(SCI_SETWRAPSTARTINDENT, new IntPtr(value));
        }
    }

    /// <summary>
    /// Gets or sets the wrap visual flags.
    /// </summary>
    /// <returns>
    /// A bitwise combination of the <see cref="Abstractions.Enumerations.WrapVisualFlags" /> enumeration.
    /// The default is <see cref="Abstractions.Enumerations.WrapVisualFlags.None" />.
    /// </returns>
    [DefaultValue(WrapVisualFlags.None)]
    [Category("Line Wrapping")]
    [Description("The visual indicator displayed on a wrapped line.")]
    [TypeConverter(typeof(FlagsEnumConverter))]
    public WrapVisualFlags WrapVisualFlags
    {
        get
        {
            return (WrapVisualFlags)DirectMessage(SCI_GETWRAPVISUALFLAGS);
        }
        set
        {
            int wrapVisualFlags = (int)value;
            DirectMessage(SCI_SETWRAPVISUALFLAGS, new IntPtr(wrapVisualFlags));
        }
    }

    /// <summary>
    /// Gets or sets additional location options when displaying wrap visual flags.
    /// </summary>
    /// <returns>
    /// One of the <see cref="Abstractions.Enumerations.WrapVisualFlagLocation" /> enumeration values.
    /// The default is <see cref="Abstractions.Enumerations.WrapVisualFlagLocation.Default" />.
    /// </returns>
    [DefaultValue(WrapVisualFlagLocation.Default)]
    [Category("Line Wrapping")]
    [Description("The location of wrap visual flags in relation to the line text.")]
    public WrapVisualFlagLocation WrapVisualFlagLocation
    {
        get
        {
            return (WrapVisualFlagLocation)DirectMessage(SCI_GETWRAPVISUALFLAGSLOCATION);
        }
        set
        {
            var location = (int)value;
            DirectMessage(SCI_SETWRAPVISUALFLAGSLOCATION, new IntPtr(location));
        }
    }

    /// <summary>
    /// Gets or sets the horizontal scroll offset.
    /// </summary>
    /// <returns>The horizontal scroll offset in pixels.</returns>
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int XOffset
    {
        get
        {
            return DirectMessage(SCI_GETXOFFSET).ToInt32();
        }
        set
        {
            value = Helpers.ClampMin(value, 0);
            DirectMessage(SCI_SETXOFFSET, new IntPtr(value));
        }
    }

    /// <summary>
    /// Gets or sets the zoom factor.
    /// </summary>
    /// <returns>The zoom factor measured in points.</returns>
    /// <remarks>For best results, values should range from -10 to 20 points.</remarks>
    /// <seealso cref="ZoomIn" />
    /// <seealso cref="ZoomOut" />
    [DefaultValue(0)]
    [Category("Appearance")]
    [Description("Zoom factor in points applied to the displayed text.")]
    public int Zoom
    {
        get
        {
            return DirectMessage(SCI_GETZOOM).ToInt32();
        }
        set
        {
            DirectMessage(SCI_SETZOOM, new IntPtr(value));
        }
    }

    #endregion Properties

    #region Events

    /// <summary>
    /// Occurs when an autocompletion list is cancelled.
    /// </summary>
    [Category("Notifications")]
    [Description("Occurs when an autocompletion list is cancelled.")]
    public event EventHandler<System.EventArgs> AutoCCancelled
    {
        add
        {
            Events.AddHandler(autoCCancelledEventKey, value);
        }
        remove
        {
            Events.RemoveHandler(autoCCancelledEventKey, value);
        }
    }

    /// <summary>
    /// Occurs when the user deletes a character while an autocompletion list is active.
    /// </summary>
    [Category("Notifications")]
    [Description("Occurs when the user deletes a character while an autocompletion list is active.")]
    public event EventHandler<System.EventArgs> AutoCCharDeleted
    {
        add
        {
            Events.AddHandler(autoCCharDeletedEventKey, value);
        }
        remove
        {
            Events.RemoveHandler(autoCCharDeletedEventKey, value);
        }
    }

    /// <summary>
    /// Occurs after autocompleted text is inserted.
    /// </summary>
    [Category("Notifications")]
    [Description("Occurs after autocompleted text has been inserted.")]
    public event EventHandler<AutoCSelectionEventArgs> AutoCCompleted
    {
        add
        {
            Events.AddHandler(autoCCompletedEventKey, value);
        }
        remove
        {
            Events.RemoveHandler(autoCCompletedEventKey, value);
        }
    }

    /// <summary>
    /// Occurs when a user has selected an item in an autocompletion list.
    /// </summary>
    /// <remarks>Automatic insertion can be cancelled by calling <see cref="AutoCCancel" /> from the event handler.</remarks>
    [Category("Notifications")]
    [Description("Occurs when a user has selected an item in an autocompletion list.")]
    public event EventHandler<AutoCSelectionEventArgs> AutoCSelection
    {
        add
        {
            Events.AddHandler(autoCSelectionEventKey, value);
        }
        remove
        {
            Events.RemoveHandler(autoCSelectionEventKey, value);
        }
    }

    /// <summary>
    /// Not supported.
    /// </summary>
    [Browsable(false)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public new event EventHandler BackColorChanged
    {
        add
        {
            base.BackColorChanged += value;
        }
        remove
        {
            base.BackColorChanged -= value;
        }
    }

    /// <summary>
    /// Not supported.
    /// </summary>
    [Browsable(false)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public new event EventHandler BackgroundImageChanged
    {
        add
        {
            base.BackgroundImageChanged += value;
        }
        remove
        {
            base.BackgroundImageChanged -= value;
        }
    }

    /// <summary>
    /// Not supported.
    /// </summary>
    [Browsable(false)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public new event EventHandler BackgroundImageLayoutChanged
    {
        add
        {
            base.BackgroundImageLayoutChanged += value;
        }
        remove
        {
            base.BackgroundImageLayoutChanged -= value;
        }
    }

    /// <summary>
    /// Occurs when text is about to be deleted.
    /// </summary>
    [Category("Notifications")]
    [Description("Occurs before text is deleted.")]
    public event EventHandler<BeforeModificationEventArgs> BeforeDelete
    {
        add
        {
            Events.AddHandler(beforeDeleteEventKey, value);
        }
        remove
        {
            Events.RemoveHandler(beforeDeleteEventKey, value);
        }
    }

    /// <summary>
    /// Occurs when text is about to be inserted.
    /// </summary>
    [Category("Notifications")]
    [Description("Occurs before text is inserted.")]
    public event EventHandler<BeforeModificationEventArgs> BeforeInsert
    {
        add
        {
            Events.AddHandler(beforeInsertEventKey, value);
        }
        remove
        {
            Events.RemoveHandler(beforeInsertEventKey, value);
        }
    }

    /// <summary>
    /// Occurs when the value of the <see cref="Scintilla.BorderStyle" /> property has changed.
    /// </summary>
    [Category("Property Changed")]
    [Description("Occurs when the value of the BorderStyle property changes.")]
    public event EventHandler BorderStyleChanged
    {
        add
        {
            Events.AddHandler(borderStyleChangedEventKey, value);
        }
        remove
        {
            Events.RemoveHandler(borderStyleChangedEventKey, value);
        }
    }

    /// <summary>
    /// Occurs when an annotation has changed.
    /// </summary>
    [Category("Notifications")]
    [Description("Occurs when an annotation has changed.")]
    public event EventHandler<ChangeAnnotationEventArgs> ChangeAnnotation
    {
        add
        {
            Events.AddHandler(changeAnnotationEventKey, value);
        }
        remove
        {
            Events.RemoveHandler(changeAnnotationEventKey, value);
        }
    }

    /// <summary>
    /// Occurs when the user enters a text character.
    /// </summary>
    [Category("Notifications")]
    [Description("Occurs when the user types a character.")]
    public event EventHandler<CharAddedEventArgs> CharAdded
    {
        add
        {
            Events.AddHandler(charAddedEventKey, value);
        }
        remove
        {
            Events.RemoveHandler(charAddedEventKey, value);
        }
    }

    /// <summary>
    /// Not supported.
    /// </summary>
    [Browsable(false)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public new event EventHandler CursorChanged
    {
        add
        {
            base.CursorChanged += value;
        }
        remove
        {
            base.CursorChanged -= value;
        }
    }

    /// <summary>
    /// Occurs when text has been deleted from the document.
    /// </summary>
    [Category("Notifications")]
    [Description("Occurs when text is deleted.")]
    public event EventHandler<BeforeModificationEventArgs> Delete
    {
        add
        {
            Events.AddHandler(deleteEventKey, value);
        }
        remove
        {
            Events.RemoveHandler(deleteEventKey, value);
        }
    }

    /// <summary>
    /// Occurs when the <see cref="Scintilla" /> control is double-clicked.
    /// </summary>
    [Category("Notifications")]
    [Description("Occurs when the editor is double clicked.")]
    public new event EventHandler<DoubleClickEventArgs> DoubleClick
    {
        add
        {
            Events.AddHandler(doubleClickEventKey, value);
        }
        remove
        {
            Events.RemoveHandler(doubleClickEventKey, value);
        }
    }

    /// <summary>
    /// Occurs when the mouse moves or another activity such as a key press ends a <see cref="DwellStart" /> event.
    /// </summary>
    [Category("Notifications")]
    [Description("Occurs when the mouse moves from its dwell start position.")]
    public event EventHandler<DwellEventArgs> DwellEnd
    {
        add
        {
            Events.AddHandler(dwellEndEventKey, value);
        }
        remove
        {
            Events.RemoveHandler(dwellEndEventKey, value);
        }
    }

    /// <summary>
    /// Occurs when the mouse clicked over a call tip displayed by the <see cref="CallTipShow" /> method.
    /// </summary>
    [Category("Notifications")]
    [Description("Occurs when the mouse is clicked over a calltip.")]
    public event EventHandler<CallTipClickEventArgs> CallTipClick
    {
        add
        {
            Events.AddHandler(callTipClickEventKey, value);
        }
        remove
        {
            Events.RemoveHandler(callTipClickEventKey, value);
        }
    }


    /// <summary>
    /// Occurs when the mouse is kept in one position (hovers) for the <see cref="MouseDwellTime" />.
    /// </summary>
    [Category("Notifications")]
    [Description("Occurs when the mouse is kept in one position (hovers) for a period of time.")]
    public event EventHandler<DwellEventArgs> DwellStart
    {
        add
        {
            Events.AddHandler(dwellStartEventKey, value);
        }
        remove
        {
            Events.RemoveHandler(dwellStartEventKey, value);
        }
    }

    /// <summary>
    /// Not supported.
    /// </summary>
    [Browsable(false)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public new event EventHandler FontChanged
    {
        add
        {
            base.FontChanged += value;
        }
        remove
        {
            base.FontChanged -= value;
        }
    }

    /// <summary>
    /// Not supported.
    /// </summary>
    [Browsable(false)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public new event EventHandler ForeColorChanged
    {
        add
        {
            base.ForeColorChanged += value;
        }
        remove
        {
            base.ForeColorChanged -= value;
        }
    }

    /// <summary>
    /// Occurs when the user clicks on text that is in a style with the <see cref="Style.Hotspot" /> property set.
    /// </summary>
    [Category("Notifications")]
    [Description("Occurs when the user clicks text styled with the hotspot flag.")]
    public event EventHandler<HotspotClickEventArgs> HotspotClick
    {
        add
        {
            Events.AddHandler(hotspotClickEventKey, value);
        }
        remove
        {
            Events.RemoveHandler(hotspotClickEventKey, value);
        }
    }

    /// <summary>
    /// Occurs when the user double clicks on text that is in a style with the <see cref="Style.Hotspot" /> property set.
    /// </summary>
    [Category("Notifications")]
    [Description("Occurs when the user double clicks text styled with the hotspot flag.")]
    public event EventHandler<HotspotClickEventArgs> HotspotDoubleClick
    {
        add
        {
            Events.AddHandler(hotspotDoubleClickEventKey, value);
        }
        remove
        {
            Events.RemoveHandler(hotspotDoubleClickEventKey, value);
        }
    }

    /// <summary>
    /// Occurs when the user releases a click on text that is in a style with the <see cref="Style.Hotspot" /> property set.
    /// </summary>
    [Category("Notifications")]
    [Description("Occurs when the user releases a click on text styled with the hotspot flag.")]
    public event EventHandler<HotspotClickEventArgs> HotspotReleaseClick
    {
        add
        {
            Events.AddHandler(hotspotReleaseClickEventKey, value);
        }
        remove
        {
            Events.RemoveHandler(hotspotReleaseClickEventKey, value);
        }
    }

    /// <summary>
    /// Occurs when the user clicks on text that has an indicator.
    /// </summary>
    [Category("Notifications")]
    [Description("Occurs when the user clicks text with an indicator.")]
    public event EventHandler<IndicatorClickEventArgs> IndicatorClick
    {
        add
        {
            Events.AddHandler(indicatorClickEventKey, value);
        }
        remove
        {
            Events.RemoveHandler(indicatorClickEventKey, value);
        }
    }

    /// <summary>
    /// Occurs when the user releases a click on text that has an indicator.
    /// </summary>
    [Category("Notifications")]
    [Description("Occurs when the user releases a click on text with an indicator.")]
    public event EventHandler<IndicatorReleaseEventArgs> IndicatorRelease
    {
        add
        {
            Events.AddHandler(indicatorReleaseEventKey, value);
        }
        remove
        {
            Events.RemoveHandler(indicatorReleaseEventKey, value);
        }
    }

    /// <summary>
    /// Occurs when text has been inserted into the document.
    /// </summary>
    [Category("Notifications")]
    [Description("Occurs when text is inserted.")]
    public event EventHandler<BeforeModificationEventArgs> Insert
    {
        add
        {
            Events.AddHandler(insertEventKey, value);
        }
        remove
        {
            Events.RemoveHandler(insertEventKey, value);
        }
    }

    /// <summary>
    /// Occurs when text is about to be inserted. The inserted text can be changed.
    /// </summary>
    [Category("Notifications")]
    [Description("Occurs before text is inserted. Permits changing the inserted text.")]
    public event EventHandler<InsertCheckEventArgs> InsertCheck
    {
        add
        {
            Events.AddHandler(insertCheckEventKey, value);
        }
        remove
        {
            Events.RemoveHandler(insertCheckEventKey, value);
        }
    }

    /// <summary>
    /// Occurs when the mouse was clicked inside a margin that was marked as sensitive.
    /// </summary>
    /// <remarks>The <see cref="Margin.Sensitive" /> property must be set for a margin to raise this event.</remarks>
    [Category("Notifications")]
    [Description("Occurs when the mouse is clicked in a sensitive margin.")]
    public event EventHandler<MarginClickEventArgs> MarginClick
    {
        add
        {
            Events.AddHandler(marginClickEventKey, value);
        }
        remove
        {
            Events.RemoveHandler(marginClickEventKey, value);
        }
    }


    // TODO This isn't working in my tests. Could be Windows Forms interfering.
    /// <summary>
    /// Occurs when the mouse was right-clicked inside a margin that was marked as sensitive.
    /// </summary>
    /// <remarks>The <see cref="Margin.Sensitive" /> property and <see cref="PopupMode.Text" /> must be set for a margin to raise this event.</remarks>
    /// <seealso cref="UsePopup(PopupMode)" />
    [Category("Notifications")]
    [Description("Occurs when the mouse is right-clicked in a sensitive margin.")]
    public event EventHandler<MarginClickEventArgs> MarginRightClick
    {
        add
        {
            Events.AddHandler(marginRightClickEventKey, value);
        }
        remove
        {
            Events.RemoveHandler(marginRightClickEventKey, value);
        }
    }

    /// <summary>
    /// Occurs when a user attempts to change text while the document is in read-only mode.
    /// </summary>
    /// <seealso cref="ReadOnly" />
    [Category("Notifications")]
    [Description("Occurs when an attempt is made to change text in read-only mode.")]
    public event EventHandler<EventArgs> ModifyAttempt
    {
        add
        {
            Events.AddHandler(modifyAttemptEventKey, value);
        }
        remove
        {
            Events.RemoveHandler(modifyAttemptEventKey, value);
        }
    }

    /// <summary>
    /// Occurs when the control determines hidden text needs to be shown.
    /// </summary>
    /// <remarks>An example of when this event might be raised is if the end of line of a contracted fold point is deleted.</remarks>
    [Category("Notifications")]
    [Description("Occurs when hidden (folded) text should be shown.")]
    public event EventHandler<NeedShownEventArgs> NeedShown
    {
        add
        {
            Events.AddHandler(needShownEventKey, value);
        }
        remove
        {
            Events.RemoveHandler(needShownEventKey, value);
        }
    }

    public event EventHandler<SCNotificationEventArgs> SCNotification
    {
        add
        {
            Events.AddHandler(scNotificationEventKey, value);
        }
        remove
        {
            Events.RemoveHandler(scNotificationEventKey, value);
        }
    }

    /// <summary>
    /// Not supported.
    /// </summary>
    [Browsable(false)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public new event PaintEventHandler Paint
    {
        add
        {
            base.Paint += value;
        }
        remove
        {
            base.Paint -= value;
        }
    }

    /// <summary>
    /// Occurs when painting has just been done.
    /// </summary>
    [Category("Notifications")]
    [Description("Occurs when the control is painted.")]
    public event EventHandler<EventArgs> Painted
    {
        add
        {
            Events.AddHandler(paintedEventKey, value);
        }
        remove
        {
            Events.RemoveHandler(paintedEventKey, value);
        }
    }

    /// <summary>
    /// Occurs when the document becomes 'dirty'.
    /// </summary>
    /// <remarks>The document 'dirty' state can be checked with the <see cref="Modified" /> property and reset by calling <see cref="SetSavePoint" />.</remarks>
    /// <seealso cref="SetSavePoint" />
    /// <seealso cref="SavePointReached" />
    [Category("Notifications")]
    [Description("Occurs when a save point is left and the document becomes dirty.")]
    public event EventHandler<EventArgs> SavePointLeft
    {
        add
        {
            Events.AddHandler(savePointLeftEventKey, value);
        }
        remove
        {
            Events.RemoveHandler(savePointLeftEventKey, value);
        }
    }

    /// <summary>
    /// Occurs when the document 'dirty' flag is reset.
    /// </summary>
    /// <remarks>The document 'dirty' state can be reset by calling <see cref="SetSavePoint" /> or undoing an action that modified the document.</remarks>
    /// <seealso cref="SetSavePoint" />
    /// <seealso cref="SavePointLeft" />
    [Category("Notifications")]
    [Description("Occurs when a save point is reached and the document is no longer dirty.")]
    public event EventHandler<EventArgs> SavePointReached
    {
        add
        {
            Events.AddHandler(savePointReachedEventKey, value);
        }
        remove
        {
            Events.RemoveHandler(savePointReachedEventKey, value);
        }
    }

    /// <summary>
    /// Occurs when the control is about to display or print text and requires styling.
    /// </summary>
    /// <remarks>
    /// This event is only raised when <see cref="Lexer" /> is set to <see cref="Container" />.
    /// The last position styled correctly can be determined by calling <see cref="GetEndStyled" />.
    /// </remarks>
    /// <seealso cref="GetEndStyled" />
    [Category("Notifications")]
    [Description("Occurs when the text needs styling.")]
    public event EventHandler<StyleNeededEventArgs> StyleNeeded
    {
        add
        {
            Events.AddHandler(styleNeededEventKey, value);
        }
        remove
        {
            Events.RemoveHandler(styleNeededEventKey, value);
        }
    }

    /// <summary>
    /// Occurs when the control UI is updated as a result of changes to text (including styling),
    /// selection, and/or scroll positions.
    /// </summary>
    [Category("Notifications")]
    [Description("Occurs when the control UI is updated.")]
    public event EventHandler<UpdateUIEventArgs> UpdateUi
    {
        add
        {
            Events.AddHandler(updateUIEventKey, value);
        }
        remove
        {
            Events.RemoveHandler(updateUIEventKey, value);
        }
    }

    /// <summary>
    /// Occurs when the user zooms the display using the keyboard or the <see cref="Zoom" /> property is changed.
    /// </summary>
    [Category("Notifications")]
    [Description("Occurs when the control is zoomed.")]
    public event EventHandler<EventArgs> ZoomChanged
    {
        add
        {
            Events.AddHandler(zoomChangedEventKey, value);
        }
        remove
        {
            Events.RemoveHandler(zoomChangedEventKey, value);
        }
    }

    #endregion Events

    #region Constructors

    /// <summary>
    /// Initializes a new instance of the <see cref="Scintilla" /> class.
    /// </summary>
    public Scintilla()
    {
        // WM_DESTROY workaround
        if (Scintilla.reparentAll == null || (bool)Scintilla.reparentAll)
            reparent = true;

        // We don't want .NET to use GetWindowText because we manage ('cache') our own text
        base.SetStyle(ControlStyles.CacheText, true);

        // Necessary control styles (see TextBoxBase)
        base.SetStyle(ControlStyles.StandardClick |
                      ControlStyles.StandardDoubleClick |
                      ControlStyles.UseTextForAccessibility |
                      ControlStyles.UserPaint,
            false);

        this.borderStyle = BorderStyle.Fixed3D;

        Lines = new LineCollection(this);
        Styles = new StyleCollection(this);
        Indicators = new IndicatorCollection(this);
        Margins = new MarginCollection(this);
        Markers = new MarkerCollection(this);
        Selections = new SelectionCollection(this);
    }

    #endregion Constructors

    /// <summary>
    /// Gets or sets a value indicating whether control's elements are aligned to support locales using right-to-left fonts.
    /// </summary>
    /// <value>The right to left.</value>

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    [Obsolete("Not used by the Scintilla.NET control.")]
    public new RightToLeft RightToLeft { get; set; }

    /// <inheritdoc />
    public new IntPtr Handle => base.Handle;
}