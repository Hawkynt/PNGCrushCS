using System;

namespace FileFormat.Core;

/// <summary>
/// A named, immutable palette declared by a file format that does not allow arbitrary palette
/// selection (e.g. CGA, DOOM, the NES hardware palette).
/// </summary>
/// <remarks>
/// When a format declares one or more <see cref="FixedPalette"/>s via
/// <see cref="IImageFormatMetadata{TSelf}.FixedPalettes"/>, the colour-reduction UI hides the
/// quantizer selection and offers the user a choice between these palettes instead. The
/// ditherer remains user-selectable.
/// <para/>
/// Colours are declared as hex literals — e.g. <c>0x00FF00</c> for green. Values without alpha
/// bits set (≤ <c>0xFFFFFF</c>) are treated as fully opaque (<c>0xFF</c> alpha).
/// </remarks>
/// <example>
/// <code>
/// static FixedPalette[] IImageFormatMetadata&lt;CgaFile&gt;.FixedPalettes => [
///   new FixedPalette("CGA palette 0 (high intensity)", 0x000000, 0x55FF55, 0xFF5555, 0xFFFF55),
///   new FixedPalette("CGA palette 1 (high intensity)", 0x000000, 0x55FFFF, 0xFF55FF, 0xFFFFFF),
/// ];
/// </code>
/// </example>
public sealed record FixedPalette {

  /// <summary>Display name shown in the UI (e.g. "CGA Palette 1 (high intensity)", "DOOM").</summary>
  public string Name { get; }

  /// <summary>The palette colours as <c>0xAARRGGBB</c> hex values, in palette index order.</summary>
  public uint[] HexColors { get; }

  /// <summary>The number of entries in this palette.</summary>
  public int Count => this.HexColors.Length;

  /// <param name="name">Display name shown in the UI.</param>
  /// <param name="hexColors">Hex colour values: <c>0xRRGGBB</c> for opaque colours, <c>0xAARRGGBB</c> when an explicit alpha is needed. Values ≤ <c>0xFFFFFF</c> are auto-promoted to <c>0xFFRRGGBB</c>.</param>
  public FixedPalette(string name, params uint[] hexColors) {
    if (string.IsNullOrEmpty(name)) throw new ArgumentException("Palette name is required.", nameof(name));
    if (hexColors == null) throw new ArgumentNullException(nameof(hexColors));
    if (hexColors.Length == 0) throw new ArgumentException("Palette must contain at least one colour.", nameof(hexColors));

    this.Name = name;
    this.HexColors = new uint[hexColors.Length];
    for (var i = 0; i < hexColors.Length; ++i) {
      var c = hexColors[i];
      this.HexColors[i] = (c & 0xFF000000u) == 0 ? c | 0xFF000000u : c;
    }
  }

  /// <summary>Returns the colours as packed RGB triplets <c>[R0, G0, B0, R1, G1, B1, ...]</c>. Alpha is dropped.</summary>
  public byte[] ToPackedRgb() {
    var result = new byte[this.HexColors.Length * 3];
    for (var i = 0; i < this.HexColors.Length; ++i) {
      var c = this.HexColors[i];
      result[i * 3 + 0] = (byte)((c >> 16) & 0xFF);
      result[i * 3 + 1] = (byte)((c >> 8) & 0xFF);
      result[i * 3 + 2] = (byte)(c & 0xFF);
    }
    return result;
  }

  /// <summary>Returns the colours as packed RGBA quadruplets <c>[R0, G0, B0, A0, R1, G1, B1, A1, ...]</c>.</summary>
  public byte[] ToPackedRgba() {
    var result = new byte[this.HexColors.Length * 4];
    for (var i = 0; i < this.HexColors.Length; ++i) {
      var c = this.HexColors[i];
      result[i * 4 + 0] = (byte)((c >> 16) & 0xFF);
      result[i * 4 + 1] = (byte)((c >> 8) & 0xFF);
      result[i * 4 + 2] = (byte)(c & 0xFF);
      result[i * 4 + 3] = (byte)((c >> 24) & 0xFF);
    }
    return result;
  }
}
