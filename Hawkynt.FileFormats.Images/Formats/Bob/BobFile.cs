using System;
using FileFormat.Core;
using FileFormat.Core.PixelFormats;

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
[VerifiedBy(ConformanceOracle.XnView)]
public readonly record struct BobFile :
  IImageFormatReader<BobFile>,
  IImageToRawImage<BobFile>,
  IImageFromRawImage<BobFile>,
  IImageFromRawImage<BobFile, Indexed8>,
  IImageFormatWriter<BobFile> {

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

  /// <summary>
  /// The untyped entry point, which adapts whatever it is given into the one representation Bob
  /// stores and then hands it to the typed overload.
  /// </summary>
  /// <remarks>
  /// It adapts rather than refusing because this is the method the registry calls, and the registry's
  /// writer contract is that any <see cref="RawImage"/> can be written and read back — the
  /// write-coverage sweep hands every registered writer a 32-bit picture and demands a file. A
  /// version of this that required the caller to have already quantised turned that sweep red for
  /// this one format while every other format still adapted, which is a contract change rather than
  /// a Bob change. The typed overload below is where a caller who has already done the work says so
  /// and gets the exactness that comes with it.
  /// </remarks>
  public static BobFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    return FromRawImage(RawImage<Indexed8>.FromUntyped(image.EnsureIndexedAtMost(PaletteCount)));
  }

  /// <summary>Creates a Bob file from the exact raw representation the format stores.</summary>
  public static BobFile FromRawImage(RawImage<Indexed8> image) {
    ArgumentNullException.ThrowIfNull(image);
    image.Validate();

    if (image.Width is <= 0 or > ushort.MaxValue)
      throw new ArgumentException($"Bob width must be in the range 1..{ushort.MaxValue}; got {image.Width}.", nameof(image));
    if (image.Height is <= 0 or > ushort.MaxValue)
      throw new ArgumentException($"Bob height must be in the range 1..{ushort.MaxValue}; got {image.Height}.", nameof(image));

    var pixelCount = checked(image.Width * image.Height);
    if (image.PixelData.Length != pixelCount)
      throw new ArgumentException(
        $"Bob requires exactly one byte per pixel ({pixelCount} bytes); got {image.PixelData.Length}.",
        nameof(image));

    if (image.PaletteCount != PaletteCount)
      throw new ArgumentException($"Bob requires exactly {PaletteCount} palette entries; got {image.PaletteCount}.", nameof(image));

    var palette = image.Palette;
    if (palette is null || palette.Length != PaletteSize)
      throw new ArgumentException($"Bob requires exactly {PaletteSize} RGB palette bytes.", nameof(image));

    if (image.AlphaTable is { } alphaTable)
      foreach (var alpha in alphaTable)
        if (alpha != byte.MaxValue)
          throw new ArgumentException("Bob cannot encode palette transparency.", nameof(image));

    return new() {
      Width = image.Width,
      Height = image.Height,
      PixelData = image.PixelData[..],
      Palette = palette[..],
    };
  }
}
