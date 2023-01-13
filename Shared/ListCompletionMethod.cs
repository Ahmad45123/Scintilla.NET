﻿using Scintilla.NET.Abstractions;

namespace ScintillaNET;

/// <summary>
/// Indicates how an autocompletion occurred.
/// </summary>
public enum ListCompletionMethod
{
    /// <summary>
    /// A fillup character (see <see cref="Scintilla.AutoCSetFillUps" />) triggered the completion.
    /// The character used is indicated by the <see cref="AutoCSelectionEventArgs.Char" /> property.
    /// </summary>
    FillUp = ScintillaConstants.SC_AC_FILLUP,

    /// <summary>
    /// A double-click triggered the completion.
    /// </summary>
    DoubleClick = ScintillaConstants.SC_AC_DOUBLECLICK,

    /// <summary>
    /// A tab key or the <see cref="ScintillaNET.Command.Tab" /> command triggered the completion.
    /// </summary>
    Tab = ScintillaConstants.SC_AC_TAB,

    /// <summary>
    /// A new line or <see cref="ScintillaNET.Command.NewLine" /> command triggered the completion.
    /// </summary>
    NewLine = ScintillaConstants.SC_AC_NEWLINE,

    /// <summary>
    /// The <see cref="Scintilla.AutoCSelect" /> method triggered the completion.
    /// </summary>
    Command = ScintillaConstants.SC_AC_COMMAND
}