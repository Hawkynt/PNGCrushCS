using System;
using FileFormat.Core;

namespace FileFormat.ProfiGrf;

/// <summary>In-memory representation of a Profi picture (.grf).</summary>
/// <remarks>
/// The Profi was a Spectrum clone with a wider screen and a real palette, and this format shows
/// both: 512 pixels across, and sixteen colours chosen from a byte each rather than the Spectrum's
/// fixed eight. The attribute is still one per eight pixels, so the cell constraint the Spectrum is
/// known for is not gone — but ink and paper get a brightness bit each here rather than sharing
/// one, which is what makes all sixteen palette entries reachable in a single cell.
/// <para/>
/// Attributes are stored interleaved with the bitmap, a byte of pixels then its attribute, rather
/// than in a plane of their own.
/// </remarks>
public readonly record struct ProfiGrfFile
  : IImageFormatReader<ProfiGrfFile>, IImageToRawImage<ProfiGrfFile> {

  /// <summary>Pixels across.</summary>
  public const int Width = 512;

  /// <summary>Rows the display shows: each stored row is drawn twice.</summary>
  public const int Height = 480;

  /// <summary>Rows the file stores.</summary>
  public const int StoredRows = Height / 2;

  /// <summary>Bytes one stored row occupies: a byte of pixels and an attribute for every eight.</summary>
  public const int Stride = Width / 8 * 2;

  /// <summary>Colours the palette holds.</summary>
  public const int ColorCount = 16;

  /// <summary>Offset of the palette.</summary>
  public const int PaletteOffset = 10;

  /// <summary>Offset of the bitmap.</summary>
  public const int BitmapOffset = 128;

  /// <summary>Total file size.</summary>
  public const int FileSize = BitmapOffset + Stride * StoredRows;

  /// <summary>The ten bytes every file starts with.</summary>
  public static ReadOnlySpan<byte> Signature => [0, 2, 240, 0, 4, 0, 128, 0, 1, 19];

  static string IImageFormatMetadata<ProfiGrfFile>.PrimaryExtension => ".grf";
  static string[] IImageFormatMetadata<ProfiGrfFile>.FileExtensions => [".grf"];
  static ProfiGrfFile IImageFormatReader<ProfiGrfFile>.FromSpan(ReadOnlySpan<byte> data)
    => ProfiGrfReader.FromSpan(data);
  static VideoMode[] IImageFormatMetadata<ProfiGrfFile>.VideoModes => [
    new("Profi", [(Width, Height)], [ColorCount])
  ];

  /// <summary>The whole file.</summary>
  public byte[] Data { get; init; }

  public static RawImage ToRawImage(ProfiGrfFile file) {
    var data = file.Data ?? [];

    // Green in the top three bits, red in the next three, blue in the bottom two.
    var palette = new byte[ColorCount * 3];
    for (var i = 0; i < ColorCount; ++i) {
      var c = PaletteOffset + i < data.Length ? data[PaletteOffset + i] : 0;
      palette[i * 3] = ChannelScaling.Expand3((c >> 2) & 7);
      palette[i * 3 + 1] = ChannelScaling.Expand3(c >> 5);
      palette[i * 3 + 2] = ChannelScaling.Expand2(c & 3);
    }

    var pixels = new byte[Width * Height];
    for (var y = 0; y < Height; ++y)
    for (var x = 0; x < Width; ++x) {
      var at = BitmapOffset + (y >> 1) * Stride + (x >> 3) * 2;
      var ink = at < data.Length && ((data[at] >> (~x & 7)) & 1) != 0;
      var attribute = at + 1 < data.Length ? data[at + 1] : 0;

      // Ink takes the low nibble with bit 6 as its brightness, paper the high one with bit 7.
      pixels[y * Width + x] = (byte)(ink
        ? ((attribute >> 3) & 8) | (attribute & 7)
        : ((attribute >> 4) & 8) | ((attribute >> 3) & 7));
    }

    return new() {
      Width = Width,
      Height = Height,
      Format = PixelFormat.Indexed8,
      PixelData = pixels,
      Palette = palette,
      PaletteCount = ColorCount,
    };
  }
}
