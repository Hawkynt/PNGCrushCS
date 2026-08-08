using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.DelmPaint;

/// <summary>In-memory representation of a DelmPaint picture (.del, .dph).</summary>
/// <remarks>
/// A Falcon picture at eight bitplanes against 256 freely chosen colours, packed in blocks of
/// exactly 32000 bytes. The block size is not arbitrary: it is the ST screen the program grew out
/// of, so a picture is stored as however many screens it takes rather than as one stream.
/// <para/>
/// The larger form assembles 640x480 out of four 320x240 quadrants that share one palette, which is
/// the same reason — the Falcon could hold a quadrant in the memory an ST screen occupied.
/// </remarks>
public readonly record struct DelmPaintFile
  : IImageFormatReader<DelmPaintFile>, IImageToRawImage<DelmPaintFile>,
    IImageFromRawImage<DelmPaintFile>, IImageFormatWriter<DelmPaintFile> {

  /// <summary>Bytes a packed block unpacks to.</summary>
  public const int BlockSize = 32000;

  /// <summary>Colours the palette holds.</summary>
  public const int ColorCount = 256;

  /// <summary>Size of the palette, four bytes a colour.</summary>
  public const int PaletteSize = ColorCount * 4;

  /// <summary>Bitplanes a pixel is spread over.</summary>
  public const int Planes = 8;

  /// <summary>Pixels across one quadrant.</summary>
  public const int QuadrantWidth = 320;

  /// <summary>Rows in one quadrant.</summary>
  public const int QuadrantHeight = 240;

  /// <summary>Bytes one quadrant's bitmap occupies.</summary>
  public const int QuadrantSize = QuadrantWidth * QuadrantHeight;

  static string IImageFormatMetadata<DelmPaintFile>.PrimaryExtension => ".del";
  static string[] IImageFormatMetadata<DelmPaintFile>.FileExtensions => [".del", ".dph"];
  static DelmPaintFile IImageFormatReader<DelmPaintFile>.FromSpan(ReadOnlySpan<byte> data)
    => DelmPaintReader.FromSpan(data);

  /// <summary>
  /// Reads a named file, the extension being what its reader needs.
  /// </summary>
  /// <remarks>
  /// The reader takes the extension into account and only the by-bytes entry was wired up here,
  /// so the registry could never reach it: whatever the extension would have settled was decided
  /// by a default instead. Ten formats carried this, each one otherwise found only when a sample
  /// happened to expose it.
  /// </remarks>
  static DelmPaintFile IImageFormatReader<DelmPaintFile>.FromFile(FileInfo file) => DelmPaintReader.FromFile(file);
  static byte[] IImageFormatWriter<DelmPaintFile>.ToBytes(DelmPaintFile file) => DelmPaintWriter.ToBytes(file);
  static VideoMode[] IImageFormatMetadata<DelmPaintFile>.VideoModes => [
    new("DelmPaint", [(QuadrantWidth, QuadrantHeight), (QuadrantWidth * 2, QuadrantHeight * 2)], [ColorCount])
  ];

  /// <summary>The unpacked blocks.</summary>
  public byte[] Unpacked { get; init; }

  /// <summary>Pixels across.</summary>
  public int Width { get; init; }

  /// <summary>Rows.</summary>
  public int Height { get; init; }

  public static RawImage ToRawImage(DelmPaintFile file) {
    var data = file.Unpacked ?? [];
    var pixels = new byte[file.Width * file.Height];

    // One quadrant, or four laid out two by two — the offsets are the same picture either way.
    (int Source, int Left, int Top)[] quadrants = file.Width == QuadrantWidth
      ? [(PaletteSize, 0, 0)]
      : [
        (PaletteSize, 0, 0),
        (PaletteSize + QuadrantSize, QuadrantWidth, 0),
        (PaletteSize + QuadrantSize * 2, 0, QuadrantHeight),
        (PaletteSize + QuadrantSize * 3, QuadrantWidth, QuadrantHeight),
      ];

    foreach (var (source, left, top) in quadrants) {
      var indices = AtariStGraphics.UnpackBitplanes(
        data, source, QuadrantWidth, Planes, QuadrantWidth, QuadrantHeight);

      for (var y = 0; y < QuadrantHeight; ++y)
        indices.AsSpan(y * QuadrantWidth, QuadrantWidth)
          .CopyTo(pixels.AsSpan((top + y) * file.Width + left));
    }

    return new() {
      Width = file.Width,
      Height = file.Height,
      Format = PixelFormat.Indexed8,
      PixelData = pixels,
      Palette = AtariStGraphics.ReadFalconPalette(data, 0, ColorCount),
      PaletteCount = ColorCount,
    };
  }

  /// <summary>Encodes a picture as one quadrant, which is the form the .del extension names.</summary>
  /// <remarks>
  /// The four-quadrant form is not written. Nothing inside a file says which of the two it is — the
  /// reader is told by the extension — so writing the larger one would produce bytes that read as a
  /// picture only when the caller also named the file .dph, and read as a truncated one otherwise.
  /// <para/>
  /// The unpacked side is three blocks of 32000 bytes whatever the picture needs, because the packer
  /// works a block at a time and the last one holds the remainder of a screen that does not divide
  /// by the block size. Only the first 77824 of those bytes are the palette and the quadrant; the
  /// rest is what the program had in memory past its picture, and zero is as good an answer as any.
  /// </remarks>
  public static DelmPaintFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);

    var indexed = image.SampleTo(QuadrantWidth, QuadrantHeight).EnsureIndexedAtMost(ColorCount);
    var unpacked = new byte[BlockSize * 3];
    AtariStGraphics.WriteFalconPalette(indexed.Palette ?? [], ColorCount, unpacked);

    AtariStGraphics
      .PackBitplanes(indexed.PixelData, QuadrantWidth, Planes, QuadrantWidth, QuadrantHeight)
      .CopyTo(unpacked, PaletteSize);

    return new() { Unpacked = unpacked, Width = QuadrantWidth, Height = QuadrantHeight };
  }
}
