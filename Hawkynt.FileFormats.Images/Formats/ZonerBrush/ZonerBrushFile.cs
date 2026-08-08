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
  : IImageFormatReader<ZonerBrushFile>, IImageToRawImage<ZonerBrushFile>, IImageFromRawImage<ZonerBrushFile>, IImageFormatWriter<ZonerBrushFile> {

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

  /// <summary>What all three samples open with, and what a ZBrush file under the same name does not.</summary>
  /// <remarks>
  /// Byte 2 differs between files, so it is not part of the test; bytes 0, 1 and 3 through 7 are the
  /// same in every one. ZBrush also writes <c>.zbr</c> and opens with the words "ZBrush File", which
  /// this refuses.
  /// </remarks>
  internal static ReadOnlySpan<byte> Signature => [0x9A, 0x02, 0x02, 0x00, 0x2D, 0x2D, 0x2D, 0x00];

  internal static bool HasSignature(ReadOnlySpan<byte> data)
    => data.Length >= 8
       && data[0] == 0x9A && data[1] == 0x02
       && data[3] == 0x00 && data[4] == 0x2D && data[5] == 0x2D && data[6] == 0x2D && data[7] == 0x00;

  static bool? IImageFormatMetadata<ZonerBrushFile>.MatchesSignature(ReadOnlySpan<byte> header)
    => header.Length < 8 ? null : HasSignature(header);
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

    // Blue first, as the entry is laid out — this used to take the bytes in the order they came and
    // so drew every preview with its reds and blues exchanged. The samples settle it: all three
    // carry the standard Windows sixteen, whose entry 1 is dark red, and the 0x7F in that entry sits
    // in the third byte. Entry 4 is dark blue and has its 0x7F in the first.
    for (var i = 0; i < PaletteCount && i * PaletteEntrySize + 2 < stored.Length; ++i) {
      palette[i * 3] = stored[i * PaletteEntrySize + 2];
      palette[i * 3 + 1] = stored[i * PaletteEntrySize + 1];
      palette[i * 3 + 2] = stored[i * PaletteEntrySize];
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

  /// <summary>Creates a brush carrying the picture as its preview.</summary>
  /// <remarks>
  /// Only the preview is written, the drawing behind it never having been read, so what comes out is
  /// a file whose picture any reader of these can draw and which no tool can paint with. That is the
  /// same limit <see cref="ZonerBrushWriter"/> already states, reached from the other side.
  /// <para/>
  /// The preview is a fixed 100 by 100, so a picture of any other size is sampled onto it rather
  /// than refused, and reduced to the sixteen colours four bits a pixel can address. The rows go
  /// bottom upwards and the palette entries blue first, both matching what <see cref="ToRawImage"/>
  /// reads; the header is the eight bytes all three samples open with, which is as much of it as is
  /// known to be constant.
  /// </remarks>
  public static ZonerBrushFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);

    var quantized = ColorQuantizer.Quantize(image.SampleTo(Width, Height).ToBgra32(), Width * Height, PaletteCount);

    var palette = new byte[PaletteCount * PaletteEntrySize];
    for (var i = 0; i < quantized.Count; ++i) {
      palette[i * PaletteEntrySize] = quantized.Palette[i * 3 + 2];
      palette[i * PaletteEntrySize + 1] = quantized.Palette[i * 3 + 1];
      palette[i * PaletteEntrySize + 2] = quantized.Palette[i * 3];
    }

    var pixels = new byte[BytesPerRow * Height];
    for (var y = 0; y < Height; ++y)
    for (var x = 0; x < Width; ++x) {
      var index = quantized.Indices[y * Width + x] & 0x0F;
      var at = (Height - 1 - y) * BytesPerRow + (x >> 1);
      pixels[at] |= (byte)((x & 1) == 0 ? index << 4 : index);
    }

    return new() {
      Header = [0x9A, 0x02, 0x02, 0x00, 0x2D, 0x2D, 0x2D, 0x00],
      Palette = palette,
      PixelData = pixels,
    };
  }
}
