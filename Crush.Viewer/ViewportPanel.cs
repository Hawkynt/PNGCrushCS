using System;
using Hawkynt.NativeForms;

namespace Crush.Viewer;

/// <summary>How the next picture is scaled when it arrives.</summary>
internal enum ZoomMode {

  /// <summary>Scaled down until the whole picture is inside the canvas.</summary>
  Fit,

  /// <summary>Shown one picture pixel to one screen pixel.</summary>
  Actual,

  /// <summary>Kept at whatever scale the user zoomed to.</summary>
  Free,
}

/// <summary>The image canvas: the toolkit's zoom panel with a picture viewer's mouse wheel on it.</summary>
/// <remarks>
/// The keys belong to <see cref="ShortcutTable"/> rather than here, so that they answer with the
/// thumbnail list focused too. What is left is the wheel, which the panel below turns into a zoom on
/// its own: a picture viewer's wheel walks the folder, and zooming is what Control with the wheel is
/// for.
/// </remarks>
internal sealed class ViewportPanel : ZoomPanel {

  /// <summary>Raised with -1 or 1 when the user asks for the neighbouring picture.</summary>
  internal event EventHandler<int>? WalkRequested;

  protected override void OnMouseWheel(MouseEventArgs e) {
    if ((e.Modifiers & KeyModifiers.Control) != 0) {
      base.OnMouseWheel(e);
      return;
    }

    if (e.Delta != 0)
      this.WalkRequested?.Invoke(this, e.Delta > 0 ? -1 : 1);
  }
}
