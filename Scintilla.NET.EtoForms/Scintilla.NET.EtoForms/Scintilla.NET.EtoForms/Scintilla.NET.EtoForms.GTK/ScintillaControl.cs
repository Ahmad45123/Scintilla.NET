﻿using System.Runtime.InteropServices;
using Eto.Forms;
using Eto.GtkSharp.Forms;
using Gtk;

namespace Scintilla.NET.EtoForms.GTK;

/// <summary>
/// Scintilla control handler for GTK.
/// Implements the <see cref="Eto.GtkSharp.Forms.GtkControl{TControl, TWidget, TCallback}" />
/// Implements the <see cref="IScintillaControl" />
/// </summary>
/// <seealso cref="Eto.GtkSharp.Forms.GtkControl{TControl, TWidget, TCallback}" />
/// <seealso cref="IScintillaControl" />
public class ScintillaControlHandler : GtkControl<Widget, ScintillaControl, Control.ICallback>, IScintillaControl
{
    /// <summary>
    /// Create a new Scintilla widget. The returned pointer can be added to a container and displayed in the same way as other widgets.
    /// </summary>
    /// <returns>IntPtr.</returns>
    [DllImport("scintilla", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
    static extern IntPtr scintilla_new();

    /// <summary>
    /// The main entry point allows sending any of the messages described in this document.
    /// </summary>
    /// <param name="ptr">The ScintillaObject pointer.</param>
    /// <param name="iMessage">The message identifier to send to the control.</param>
    /// <param name="wParam">The message <c>wParam</c> field.</param>
    /// <param name="lParam">The message <c>lParam</c> field.</param>
    /// <returns>IntPtr.</returns>
    [DllImport("scintilla", CallingConvention = CallingConvention.Cdecl)]
    static extern IntPtr scintilla_send_message(IntPtr ptr, int iMessage, IntPtr wParam, IntPtr lParam);

    readonly IntPtr editor;

    /// <summary>
    /// Initializes a new instance of the <see cref="ScintillaControlHandler"/> class.
    /// </summary>
    public ScintillaControlHandler()
    {
        editor = scintilla_new();
        var nativeControl = new Widget(editor);
        Control = nativeControl;
        Lexilla = LexillaSingleton;
    }
    

    private static Lexilla? lexillaInstance;

    /// <summary>
    /// Gets the singleton instance of the <see cref="Lexilla"/> class.
    /// </summary>
    /// <value>The singleton instance of the <see cref="Lexilla"/> class.</value>
    private static ILexilla LexillaSingleton
    {
        get
        {
            lexillaInstance ??= new Lexilla();
            return lexillaInstance;
        }
    }

    /// <inheritdoc cref="IScintillaControl.SetParameter"/>
    public IntPtr SetParameter(int message, IntPtr wParam, IntPtr lParam)
    {
        return scintilla_send_message(editor, message, wParam, lParam);
    }

    /// <inheritdoc cref="IScintillaControl.DirectMessage(int)"/>
    public IntPtr DirectMessage(int msg)
    {
        return SetParameter(msg, IntPtr.Zero, IntPtr.Zero);
    }

    /// <inheritdoc cref="IScintillaControl.DirectMessage(int, IntPtr)"/>
    public IntPtr DirectMessage(int msg, IntPtr wParam)
    {
        return SetParameter(msg, wParam, IntPtr.Zero);
    }

    /// <inheritdoc cref="IScintillaControl.DirectMessage(int, IntPtr, IntPtr)"/>
    public IntPtr DirectMessage(int msg, IntPtr wParam, IntPtr lParam)
    {
        return SetParameter(msg, wParam, lParam);
    }

    /// <inheritdoc cref="IScintillaControl.DirectMessage(int, IntPtr, IntPtr)"/>
    public IntPtr DirectMessage(IntPtr sciPtr, int msg, IntPtr wParam, IntPtr lParam)
    {
        return scintilla_send_message(sciPtr, msg, wParam, lParam);
    }

    /// <summary>
    /// Gets the Lexilla library access.
    /// </summary>
    /// <value>The lexilla library access.</value>
    public ILexilla Lexilla { get; }
}