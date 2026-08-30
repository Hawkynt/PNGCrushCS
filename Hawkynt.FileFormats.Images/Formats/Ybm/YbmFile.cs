using System;
using FileFormat.Core;

namespace FileFormat.Ybm;

/// <summary>In-memory representation of a Bennet Yee face-file bitmap (YBM).</summary>
[FormatDetectionPriority(90)]
[FormatMagicBytes([0x21, 0x21])]
public readonly record struct YbmFile : IImageFormatReader<YbmFile>, IImageToRawImage<YbmFile>, IImageFromRawImage<YbmFile>, IImageFormatWriter<YbmFile> {

  /// <summary>Largest dimension representable by the signed 16-bit YBM header.</summary>
  public const int MaximumDimension = short.MaxValue;

  static string IImageFormatMetadata<YbmFile>.PrimaryExtension => ".ybm";
  static string[] IImageFormatMetadata<YbmFile>.FileExtensions => [".ybm"];
  static YbmFile IImageFormatReader<YbmFile>.FromSpan(ReadOnlySpan<byte> data) => YbmReader.FromSpan(data);
  static byte[] IImageFormatWriter<YbmFile>.ToBytes(YbmFile file) => YbmWriter.ToBytes(file);

  /// <summary>Image width in pixels.</summary>
  public int Width { get; init; }

  /// <summary>Image height in pixels.</summary>
  public int Height { get; init; }

  /// <summary>
  /// Raw YBM raster. Each row is padded to a whole 16-bit word. Words are stored big-endian,
  /// while pixels progress from bit 0 through bit 15 inside each word.
  /// </summary>
  public byte[] RasterData { get; init; }

  /// <summary>Gets the encoded byte count of one raster row.</summary>
  public static int GetRowStride(int width) => ((width + 15) >> 4) << 1;

  /// <summary>Converts a YBM bitmap to a platform-independent RGB24 image.</summary>
  public static RawImage ToRawImage(YbmFile file) {
    _ValidateDimensions(file.Width, file.Height, nameof(file));
    var stride = GetRowStride(file.Width);
    var expected = checked(stride * file.Height);
    if (file.RasterData is null || file.RasterData.Length != expected)
      throw new ArgumentException($"YBM raster length must be exactly {expected} bytes.", nameof(file));

    var rgb = new byte[checked(file.Width * file.Height * 3)];
    for (var y = 0; y < file.Height; ++y) {
      var row = y * stride;
      for (var x = 0; x < file.Width; ++x) {
        var wordOffset = row + ((x >> 4) << 1);
        var word = (ushort)((file.RasterData[wordOffset] << 8) | file.RasterData[wordOffset + 1]);
        var black = ((word >> (x & 15)) & 1) != 0;
        var value = black ? (byte)0 : (byte)255;
        var output = (y * file.Width + x) * 3;
        rgb[output] = value;
        rgb[output + 1] = value;
        rgb[output + 2] = value;
      }
    }

    return new RawImage {
      Width = file.Width,
      Height = file.Height,
      Format = PixelFormat.Rgb24,
      PixelData = rgb,
    };
  }

  /// <summary>Creates a monochrome YBM bitmap from an arbitrary image.</summary>
  public static YbmFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    _ValidateDimensions(image.Width, image.Height, nameof(image));
    image = image.EnsureAnyFormat(PixelFormat.Rgb24);

    var stride = GetRowStride(image.Width);
    var raster = new byte[checked(stride * image.Height)];
    for (var y = 0; y < image.Height; ++y)
      for (var x = 0; x < image.Width; ++x) {
        var pixel = (y * image.Width + x) * 3;
        var r = image.PixelData[pixel];
        var g = image.PixelData[pixel + 1];
        var b = image.PixelData[pixel + 2];
        // ITU-R BT.601 integer luma. YBM is bilevel, so values below mid-grey become black.
        var luma = (299 * r + 587 * g + 114 * b + 500) / 1000;
        if (luma >= 128)
          continue;

        var wordOffset = y * stride + ((x >> 4) << 1);
        var bit = x & 15;
        if (bit < 8)
          raster[wordOffset + 1] |= (byte)(1 << bit);
        else
          raster[wordOffset] |= (byte)(1 << (bit - 8));
      }

    return new YbmFile {
      Width = image.Width,
      Height = image.Height,
      RasterData = raster,
    };
  }

  internal static void Validate(YbmFile file, string parameterName) {
    _ValidateDimensions(file.Width, file.Height, parameterName);
    var expected = checked(GetRowStride(file.Width) * file.Height);
    if (file.RasterData is null || file.RasterData.Length != expected)
      throw new ArgumentException($"YBM raster length must be exactly {expected} bytes.", parameterName);
  }

  private static void _ValidateDimensions(int width, int height, string parameterName) {
    if (width <= 0 || width > MaximumDimension)
      throw new ArgumentOutOfRangeException(parameterName, $"YBM width must be in the range 1..{MaximumDimension}.");
    if (height <= 0 || height > MaximumDimension)
      throw new ArgumentOutOfRangeException(parameterName, $"YBM height must be in the range 1..{MaximumDimension}.");
  }
}
