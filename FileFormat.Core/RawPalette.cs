using System;

namespace FileFormat.Core;

/// <summary>
/// One unassociated RGBA palette entry in canonical 16-bit full-range form. Eight-bit palette values
/// are represented exactly by multiplying each component by 257, so converting 8-bit palettes into
/// this representation is lossless and exactly reversible.
/// </summary>
public readonly record struct RawPaletteEntry(
  ushort Red,
  ushort Green,
  ushort Blue,
  ushort Alpha = ushort.MaxValue
);

/// <summary>
/// Format-independent palette representation. Palette entries are kept at 16-bit RGBA precision so
/// formats with 16-bit colour maps do not have to collapse through the legacy RGB8 palette arrays.
/// Quantization and approximate sample-depth reduction are deliberately not palette responsibilities.
/// </summary>
public sealed class RawPalette {
  private const int _EightToSixteenScale = ushort.MaxValue / byte.MaxValue;
  private readonly RawPaletteEntry[] _entries;

  public RawPalette(ReadOnlySpan<RawPaletteEntry> entries) {
    this._entries = entries.ToArray();
  }

  /// <summary>Number of entries in this palette.</summary>
  public int Count => this._entries.Length;

  /// <summary>Read-only span over the canonical palette entries.</summary>
  public ReadOnlySpan<RawPaletteEntry> Entries => this._entries;

  /// <summary>Gets one palette entry by index.</summary>
  public RawPaletteEntry this[int index] => this._entries[index];

  /// <summary>Whether at least one palette entry is not fully opaque.</summary>
  public bool HasAlpha {
    get {
      foreach (var entry in this._entries)
        if (entry.Alpha != ushort.MaxValue)
          return true;

      return false;
    }
  }

  /// <summary>
  /// Converts legacy RGB triplets plus an optional 8-bit alpha table into the canonical RGBA64
  /// representation without loss. Only the first <paramref name="count"/> entries are consumed.
  /// </summary>
  public static RawPalette FromRgb24(
    ReadOnlySpan<byte> rgb,
    int count,
    ReadOnlySpan<byte> alpha = default
  ) {
    ArgumentOutOfRangeException.ThrowIfNegative(count);

    var requiredRgbBytes = checked(count * 3);
    if (rgb.Length < requiredRgbBytes)
      throw new ArgumentException(
        $"The RGB palette has {rgb.Length} byte(s), but {count} entries require at least {requiredRgbBytes}.",
        nameof(rgb)
      );

    if (!alpha.IsEmpty && alpha.Length < count)
      throw new ArgumentException(
        $"The alpha table has {alpha.Length} byte(s), but {count} entries require at least {count}.",
        nameof(alpha)
      );

    var entries = new RawPaletteEntry[count];
    for (var i = 0; i < count; ++i) {
      var rgbOffset = i * 3;
      entries[i] = new(
        _Expand8(rgb[rgbOffset]),
        _Expand8(rgb[rgbOffset + 1]),
        _Expand8(rgb[rgbOffset + 2]),
        alpha.IsEmpty ? ushort.MaxValue : _Expand8(alpha[i])
      );
    }

    return new(entries);
  }

  /// <summary>
  /// Converts the legacy palette attached to an indexed <see cref="RawImage"/>. Returns <see langword="null"/>
  /// when no palette is attached. The conversion itself is exact; it does not quantize or reorder entries.
  /// </summary>
  public static RawPalette? FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);

    if (image.Palette is null || image.PaletteCount == 0)
      return null;

    return FromRgb24(image.Palette, image.PaletteCount, image.AlphaTable);
  }

  /// <summary>
  /// Tries to express this palette exactly in the legacy RGB8 + optional alpha-table representation.
  /// The operation fails rather than reducing 16-bit values approximately.
  /// </summary>
  public bool TryToRgb24Exact(out byte[] rgb, out byte[]? alpha) {
    foreach (var entry in this._entries)
      if (!_IsExactlyEightBit(entry.Red)
          || !_IsExactlyEightBit(entry.Green)
          || !_IsExactlyEightBit(entry.Blue)
          || !_IsExactlyEightBit(entry.Alpha)) {
        rgb = [];
        alpha = null;
        return false;
      }

    rgb = new byte[checked(this.Count * 3)];
    alpha = this.HasAlpha ? new byte[this.Count] : null;

    for (var i = 0; i < this.Count; ++i) {
      var entry = this._entries[i];
      var rgbOffset = i * 3;
      rgb[rgbOffset] = _Collapse8(entry.Red);
      rgb[rgbOffset + 1] = _Collapse8(entry.Green);
      rgb[rgbOffset + 2] = _Collapse8(entry.Blue);
      if (alpha is not null)
        alpha[i] = _Collapse8(entry.Alpha);
    }

    return true;
  }

  /// <summary>
  /// Expresses this palette in the legacy RGB8 + optional alpha-table representation, throwing when
  /// doing so would require precision reduction.
  /// </summary>
  public (byte[] Rgb, byte[]? Alpha) ToRgb24Exact() {
    if (!this.TryToRgb24Exact(out var rgb, out var alpha))
      throw new InvalidOperationException(
        "The palette contains 16-bit component values that cannot be represented exactly as full-range 8-bit samples."
      );

    return (rgb, alpha);
  }

  private static ushort _Expand8(byte value) => (ushort)(value * _EightToSixteenScale);
  private static bool _IsExactlyEightBit(ushort value) => value % _EightToSixteenScale == 0;
  private static byte _Collapse8(ushort value) => (byte)(value / _EightToSixteenScale);
}
