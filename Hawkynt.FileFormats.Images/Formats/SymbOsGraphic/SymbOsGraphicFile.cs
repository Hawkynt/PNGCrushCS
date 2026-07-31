using System;
using System.Collections.Generic;
using FileFormat.Core;

namespace FileFormat.SymbOsGraphic;

/// <summary>In-memory representation of a SymbOS graphic (.sgx).</summary>
/// <remarks>
/// A picture assembled from tiles rather than stored as one bitmap. SymbOS ran on eight-bit
/// machines whose memory came in banks, so a graphic larger than a bank could not be one object —
/// it is a row of chunks, a marker, another row, and so on, each chunk small enough to load and
/// draw on its own.
/// <para/>
/// Chunks come in two kinds and a picture may mix them: a short header for four colours and a
/// longer one for sixteen. The four-colour form packs its pixels the way the Amstrad's hardware
/// does, with a byte holding the low bits of four pixels in one nibble and their high bits in the
/// other, rather than two bits of each pixel side by side.
/// </remarks>
public readonly record struct SymbOsGraphicFile
  : IImageFormatReader<SymbOsGraphicFile>, IImageToRawImage<SymbOsGraphicFile> {

  /// <summary>The chunk header that ends a row of chunks.</summary>
  public const byte RowMarker = 255;

  /// <summary>The chunk header that introduces the sixteen-colour form.</summary>
  public const byte WideHeader = 64;

  /// <summary>Widest a four-colour chunk's stride may be before the wide header is needed.</summary>
  public const int MaxNarrowStride = 63;

  /// <summary>The four colours a narrow chunk draws from.</summary>
  public static ReadOnlySpan<byte> FourColorPalette => [
    0xFF, 0xFF, 0xFF, 0xAA, 0xAA, 0xAA, 0x00, 0x00, 0x00, 0x55, 0x55, 0x55,
  ];

  /// <summary>The sixteen colours a wide chunk draws from.</summary>
  public static ReadOnlySpan<byte> SixteenColorPalette => [
    0xFF, 0xFF, 0x80, 0x00, 0x00, 0x00, 0xFF, 0x80, 0x00, 0x80, 0x00, 0x00,
    0x00, 0xFF, 0xFF, 0x00, 0x00, 0x80, 0x80, 0x80, 0xFF, 0x00, 0x00, 0xFF,
    0xFF, 0xFF, 0xFF, 0x00, 0x80, 0x00, 0x00, 0xFF, 0x00, 0xFF, 0x00, 0xFF,
    0xFF, 0xFF, 0x00, 0x80, 0x80, 0x80, 0xFF, 0x80, 0x80, 0xFF, 0x00, 0x00,
  ];

  static string IImageFormatMetadata<SymbOsGraphicFile>.PrimaryExtension => ".sgx";
  static string[] IImageFormatMetadata<SymbOsGraphicFile>.FileExtensions => [".sgx"];
  static SymbOsGraphicFile IImageFormatReader<SymbOsGraphicFile>.FromSpan(ReadOnlySpan<byte> data)
    => SymbOsGraphicReader.FromSpan(data);
  static VideoMode[] IImageFormatMetadata<SymbOsGraphicFile>.VideoModes => [
    new("SymbOS", [(IntegerRange.Any, IntegerRange.Any)], [16])
  ];

  /// <summary>The whole file.</summary>
  public byte[] Data { get; init; }

  /// <summary>Pixels across.</summary>
  public int Width { get; init; }

  /// <summary>Rows.</summary>
  public int Height { get; init; }

  /// <summary>The chunks in the order they are drawn.</summary>
  public IReadOnlyList<SymbOsChunk> Chunks { get; init; }

  public static RawImage ToRawImage(SymbOsGraphicFile file) {
    var data = file.Data ?? [];
    var rgb = new byte[file.Width * file.Height * 3];

    foreach (var chunk in file.Chunks ?? []) {
      var palette = chunk.IsWide ? SixteenColorPalette : FourColorPalette;

      for (var y = 0; y < chunk.Height; ++y)
      for (var x = 0; x < chunk.Width; ++x) {
        var row = chunk.DataOffset + y * chunk.Stride;
        int index;

        if (chunk.IsWide)
          index = MsxGraphics.GetNibble(data, row, x);
        else {
          var at = row + (x >> 2);
          // One nibble holds four pixels' low bits and the other their high bits.
          var b = at < data.Length ? data[at] >> (~x & 3) : 0;
          index = ((b >> 3) & 2) | (b & 1);
        }

        var entry = index * 3;
        var target = ((chunk.Top + y) * file.Width + chunk.Left + x) * 3;
        rgb[target] = palette[entry];
        rgb[target + 1] = palette[entry + 1];
        rgb[target + 2] = palette[entry + 2];
      }
    }

    return new() { Width = file.Width, Height = file.Height, Format = PixelFormat.Rgb24, PixelData = rgb };
  }
}
