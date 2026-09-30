using System;
using Hawkynt.NativeForms;

namespace Crush.Viewer;

/// <summary>The window's keyboard bindings, answering wherever the focus happens to sit.</summary>
/// <remarks>
/// <para>
/// Neither <see cref="Form"/> nor <see cref="Control"/> publishes a key event, and a
/// <see cref="RibbonItem"/> carries no shortcut of its own, so there is no property a window-wide
/// binding can be hung on. What the toolkit does have is a single form-wide key chain: every drawn
/// control and every text box offers its keys to the form before handling them, and that offer
/// dispatches registered <see cref="ToolStripMenuItem.ShortcutKeys"/> to every
/// <see cref="MenuStrip"/> anywhere in the control tree.
/// </para>
/// <para>
/// A strip that is never shown is therefore exactly one thing: a table of chords that covers the
/// whole window — the thumbnail list, the canvas, the ribbon and the information box alike — instead
/// of only whichever control has the focus. Handing the same job to a key handler on each focusable
/// child would have to be repeated for every control added later, and would miss the ones the
/// toolkit builds itself.
/// </para>
/// <para>
/// The chain has one documented gap: a key the focused control claims through
/// <c>Control.IsInputKey</c> goes straight to that control. In this window that is Enter alone,
/// claimed by the thumbnail list to activate an item, which is not a chord bound here.
/// </para>
/// </remarks>
internal sealed class ShortcutTable {

  private readonly MenuStrip _strip = new() { Visible = false };

  /// <summary>The carrier to add to the form's controls. It occupies no space and paints nothing.</summary>
  internal MenuStrip Carrier => this._strip;

  /// <summary>Binds every one of <paramref name="chords"/> to <paramref name="action"/>.</summary>
  /// <remarks>
  /// One item per chord, because dispatch matches a single item's chord exactly: a key that means
  /// the same thing with and without Shift, or on the number row and the keypad, is several chords.
  /// The caption never reaches a screen, so it is written for whoever reads this table next.
  /// </remarks>
  internal void Bind(string name, Action action, params Keys[] chords) {
    foreach (var chord in chords) {
      var item = new ToolStripMenuItem(name) { ShortcutKeys = chord };
      item.Click += (_, _) => action();
      this._strip.Items.Add(item);
    }
  }
}
