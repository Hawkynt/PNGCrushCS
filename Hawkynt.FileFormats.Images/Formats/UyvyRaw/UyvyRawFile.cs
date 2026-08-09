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
/// <para/>
/// The samples are studio swing — luma 16 to 235, chroma 16 to 240 — which is what the standard this
/// stream comes off the wire under says and what XnView's own converter writes. Reading them as
/// though they filled the whole byte, which is what this did, returns pure red as 237, 15, 14 and is
/// wrong by 15 of 255 on average over a chart of saturated colours; read as studio swing the same
/// chart comes back within 0.26 of 255 on average and 3 at worst, which is the resampling the
/// halved chroma costs and nothing else.
/// <para/>
/// XnView has two names for this one stream and the difference between them is not in the pixels.
/// Its "YUV 16Bits Interleaved" is what is read here — the rows in order. Its "YUV 16Bits" stores
/// the even rows of a frame first and the odd rows after them, and nothing in a headerless stream
/// says which of the two it is; a file of one read as the other comes back correct at the first row
/// and the last and shows the top field's colours through the middle, wrong by 61 of 255 on average.
/// Both names claim this extension, so the progressive reading is the one taken, that being what the
/// four letters mean everywhere they name a capture buffer.
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
  /// Nothing in the file states its size, so the length is the only evidence there is. The first
  /// twenty-five entries are not a list assembled here: they are the one XnView's own reader carries
  /// for this format, in its order, which matters because two of them are the same number of bytes —
  /// 720 by 512 and 640 by 576 both come to 737280, and taking them in this order is what makes this
  /// reader place such a stream where that one places it.
  /// <para/>
  /// The five after them are sizes this reader already accepted and that list does not name. They are
  /// kept because dropping them would refuse files that were being read, and they are kept last so
  /// that they can only ever settle a length XnView refuses outright. A stream matching none of the
  /// thirty is refused rather than shown at a shape picked out of the air.
  /// </remarks>
  internal static readonly (int Width, int Height)[] KnownResolutions = [
    (360, 240),
    (360, 288),
    (352, 480),
    (360, 480),
    (480, 480),
    (528, 480),
    (544, 480),
    (640, 480),
    (704, 480),
    (720, 480),
    (720, 486),
    (720, 512),
    (352, 576),
    (360, 576),
    (480, 576),
    (528, 576),
    (544, 576),
    (640, 576),
    (704, 576),
    (720, 576),
    (720, 608),
    (1280, 720),
    (1280, 1080),
    (1440, 1080),
    (1920, 1080),
    (352, 288),
    (352, 240),
    (320, 240),
    (176, 144),
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

  /// <summary>Studio-swing BT.601: luma spans 16 to 235 and the chroma differences 16 to 240.</summary>
  /// <remarks>
  /// The constants are the usual ones over sixteen bits: 255/219 for the luma, and the three
  /// difference terms each already carrying the 255/224 the chroma range needs.
  /// </remarks>
  private static void _Write(byte[] rgb, int at, int luma, int u, int v) {
    var y = 76309 * (luma - 16);
    rgb[at] = _Clamp((y + 104597 * v + 32768) >> 16);
    rgb[at + 1] = _Clamp((y - 25675 * u - 53279 * v + 32768) >> 16);
    rgb[at + 2] = _Clamp((y + 132201 * u + 32768) >> 16);
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

  /// <summary>Studio-swing BT.601 the other way round, so that what is written is read back.</summary>
  private static byte _Luma(byte[] rgb, int at)
    => _Clamp(((16829 * rgb[at] + 33039 * rgb[at + 1] + 6416 * rgb[at + 2] + 32768) >> 16) + 16);

  private static byte _Chroma(byte[] rgb, int first, int second, bool blue) {
    var a = _Component(rgb, first, blue);
    var b = _Component(rgb, second, blue);
    return _Clamp(((a + b) >> 1) + 128);
  }

  private static int _Component(byte[] rgb, int at, bool blue) => blue
    ? (-9714 * rgb[at] - 19071 * rgb[at + 1] + 28785 * rgb[at + 2] + 32768) >> 16
    : (28785 * rgb[at] - 24103 * rgb[at + 1] - 4682 * rgb[at + 2] + 32768) >> 16;
}
