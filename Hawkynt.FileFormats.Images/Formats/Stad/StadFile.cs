using System;
using FileFormat.Core;

namespace FileFormat.Stad;

/// <summary>In-memory representation of a STAD compressed Atari ST high-resolution screen image.</summary>
[FormatMagicBytes([0x70, 0x4D, 0x38])]
public readonly record struct StadFile : IImageFormatReader<StadFile>, IImageToRawImage<StadFile>, IImageFromRawImage<StadFile>, IImageFormatWriter<StadFile> {

  /// <summary>Decompressed screen data size in bytes (640x400 monochrome = 32000 bytes).</summary>
  internal const int ScreenDataSize = 32_000;

  /// <summary>Fixed pixel width.</summary>
  internal const int PixelWidth = 640;

  /// <summary>Fixed pixel height.</summary>
  internal const int PixelHeight = 400;

  /// <summary>Bytes per pixel row (640 / 8).</summary>
  internal const int BytesPerRow = PixelWidth / 8;

  static string IImageFormatMetadata<StadFile>.PrimaryExtension => ".pac";
  static string[] IImageFormatMetadata<StadFile>.FileExtensions => [".pac"];
  static StadFile IImageFormatReader<StadFile>.FromSpan(ReadOnlySpan<byte> data) => StadReader.FromSpan(data);
  static VideoMode[] IImageFormatMetadata<StadFile>.VideoModes => [new("Atari ST high resolution", [(PixelWidth, PixelHeight)], [2])];
  static byte[] IImageFormatWriter<StadFile>.ToBytes(StadFile file) => StadWriter.ToBytes(file);

  /// <summary>Always 640.</summary>
  public int Width => PixelWidth;

  /// <summary>Always 400.</summary>
  public int Height => PixelHeight;

  /// <summary>Raw decompressed screen data (32000 bytes).</summary>
  public byte[] RawData { get; init; }

  /// <summary>Whether compressed bytes are stored row-first or byte-column-first.</summary>
  public StadPacking Packing { get; init; }

  // Parsed files retain their original compression header values so a caller that only reads and
  // writes does not silently change them. Newly-authored files leave this false and choose fresh,
  // deterministic values from the raster histogram.
  internal bool HasCompressionParameters { get; init; }
  internal byte IdByte { get; init; }
  internal byte PackByte { get; init; }
  internal byte SpecialByte { get; init; }

  private static readonly byte[] _BlackWhitePalette = [255, 255, 255, 0, 0, 0];

  /// <summary>Reduces a picture to the fixed 640x400 Atari ST monochrome screen.</summary>
  public static StadFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);

    var rgb = image.SampleTo(PixelWidth, PixelHeight).PixelData;
    var screen = new byte[ScreenDataSize];

    for (var y = 0; y < PixelHeight; ++y)
      for (var x = 0; x < PixelWidth; ++x) {
        var at = (y * PixelWidth + x) * 3;
        var brightness = (rgb[at] * 299 + rgb[at + 1] * 587 + rgb[at + 2] * 114) / 1000;
        if (brightness < 128)
          screen[y * BytesPerRow + x / 8] |= (byte)(1 << (7 - (x % 8)));
      }

    return new() {
      RawData = screen,
      Packing = StadWriter.SelectPacking(screen),
    };
  }

  /// <summary>Converts the STAD screen to an Indexed1 raw image with white paper and black ink.</summary>
  public static RawImage ToRawImage(StadFile file) {
    Validate(file, nameof(file));
    return new() {
      Width = PixelWidth,
      Height = PixelHeight,
      Format = PixelFormat.Indexed1,
      PixelData = file.RawData[..],
      Palette = _BlackWhitePalette[..],
      PaletteCount = 2,
    };
  }

  internal static void Validate(StadFile file, string parameterName) {
    if (file.RawData is null || file.RawData.Length != ScreenDataSize)
      throw new ArgumentException($"STAD screen data must contain exactly {ScreenDataSize} bytes.", parameterName);
    if (file.Packing is not StadPacking.Horizontal and not StadPacking.Vertical)
      throw new ArgumentException($"Unsupported STAD packing value {(byte)file.Packing}.", parameterName);
    if (file.HasCompressionParameters && file.IdByte == file.SpecialByte)
      throw new ArgumentException("STAD id and special escape bytes must differ.", parameterName);
  }
}
