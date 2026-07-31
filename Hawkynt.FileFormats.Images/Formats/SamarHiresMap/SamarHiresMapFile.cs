using System;
using FileFormat.Core;

namespace FileFormat.SamarHiresMap;

/// <summary>
/// In-memory representation of a SAMAR Hi-res Interlace with Map of Colours picture (.shc).
/// </summary>
/// <remarks>
/// Two Atari 8-bit high-resolution screens shown on alternate fields, each with a colour register
/// that changes as the beam crosses the picture. The register cannot be reloaded at arbitrary
/// points — the processor has only so many cycles between the bytes ANTIC is fetching — so the
/// positions where it changes are fixed, six per line, and the two fields change at different ones
/// so that between them the picture has twelve colour zones rather than six.
/// <para/>
/// The extension is shared with the MSX2+ YJK format, which is unrelated.
/// </remarks>
public readonly record struct SamarHiresMapFile
  : IImageFormatReader<SamarHiresMapFile>, IImageToRawImage<SamarHiresMapFile> {

  /// <summary>Pixels across.</summary>
  public const int Width = 320;

  /// <summary>Rows.</summary>
  public const int Height = 192;

  /// <summary>Where the second field's bitmap starts.</summary>
  public const int SecondBitmapOffset = Width * Height / 8;

  /// <summary>Where the first field's colour map starts.</summary>
  public const int FirstColorOffset = SecondBitmapOffset * 2;

  /// <summary>Where the second field's colour map starts.</summary>
  public const int SecondColorOffset = 16640;

  /// <summary>Total file size.</summary>
  public const int FileSize = 17920;

  /// <summary>Where along a line the first field reloads its colour register.</summary>
  public static ReadOnlySpan<int> FirstFieldChanges => [94, 166, 214, 262, 306];

  /// <summary>Where along a line the second field reloads its colour register.</summary>
  public static ReadOnlySpan<int> SecondFieldChanges => [46, 142, 190, 238, 286];

  static string IImageFormatMetadata<SamarHiresMapFile>.PrimaryExtension => ".shc";
  static string[] IImageFormatMetadata<SamarHiresMapFile>.FileExtensions => [".shc"];
  static SamarHiresMapFile IImageFormatReader<SamarHiresMapFile>.FromSpan(ReadOnlySpan<byte> data)
    => SamarHiresMapReader.FromSpan(data);
  static VideoMode[] IImageFormatMetadata<SamarHiresMapFile>.VideoModes => [
    new("Atari 8-bit", [(Width, Height)], [256])
  ];

  /// <summary>The whole file.</summary>
  public byte[] Data { get; init; }

  public static RawImage ToRawImage(SamarHiresMapFile file) {
    var data = file.Data ?? [];

    var first = _DecodeField(data, 0, FirstColorOffset, FirstFieldChanges);
    var second = _DecodeField(data, SecondBitmapOffset, SecondColorOffset, SecondFieldChanges);

    return new() {
      Width = Width,
      Height = Height,
      Format = PixelFormat.Rgb24,
      PixelData = FrameBlend.Average(Atari8BitGraphics.ApplyPalette(first), Atari8BitGraphics.ApplyPalette(second)),
    };
  }

  /// <summary>Draws one field into GTIA colour bytes.</summary>
  private static byte[] _DecodeField(ReadOnlySpan<byte> data, int bitmap, int color, ReadOnlySpan<int> changes) {
    var frame = new byte[Width * Height];

    for (var y = 0; y < Height; ++y) {
      for (var x = 0; x < Width; ++x) {
        foreach (var change in changes) {
          if (x == change)
            ++color;
        }

        var at = y * Width + x;
        var lit = (_At(data, bitmap + (at >> 3)) >> (~x & 7) & 1) != 0;

        // A lit pixel keeps the register's hue but loses its luminance; an unlit one keeps both,
        // less the bit the hardware ignores.
        frame[at] = (byte)(_At(data, color) & (lit ? 240 : 254));
      }

      // The last zone of a line runs into the first of the next, so the register steps once more.
      ++color;
    }

    return frame;
  }

  private static byte _At(ReadOnlySpan<byte> data, int offset)
    => offset >= 0 && offset < data.Length ? data[offset] : (byte)0;
}
