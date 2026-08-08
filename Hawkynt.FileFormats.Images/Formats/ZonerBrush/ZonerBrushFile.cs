using System;
using FileFormat.Core;

namespace FileFormat.ZonerBrush;

/// <summary>In-memory representation of the preview inside a Zoner brush (.zbr).</summary>
/// <remarks>
/// The file itself is a drawing rather than a picture — the three samples here are 8811, 10525 and
/// 42864 bytes and all three show the same 100 by 100 — so what is read is the preview bitmap the
/// file carries so a chooser has something to draw.
/// <para/>
/// That preview is a fixed 100 by 100 at four bits a pixel: sixteen palette entries of four bytes
/// each at 104, the picture at 168, 52 bytes to a row and the rows from the bottom up. 104 plus
/// sixteen fours is 168, which is what makes the two offsets one fact rather than two.
/// <para/>
/// The drawing behind it is not read. A vector file is not a picture, and the preview is what the
/// tool draws.
/// </remarks>
public readonly record struct ZonerBrushFile
  : IImageFormatReader<ZonerBrushFile>, IImageToRawImage<ZonerBrushFile>, IImageFormatWriter<ZonerBrushFile> {

  /// <summary>The preview is always this size.</summary>
  public const int Width = 100, Height = 100;

  /// <summary>Bytes a row takes: two pixels a byte, padded to four.</summary>
  public const int BytesPerRow = 52;

  public const int PaletteCount = 16;

  /// <summary>Four bytes an entry, the fourth unused.</summary>
  public const int PaletteEntrySize = 4;

  public const int PaletteOffset = 104;

  public const int PixelOffset = PaletteOffset + PaletteCount * PaletteEntrySize;

  /// <summary>The least a file can be: the header, the palette and the preview.</summary>
  public const int MinimumFileSize = PixelOffset + BytesPerRow * Height;

  static string IImageFormatMetadata<ZonerBrushFile>.PrimaryExtension => ".zbr";
  static string[] IImageFormatMetadata<ZonerBrushFile>.FileExtensions => [".zbr"];
  static ZonerBrushFile IImageFormatReader<ZonerBrushFile>.FromSpan(ReadOnlySpan<byte> data) => ZonerBrushReader.FromSpan(data);
  static byte[] IImageFormatWriter<ZonerBrushFile>.ToBytes(ZonerBrushFile file) => ZonerBrushWriter.ToBytes(file);
  static VideoMode[] IImageFormatMetadata<ZonerBrushFile>.VideoModes => [
    new("Preview", [(Width, Height)], [PaletteCount])
  ];

  /// <summary>Everything before the palette, kept so writing one back preserves the drawing's head.</summary>
  public byte[] Header { get; init; }

  /// <summary>Sixteen entries of four bytes, blue first.</summary>
  public byte[] Palette { get; init; }

  /// <summary>The preview, two pixels a byte.</summary>
  public byte[] PixelData { get; init; }

  public static RawImage ToRawImage(ZonerBrushFile file) {
    var data = file.PixelData ?? [];
    var stored = file.Palette ?? [];
    var palette = new byte[PaletteCount * 3];
    for (var i = 0; i < PaletteCount && i * PaletteEntrySize + 2 < stored.Length; ++i) {
      palette[i * 3] = stored[i * PaletteEntrySize];
      palette[i * 3 + 1] = stored[i * PaletteEntrySize + 1];
      palette[i * 3 + 2] = stored[i * PaletteEntrySize + 2];
    }

    var pixels = new byte[Width * Height];
    for (var y = 0; y < Height; ++y)
    for (var x = 0; x < Width; ++x) {
      // Bottom row first.
      var at = (Height - 1 - y) * BytesPerRow + (x >> 1);
      var v = at < data.Length ? ((x & 1) == 0 ? data[at] >> 4 : data[at] & 0x0F) : 0;
      pixels[y * Width + x] = (byte)v;
    }

    return new() {
      Width = Width,
      Height = Height,
      Format = PixelFormat.Indexed8,
      PixelData = pixels,
      Palette = palette,
      PaletteCount = PaletteCount,
    };
  }
}
