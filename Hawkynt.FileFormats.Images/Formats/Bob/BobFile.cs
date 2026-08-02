using System;
using FileFormat.Core;

namespace FileFormat.Bob;

/// <summary>In-memory representation of a Bob Raytracer image.</summary>
/// <remarks>
/// Four bytes of size, a 256-entry palette, then one index a pixel — which the arithmetic settles
/// exactly: the sample is 1419 by 1001, and 4 + 768 + 1419 * 1001 is its length to the byte.
/// <para/>
/// What was here before read the height from the wrong place, called the pixels 24-bit and had no
/// palette at all, so a 1.4 MB file came back as 1419 by 65535 — a 93 megapixel picture a viewer
/// would try to allocate. Decoded as above it matches XnView's rendering of the same file to the
/// byte.
/// </remarks>
public readonly record struct BobFile : IImageFormatReader<BobFile>, IImageToRawImage<BobFile>, IImageFromRawImage<BobFile>, IImageFormatWriter<BobFile> {

  /// <summary>Bytes of size information before the palette.</summary>
  internal const int HeaderSize = 4;

  /// <summary>Entries the palette holds.</summary>
  internal const int PaletteCount = 256;

  /// <summary>Bytes the palette takes.</summary>
  internal const int PaletteSize = PaletteCount * 3;

  /// <summary>Offset the indices start at.</summary>
  internal const int PixelOffset = HeaderSize + PaletteSize;

  /// <summary>The length a file of the given size has.</summary>
  internal static long SizeOf(int width, int height) => PixelOffset + (long)width * height;

  static string IImageFormatMetadata<BobFile>.PrimaryExtension => ".bob";
  static string[] IImageFormatMetadata<BobFile>.FileExtensions => [".bob"];
  static BobFile IImageFormatReader<BobFile>.FromSpan(ReadOnlySpan<byte> data) => BobReader.FromSpan(data);
  static byte[] IImageFormatWriter<BobFile>.ToBytes(BobFile file) => BobWriter.ToBytes(file);

  public int Width { get; init; }

  public int Height { get; init; }

  /// <summary>One palette index a pixel.</summary>
  public byte[] PixelData { get; init; }

  /// <summary>256 colours, three bytes each.</summary>
  public byte[] Palette { get; init; }

  public static RawImage ToRawImage(BobFile file) => new() {
    Width = file.Width,
    Height = file.Height,
    Format = PixelFormat.Indexed8,
    PixelData = file.PixelData[..],
    Palette = file.Palette[..],
    PaletteCount = PaletteCount,
  };

  public static BobFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);

    var indexed = image.EnsureIndexedAtMost(PaletteCount);
    var palette = new byte[PaletteSize];
    var source = indexed.Palette;
    if (source != null)
      source.AsSpan(0, Math.Min(source.Length, palette.Length)).CopyTo(palette);

    var pixels = new byte[image.Width * image.Height];
    indexed.PixelData.AsSpan(0, Math.Min(indexed.PixelData.Length, pixels.Length)).CopyTo(pixels);

    return new() {
      Width = image.Width,
      Height = image.Height,
      PixelData = pixels,
      Palette = palette,
    };
  }
}
