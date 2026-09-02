using System;
using FileFormat.Core;

namespace FileFormat.Mtv;

/// <summary>In-memory representation of an MTV/PRT ray-tracer image.</summary>
/// <remarks>
/// The original MTV layout is one ASCII <c>width height</c> line followed by one red, green and blue
/// byte per pixel. PRT and Rayshade use the same raster layout.
/// </remarks>
[FormatDetectionPriority(999)]
public readonly record struct MtvFile :
  IImageFormatReader<MtvFile>, IImageToRawImage<MtvFile>,
  IImageFromRawImage<MtvFile>, IImageFormatWriter<MtvFile> {

  internal const int MaximumPixels = 100_000_000;

  static string IImageFormatMetadata<MtvFile>.PrimaryExtension => ".mtv";

  /// <summary><c>.pic</c> is the same raster under Rayshade's name for it.</summary>
  /// <remarks>
  /// Rayshade writes Utah RLE only when it is built against that toolkit; without it, per its own
  /// README, it "can be configured to create image files using a generic format identical to that
  /// used by Mark VandeWettering's 'mtv' ray tracer", and that is what lands in a <c>.pic</c>.
  /// XnView lists Rayshade and MTV as two formats but its writers emit the same bytes — one 61x37
  /// picture converted both ways gave files that compare equal.
  /// <para/>
  /// Several other formats answer to <c>.pic</c>, all of them with stronger binary signatures. The
  /// structural MTV probe therefore runs at the registry's deliberately late priority 999.
  /// </remarks>
  static string[] IImageFormatMetadata<MtvFile>.FileExtensions => [".mtv", ".pic"];
  static MtvFile IImageFormatReader<MtvFile>.FromSpan(ReadOnlySpan<byte> data) => MtvReader.FromSpan(data);
  static byte[] IImageFormatWriter<MtvFile>.ToBytes(MtvFile file) => MtvWriter.ToBytes(file);
  static VideoMode[] IImageFormatMetadata<MtvFile>.VideoModes => [
    new("Default", [(IntegerRange.Any, IntegerRange.Any)])
  ];

  static bool? IImageFormatMetadata<MtvFile>.MatchesSignature(ReadOnlySpan<byte> header)
    => MtvReader.MatchesSignature(header);

  /// <summary>Image width in pixels.</summary>
  public int Width { get; init; }

  /// <summary>Image height in pixels.</summary>
  public int Height { get; init; }

  /// <summary>Top-to-bottom, left-to-right RGB24 pixel bytes.</summary>
  public byte[] PixelData { get; init; }

  public static RawImage ToRawImage(MtvFile file) {
    Validate(file, nameof(file));
    return new() {
      Width = file.Width,
      Height = file.Height,
      Format = PixelFormat.Rgb24,
      PixelData = file.PixelData[..],
    };
  }

  public static MtvFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    image = image.EnsureFormat(PixelFormat.Rgb24);
    ValidateDimensions(image.Width, image.Height, nameof(image));

    var expected = checked(image.Width * image.Height * 3);
    if (image.PixelData.Length < expected)
      throw new ArgumentException($"RGB pixel data must contain at least {expected} bytes.", nameof(image));

    return new() {
      Width = image.Width,
      Height = image.Height,
      PixelData = image.PixelData[..expected],
    };
  }

  internal static void Validate(MtvFile file, string parameterName) {
    ValidateDimensions(file.Width, file.Height, parameterName);
    var expected = checked(file.Width * file.Height * 3);
    if (file.PixelData is null || file.PixelData.Length != expected)
      throw new ArgumentException($"MTV pixel data must be exactly {expected} bytes.", parameterName);
  }

  internal static void ValidateDimensions(int width, int height, string parameterName) {
    if (width <= 0)
      throw new ArgumentOutOfRangeException(parameterName, "MTV width must be positive.");
    if (height <= 0)
      throw new ArgumentOutOfRangeException(parameterName, "MTV height must be positive.");

    if ((long)width * height > MaximumPixels)
      throw new ArgumentOutOfRangeException(parameterName, $"MTV images may contain at most {MaximumPixels:N0} pixels.");
  }
}
