using System;
using FileFormat.Core;

namespace FileFormat.XlPaint;

/// <summary>In-memory representation of an XL-Paint picture (.xlp).</summary>
/// <remarks>
/// Two Graphics 15 screens shown alternately and averaged, packed together into one run-length
/// stream laid out column by column. Packing down columns rather than across rows is deliberate:
/// the two interlaced screens differ from each other far more than a screen differs from itself
/// vertically, so a column of one screen is the longest run there is to find.
/// <para/>
/// Later files carry a marker and a header; earlier ones carry neither, and the only way to tell a
/// 200-row picture from a 192-row one is to unpack it and see which length the stream fills.
/// </remarks>
public readonly record struct XlPaintFile
  : IImageFormatReader<XlPaintFile>, IImageToRawImage<XlPaintFile>,
    IImageFromRawImage<XlPaintFile>, IImageFormatWriter<XlPaintFile> {

  /// <summary>Screen pixels across.</summary>
  public const int Width = 320;

  /// <summary>Bytes one row of one screen occupies.</summary>
  public const int Stride = Width / 8;

  /// <summary>The text later files start with.</summary>
  public const string Signature = "XLPC";

  static string IImageFormatMetadata<XlPaintFile>.PrimaryExtension => ".xlp";
  static string[] IImageFormatMetadata<XlPaintFile>.FileExtensions => [".xlp"];
  static XlPaintFile IImageFormatReader<XlPaintFile>.FromSpan(ReadOnlySpan<byte> data)
    => XlPaintReader.FromSpan(data);
  static byte[] IImageFormatWriter<XlPaintFile>.ToBytes(XlPaintFile file) => XlPaintWriter.ToBytes(file);
  static VideoMode[] IImageFormatMetadata<XlPaintFile>.VideoModes => [
    new("XL-Paint", [(Width, 192), (Width, 200)], [10])
  ];

  /// <summary>Both unpacked screens, one after the other.</summary>
  public byte[] ScreenData { get; init; }

  /// <summary>Rows.</summary>
  public int Height { get; init; }

  /// <summary>The registers both screens draw from: background, PF0, PF1 and PF2.</summary>
  public byte[] Registers { get; init; }

  public static RawImage ToRawImage(XlPaintFile file) {
    var data = file.ScreenData ?? [];
    var registers = file.Registers ?? [];

    var first = Atari8BitGraphics.DecodeGr15Frame(data, 0, Stride, Width, file.Height, registers);
    var second = Atari8BitGraphics.DecodeGr15Frame(data, file.Height * Stride, Stride, Width, file.Height, registers);

    return new() {
      Width = Width,
      Height = file.Height,
      Format = PixelFormat.Rgb24,
      PixelData = FrameBlend.Average(first, second),
    };
  }

  /// <summary>Rows a written picture takes.</summary>
  private const int WrittenHeight = 192;

  /// <summary>Logical pixels across; each is drawn two screen pixels wide.</summary>
  private const int LogicalWidth = Width / 2;

  /// <summary>Fits a picture into two Graphics 15 screens whose average it is.</summary>
  /// <remarks>
  /// Four registers give four colours on one screen, but the display averages the two — so a pixel
  /// set to one register in the first field and another in the second shows the colour between
  /// them. Four registers therefore reach ten colours rather than four, which is what the format
  /// declares and what makes interlacing worth its second screen.
  /// </remarks>
  public static XlPaintFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);

    var rgb = image.SampleTo(Width, WrittenHeight).EnsureFormat(PixelFormat.Rgb24);
    var bgra = PixelConverter.Convert(rgb, PixelFormat.Bgra32);
    var registers = Atari8BitGraphics.ChooseGr15Registers(
      bgra.PixelData, Width * WrittenHeight, Atari8BitGraphics.Gr15RegisterCount);

    var gtia = Atari8BitGraphics.CreatePalette();
    var screens = new byte[WrittenHeight * 2 * Stride];
    var second = WrittenHeight * Stride;

    for (var y = 0; y < WrittenHeight; ++y)
    for (var lx = 0; lx < LogicalWidth; ++lx) {
      var x = lx * 2;
      var source = (y * Width + x) * 3;

      int bestFirst = 0, bestSecond = 0;
      var bestCost = int.MaxValue;
      for (var a = 0; a < registers.Length; ++a)
      for (var b = 0; b < registers.Length; ++b) {
        var ea = (registers[a] & 254) * 3;
        var eb = (registers[b] & 254) * 3;
        var dr = rgb.PixelData[source] - ((gtia[ea] + gtia[eb]) >> 1);
        var dg = rgb.PixelData[source + 1] - ((gtia[ea + 1] + gtia[eb + 1]) >> 1);
        var db = rgb.PixelData[source + 2] - ((gtia[ea + 2] + gtia[eb + 2]) >> 1);
        var cost = dr * dr + dg * dg + db * db;
        if (cost >= bestCost)
          continue;

        bestCost = cost;
        bestFirst = a;
        bestSecond = b;
      }

      var at = y * Stride + (x >> 3);
      var shift = ~x & 6;
      screens[at] |= (byte)(bestFirst << shift);
      screens[second + at] |= (byte)(bestSecond << shift);
    }

    return new() { ScreenData = screens, Height = WrittenHeight, Registers = registers };
  }
}
