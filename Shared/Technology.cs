﻿using Scintilla.NET.Abstractions;

namespace ScintillaNET;

/// <summary>
/// The rendering technology used in a <see cref="Scintilla" /> control.
/// </summary>
public enum Technology
{
    /// <summary>
    /// Renders text using GDI. This is the default.
    /// </summary>
    Default = ScintillaConstants.SC_TECHNOLOGY_DEFAULT,

    /// <summary>
    /// Renders text using Direct2D/DirectWrite. Since Direct2D buffers drawing,
    /// Scintilla's buffering can be turned off with <see cref="Scintilla.BufferedDraw" />.
    /// </summary>
    DirectWrite = ScintillaConstants.SC_TECHNOLOGY_DIRECTWRITE
}