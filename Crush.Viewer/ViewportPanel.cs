using System;
using Hawkynt.NativeForms;

namespace Crush.Viewer;

/// <summary>The image canvas, with the keys a picture viewer is expected to answer to.</summary>
/// <remarks>
/// The toolkit publishes key events on text boxes and on the protected surface of a drawn control,
/// but a form has no public key event of its own, so a shortcut has to belong to the control that
/// has the focus. Putting folder and page walking on the canvas is the closest thing to a global
/// binding that the toolkit allows.
/// </remarks>
internal sealed class ViewportPanel : ZoomPanel {

  /// <summary>Raised with -1 or 1 when the user asks for the neighbouring picture.</summary>
  internal event EventHandler<int>? WalkRequested;

  /// <summary>Raised with -1 or 1 when the user asks for the neighbouring page.</summary>
  internal event EventHandler<int>? PageRequested;

  protected override bool IsInputKey(Keys keyData)
    => keyData is Keys.Left or Keys.Right or Keys.PageUp or Keys.PageDown or Keys.Home or Keys.End
      || base.IsInputKey(keyData);

  protected override void OnKeyDown(KeyEventArgs e) {
    switch (e.KeyCode) {
      case Keys.Left: this.WalkRequested?.Invoke(this, -1); e.Handled = true; return;
      case Keys.Right: this.WalkRequested?.Invoke(this, 1); e.Handled = true; return;
      case Keys.PageUp: this.PageRequested?.Invoke(this, -1); e.Handled = true; return;
      case Keys.PageDown: this.PageRequested?.Invoke(this, 1); e.Handled = true; return;
    }

    base.OnKeyDown(e);
  }
}
