using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.UyvyRaw;

/// <summary>In-memory representation of a raw UYVY 4:2:2 image.</summary>
/// <remarks>
/// Two pixels to four bytes, interleaved rather than planar: a shared blue-difference, the first
/// pixel's luma, a shared red-difference, then the second pixel's luma. That is the order the name
/// spells out, and it is what capture hardware hands over — which is why the format exists at all
/// and why it has no header to say so.
/// <para/>
/// Not the same thing as the planar 4:2:0 stream this project already reads: that one keeps its
/// three components in three blocks and halves the chroma vertically as well. Neither can be read
/// as the other.
/// </remarks>
public readonly record struct UyvyRawFile
  : IImageFormatReader<UyvyRawFile>, IImageToRawImage<UyvyRawFile>,
    IImageFromRawImage<UyvyRawFile>, IImageFormatWriter<UyvyRawFile> {

  /// <summary>Bytes one pixel takes, the chroma being shared with its neighbour.</summary>
  public const int BytesPerPixel = 2;

  static string IImageFormatMetadata<UyvyRawFile>.PrimaryExtension => ".uyvy";
  static string[] IImageFormatMetadata<UyvyRawFile>.FileExtensions => [".uyvy"];
  static UyvyRawFile IImageFormatReader<UyvyRawFile>.FromSpan(ReadOnlySpan<byte> data) => UyvyRawReader.FromSpan(data);
  static byte[] IImageFormatWriter<UyvyRawFile>.ToBytes(UyvyRawFile file) => UyvyRawWriter.ToBytes(file);
  static VideoMode[] IImageFormatMetadata<UyvyRawFile>.VideoModes => [
    new("Default", [(720, 576)])
  ];

  /// <summary>
  /// The sizes a headerless stream is guessed to be, largest first where two share a length.
  /// </summary>
  /// <remarks>
  /// Nothing in the file states its size, so the length is the only evidence there is. These are
  /// the frame sizes capture hardware produces; a stream of any other length cannot be placed and
  /// is refused rather than shown at a shape picked out of the air.
  /// </remarks>
  internal static readonly (int Width, int Height)[] KnownResolutions = [
    (720, 576),
    (720, 486),
    (720, 480),
    (704, 576),
    (640, 480),
    (352, 288),
    (352, 240),
    (320, 240),
    (176, 144),
    (1920, 1080),
    (1280, 720),
    (64, 64),
  ];

  public int Width { get; init; }

  public int Height { get; init; }

  /// <summary>The stream as it lies, four bytes to every two pixels.</summary>
  public byte[] PixelData { get; init; }

  /// <summary>Which frame a stream of a given length is.</summary>
  internal static (int Width, int Height) SizeOf(int length) {
    foreach (var (width, height) in KnownResolutions)
      if (width * height * BytesPerPixel == length)
        return (width, height);

    throw new InvalidDataException(
      $"A UYVY stream states no size, and {length} bytes is not one of the frame sizes it comes in.");
  }

  public static RawImage ToRawImage(UyvyRawFile file) {
    var width = file.Width;
    var height = file.Height;
    var rgb = new byte[width * height * 3];

    for (var y = 0; y < height; ++y)
    for (var x = 0; x < width; x += 2) {
      var at = (y * width + x) * BytesPerPixel;
      if (at + 3 >= file.PixelData.Length)
        break;

      int u = file.PixelData[at] - 128;
      int luma0 = file.PixelData[at + 1];
      int v = file.PixelData[at + 2] - 128;
      int luma1 = file.PixelData[at + 3];

      _Write(rgb, (y * width + x) * 3, luma0, u, v);
      if (x + 1 < width)
        _Write(rgb, (y * width + x + 1) * 3, luma1, u, v);
    }

    return new() { Width = width, Height = height, Format = PixelFormat.Rgb24, PixelData = rgb };
  }

  /// <summary>Full-range BT.601, which is what the hardware and every reader of these assume.</summary>
  private static void _Write(byte[] rgb, int at, int luma, int u, int v) {
    rgb[at] = _Clamp(luma + ((91881 * v) >> 16));
    rgb[at + 1] = _Clamp(luma - ((22554 * u + 46802 * v) >> 16));
    rgb[at + 2] = _Clamp(luma + ((116130 * u) >> 16));
  }

  private static byte _Clamp(int value) => (byte)(value < 0 ? 0 : value > 255 ? 255 : value);

  public static UyvyRawFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);

    // The width has to be even: two pixels share one pair of chroma samples, and half a pair is
    // not expressible.
    var width = Math.Max(2, image.Width & ~1);
    var height = Math.Max(1, image.Height);
    var rgb = (image.Width == width ? image : image.SampleTo(width, height)).EnsureFormat(PixelFormat.Rgb24);

    var data = new byte[width * height * BytesPerPixel];

    for (var y = 0; y < height; ++y)
    for (var x = 0; x < width; x += 2) {
      var first = (y * width + x) * 3;
      var second = (y * width + Math.Min(x + 1, width - 1)) * 3;
      var at = (y * width + x) * BytesPerPixel;

      // The chroma of a pair is the mean of its two pixels', which is what halving the horizontal
      // chroma resolution means.
      data[at] = _Chroma(rgb.PixelData, first, second, blue: true);
      data[at + 1] = _Luma(rgb.PixelData, first);
      data[at + 2] = _Chroma(rgb.PixelData, first, second, blue: false);
      data[at + 3] = _Luma(rgb.PixelData, second);
    }

    return new() { Width = width, Height = height, PixelData = data };
  }

  private static byte _Luma(byte[] rgb, int at)
    => _Clamp((19595 * rgb[at] + 38470 * rgb[at + 1] + 7471 * rgb[at + 2]) >> 16);

  private static byte _Chroma(byte[] rgb, int first, int second, bool blue) {
    var a = _Component(rgb, first, blue);
    var b = _Component(rgb, second, blue);
    return _Clamp(((a + b) >> 1) + 128);
  }

  private static int _Component(byte[] rgb, int at, bool blue) => blue
    ? (-11056 * rgb[at] - 21712 * rgb[at + 1] + 32768 * rgb[at + 2]) >> 16
    : (32768 * rgb[at] - 27440 * rgb[at + 1] - 5328 * rgb[at + 2]) >> 16;
}
