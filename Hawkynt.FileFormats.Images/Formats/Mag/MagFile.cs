using System;
using FileFormat.Core;

namespace FileFormat.Mag;

/// <summary>In-memory representation of a MAKIchan Graphics image.</summary>
/// <remarks>
/// The layout: eight signature bytes, a comment ending at the first <c>0x1A</c>, then a 32-byte
/// header whose offsets are all relative to its own first byte. In it: a screen mode at 3, the drawn
/// region as four 16-bit values at 4, 6, 8 and 10, and then five long words giving where the two flag
/// streams and the pixels are and how long the last three are. The palette follows the header — three
/// bytes an entry, green first, then red, then blue.
/// <para/>
/// The compression works on units of two bytes. Flag stream A is a bitstream, one bit per <b>four</b>
/// bytes of picture, and where a bit is set one byte is taken from stream B and exclusive-ored into a
/// running row of flags. Each flag byte then covers two units, high nibble first. A nibble of nought
/// means the next two bytes come from the pixel stream; anything else names a place to copy two bytes
/// from, so many units left and so many rows up.
/// <para/>
/// This was previously recorded as unsolved — nine of the sixteen codes settled, the other six
/// apparently not copies at all, and 87% of pixels right. That conclusion came from feeding the flag
/// stream at the wrong granularity: one bit per unit rather than per four bytes, which puts every flag
/// against the wrong unit and makes six of the codes look like they mean something other than copying.
/// At the right granularity all sixteen are copies, all three samples consume their flag and pixel
/// streams to the exact byte, and every pixel falls where RECOIL puts it.
/// <para/>
/// A caution worth keeping from that note: comparing decoded palette <em>indices</em> against another
/// tool's rendering gives nonsense here, because entries 0 and 1 are both black in some files and the
/// picture cannot tell them apart. Compare colours.
/// </remarks>
public readonly record struct MagFile
  : IImageFormatReader<MagFile>, IImageToRawImage<MagFile> {

  internal const int HeaderSize = 32;

  static string IImageFormatMetadata<MagFile>.PrimaryExtension => ".mag";
  static string[] IImageFormatMetadata<MagFile>.FileExtensions => [".mag", ".mki"];
  static MagFile IImageFormatReader<MagFile>.FromSpan(ReadOnlySpan<byte> data) => MagReader.FromSpan(data);
  static VideoMode[] IImageFormatMetadata<MagFile>.VideoModes => [
    new("Default", [(IntegerRange.Any, IntegerRange.Any)], [16, 256])
  ];

  static bool? IImageFormatMetadata<MagFile>.MatchesSignature(ReadOnlySpan<byte> header)
    => header.Length >= Magic.Length && header[..Magic.Length].SequenceEqual(Magic) ? true : null;

  /// <summary>The eight bytes a file opens with.</summary>
  internal static ReadOnlySpan<byte> Magic => "MAKI02  "u8;

  /// <summary>
  /// Where a copy comes from, in units of two bytes to the left and rows above.
  /// </summary>
  /// <remarks>
  /// All sixteen entries are used by the samples and all sixteen are copies.
  /// </remarks>
  internal static ReadOnlySpan<int> CopyColumns => [0, -1, -2, -4, 0, -1, 0, -1, -2, 0, -1, -2, 0, -1, -2, 0];

  /// <summary>How many rows up a copy comes from, alongside <see cref="CopyColumns"/>.</summary>
  internal static ReadOnlySpan<int> CopyRows => [0, 0, 0, 0, -1, -1, -2, -2, -2, -4, -4, -4, -8, -8, -8, -16];

  /// <summary>Displayed width, which is twice the stored one in 256 colours.</summary>
  public int Width { get; init; }

  /// <summary>Displayed height, which is twice the stored one in the 200-line modes.</summary>
  public int Height { get; init; }

  /// <summary>One index per displayed pixel.</summary>
  public byte[] PixelData { get; init; }

  /// <summary>The palette the file states, as RGB triplets.</summary>
  public byte[] Palette { get; init; }

  /// <summary>Colours the palette holds: sixteen or 256.</summary>
  public int PaletteCount { get; init; }

  public static RawImage ToRawImage(MagFile file) => new() {
    Width = file.Width,
    Height = file.Height,
    Format = PixelFormat.Indexed8,
    PixelData = file.PixelData[..],
    Palette = file.Palette[..],
    PaletteCount = file.PaletteCount,
  };
}
