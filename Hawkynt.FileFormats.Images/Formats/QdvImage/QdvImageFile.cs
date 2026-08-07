using System;
using FileFormat.Core;

namespace FileFormat.QdvImage;

/// <summary>In-memory representation of a QDV picture (.qdv).</summary>
/// <remarks>
/// Five bytes of header, a 256-entry palette of RGB triplets, then one byte a pixel. There is no
/// magic: the size in the header and the length of the file are what identify one.
/// <para/>
/// What was here before expected four bytes reading "QDV\0" and a twelve-byte header carrying a
/// depth and a flags word. No file has that — it was an invention, and the one real sample was
/// refused by it. The sample states 640 by 480, and 5 plus 768 plus 640 times 480 is its length to
/// the byte.
/// </remarks>
public readonly record struct QdvImageFile
  : IImageFormatReader<QdvImageFile>, IImageToRawImage<QdvImageFile>,
    IImageFromRawImage<QdvImageFile>, IImageFormatWriter<QdvImageFile> {

  static string IImageFormatMetadata<QdvImageFile>.PrimaryExtension => ".qdv";
  static string[] IImageFormatMetadata<QdvImageFile>.FileExtensions => [".qdv"];
  static QdvImageFile IImageFormatReader<QdvImageFile>.FromSpan(ReadOnlySpan<byte> data) => QdvImageReader.FromSpan(data);
  static byte[] IImageFormatWriter<QdvImageFile>.ToBytes(QdvImageFile file) => QdvImageWriter.ToBytes(file);
  static VideoMode[] IImageFormatMetadata<QdvImageFile>.VideoModes => [
    new("Default", [(new IntegerRange(1, ushort.MaxValue), new IntegerRange(1, ushort.MaxValue))], [PaletteCount])
  ];

  /// <summary>Width and height as big-endian words, then the highest index in use.</summary>
  public const int HeaderSize = 5;

  /// <summary>Entries the palette holds.</summary>
  public const int PaletteCount = 256;

  /// <summary>Bytes the palette takes: one RGB triplet an entry.</summary>
  public const int PaletteSize = PaletteCount * 3;

  /// <summary>Where the picture starts, the palette lying between it and the header.</summary>
  public const int PixelOffset = HeaderSize + PaletteSize;

  /// <summary>The least a file can be: a header, a palette and one pixel.</summary>
  public const int MinFileSize = PixelOffset + 1;

  public int Width { get; init; }

  public int Height { get; init; }

  /// <summary>The highest palette index the picture uses, as the header states it.</summary>
  public byte HighestIndex { get; init; }

  /// <summary>The palette, 256 RGB triplets.</summary>
  public byte[] Palette { get; init; }

  /// <summary>One byte a pixel.</summary>
  public byte[] PixelData { get; init; }

  public static RawImage ToRawImage(QdvImageFile file) => new() {
    Width = file.Width,
    Height = file.Height,
    Format = PixelFormat.Indexed8,
    PixelData = (file.PixelData ?? [])[..],
    Palette = (file.Palette ?? new byte[PaletteSize])[..],
    PaletteCount = PaletteCount,
  };

  public static QdvImageFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    var indexed = image.EnsureIndexedAtMost(PaletteCount);

    var palette = new byte[PaletteSize];
    (indexed.Palette ?? []).AsSpan(0, Math.Min(indexed.PaletteCount * 3, PaletteSize)).CopyTo(palette);

    return new() {
      Width = indexed.Width,
      Height = indexed.Height,
      HighestIndex = (byte)Math.Max(0, Math.Min(PaletteCount - 1, indexed.PaletteCount - 1)),
      Palette = palette,
      PixelData = indexed.PixelData[..],
    };
  }
}
