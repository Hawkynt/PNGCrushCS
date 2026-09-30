using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Hawkynt.FileFormats.Images;
using Hawkynt.NativeForms;

namespace Crush.Viewer;

/// <summary>The ribbon's and the status bar's artwork, decoded out of the assembly on demand.</summary>
/// <remarks>
/// <para>
/// Two lists, because the ribbon draws a large item's icon at 32 pixels and everything else — a small
/// item, a quick-access button, a status panel — at 16, and an icon looks its best at the size it was
/// drawn for. The files are their sets' own, at their own sizes — Silk, its companion, Fugue and
/// FatCow at 16, Oxygen and FatCow at 32 — and none is scaled up. Where a 32-pixel icon is wanted in
/// a 16-pixel slot it is box-filtered down, which is what <see cref="ImageBridge.ToThumbnailArgb"/>
/// already does for a picture.
/// </para>
/// <para>
/// Decoding goes through the repository's own PNG reader rather than the toolkit's, which documents
/// itself as handling a subset. The whole set is a few dozen small files, so it is read once at start-up
/// rather than on demand: the cost is a few milliseconds and the alternative is a ribbon that fills
/// in as the user notices it.
/// </para>
/// </remarks>
internal sealed class IconSet : IDisposable {

  /// <summary>What the ribbon draws a large item's icon at.</summary>
  internal const int LARGE_EDGE = 32;

  /// <summary>What the ribbon, the quick-access strip and the status bar draw everything else at.</summary>
  internal const int SMALL_EDGE = 16;

  private readonly Dictionary<string, string> _resources = [];
  private readonly ImageList _small = new(SMALL_EDGE);
  private readonly ImageList _large = new(LARGE_EDGE);

  internal IconSet() {
    var assembly = typeof(IconSet).Assembly;
    foreach (var resource in assembly.GetManifestResourceNames()) {
      if (!resource.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
        continue;

      // "Icons.Silk.disk.png" is keyed as "disk": which set an icon came from is a licensing fact,
      // not something a caller asking for a disk should have to know.
      var stem = resource[..^4];
      this._resources[stem[(stem.LastIndexOf('.') + 1)..]] = resource;
    }
  }

  /// <summary>Gives an item the icon named <paramref name="name"/>, at the size it will be drawn at.</summary>
  /// <remarks>
  /// A name with no artwork behind it leaves the item as it was — text alone. That is the answer for
  /// a command no set has an honest glyph for, and it means a typo costs a missing icon rather than
  /// a window that will not open.
  /// </remarks>
  internal void Apply(ToolStripItem item, string name, RibbonItemSize size)
    => this.Apply(item, name, size == RibbonItemSize.Large ? LARGE_EDGE : SMALL_EDGE);

  /// <inheritdoc cref="Apply(ToolStripItem,string,RibbonItemSize)"/>
  internal void Apply(ToolStripItem item, string name, int edge = SMALL_EDGE) {
    var list = edge == LARGE_EDGE ? this._large : this._small;
    if (!this._Ensure(list, name))
      return;

    item.ImageList = list;
    item.ImageKey = name;
  }

  private bool _Ensure(ImageList list, string name) {
    if (list.ContainsKey(name))
      return true;

    if (!this._resources.TryGetValue(name, out var resource))
      return false;

    try {
      using var stream = typeof(IconSet).Assembly.GetManifestResourceStream(resource);
      if (stream == null)
        return false;

      using var memory = new MemoryStream();
      stream.CopyTo(memory);
      var raw = FormatRegistry.Read(memory.ToArray());
      if (raw == null)
        return false;

      list.Add(name, ImageBridge.ToThumbnailArgb(raw, list.ImageSize.Width));
      return true;
    } catch (Exception) {
      // An icon that will not decode is an icon that is not shown. Nothing else about the window
      // depends on it, and a viewer that refuses to open because of a 16-pixel picture is worse
      // than one that opens with a text button.
      return false;
    }
  }

  public void Dispose() {
    this._small.Dispose();
    this._large.Dispose();
  }
}
