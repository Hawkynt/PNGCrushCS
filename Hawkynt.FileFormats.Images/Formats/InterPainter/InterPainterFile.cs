using System;
using FileFormat.Core;

namespace FileFormat.InterPainter;

/// <summary>In-memory representation of an Atari 8-bit InterPainter / ING 15 (.inp, .ing) screen.</summary>
/// <remarks>
/// Mode E only offers four colours, so InterPainter stores two full-screen bitmaps and flips
/// between them every frame. The eye averages the two, which turns four registers into ten usable
/// shades: the four registers themselves plus the six ways of pairing them. The file is the two
/// 8000-byte bitmaps followed by the background and PF0-PF2 colour bytes.
/// </remarks>
public readonly record struct InterPainterFile
  : IImageFormatReader<InterPainterFile>, IImageToRawImage<InterPainterFile>,
    IImageFromRawImage<InterPainterFile>, IImageFormatWriter<InterPainterFile> {

  /// <summary>Displayed width.</summary>
  public const int DisplayWidth = 320;

  /// <summary>Displayed height.</summary>
  public const int DisplayHeight = 200;

  /// <summary>Size of one bitmap.</summary>
  public const int FrameDataSize = Atari8BitGraphics.Gr7BytesPerRow * DisplayHeight;

  /// <summary>Offset of the second bitmap.</summary>
  public const int SecondFrameOffset = FrameDataSize;

  /// <summary>Offset of the colour bytes.</summary>
  public const int ColorsOffset = FrameDataSize * 2;

  /// <summary>Colour bytes stored, in the order background, PF0, PF1, PF2.</summary>
  public const int ColorCount = 4;

  /// <summary>Total file size.</summary>
  public const int FileSize = ColorsOffset + ColorCount;

  /// <summary>Distinct shades the two frames can average to.</summary>
  public const int BlendCount = ColorCount * (ColorCount + 1) / 2;

  static string IImageFormatMetadata<InterPainterFile>.PrimaryExtension => ".inp";
  static string[] IImageFormatMetadata<InterPainterFile>.FileExtensions => [".inp", ".ing", ".ins"];
  static InterPainterFile IImageFormatReader<InterPainterFile>.FromSpan(ReadOnlySpan<byte> data) => InterPainterReader.FromSpan(data);
  static byte[] IImageFormatWriter<InterPainterFile>.ToBytes(InterPainterFile file) => InterPainterWriter.ToBytes(file);
  static VideoMode[] IImageFormatMetadata<InterPainterFile>.VideoModes => [
    new("Interlaced Graphics 15", [(DisplayWidth, DisplayHeight)], [BlendCount])
  ];

  /// <summary>Packed mode E bitmap shown on even frames.</summary>
  public byte[] FirstFrame { get; init; }

  /// <summary>Packed mode E bitmap shown on odd frames.</summary>
  public byte[] SecondFrame { get; init; }

  /// <summary>Colour bytes indexed by pixel value: background first, then PF0, PF1 and PF2.</summary>
  public byte[] Colors { get; init; }

  /// <summary>Averages two colours the way the flicker does, one channel at a time.</summary>
  private static void _Blend(ReadOnlySpan<byte> first, ReadOnlySpan<byte> second, Span<byte> result) {
    for (var channel = 0; channel < 3; ++channel)
      result[channel] = (byte)((first[channel] + second[channel]) >> 1);
  }

  public static RawImage ToRawImage(InterPainterFile file) {
    var gtia = Atari8BitGraphics.CreatePalette();

    // Every pairing of the four registers is a shade the picture may use, including a register
    // with itself.
    var palette = new byte[BlendCount * 3];
    var slotOf = new int[ColorCount, ColorCount];
    var used = 0;
    for (var a = 0; a < ColorCount; ++a)
    for (var b = a; b < ColorCount; ++b) {
      _Blend(gtia.AsSpan(file.Colors[a] * 3, 3), gtia.AsSpan(file.Colors[b] * 3, 3), palette.AsSpan(used * 3, 3));
      slotOf[a, b] = slotOf[b, a] = used;
      ++used;
    }

    var firstPixels = Atari8BitGraphics.UnpackGr7(file.FirstFrame, 0, DisplayHeight);
    var secondPixels = Atari8BitGraphics.UnpackGr7(file.SecondFrame, 0, DisplayHeight);

    var pixels = new byte[DisplayWidth * DisplayHeight];
    for (var y = 0; y < DisplayHeight; ++y)
    for (var x = 0; x < DisplayWidth; ++x) {
      var source = (y * Atari8BitGraphics.Gr7Width) + (x >> 1);
      pixels[y * DisplayWidth + x] = (byte)slotOf[firstPixels[source], secondPixels[source]];
    }

    return new() {
      Width = DisplayWidth,
      Height = DisplayHeight,
      Format = PixelFormat.Indexed8,
      PixelData = pixels,
      Palette = palette,
      PaletteCount = BlendCount,
    };
  }

  public static InterPainterFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    if (image.Width != DisplayWidth || image.Height != DisplayHeight)
      throw new ArgumentException($"Expected {DisplayWidth}x{DisplayHeight} but got {image.Width}x{image.Height}.", nameof(image));

    var bgra = PixelConverter.Convert(image, PixelFormat.Bgra32);
    var gtia = Atari8BitGraphics.CreatePalette();

    // Pick the four registers from a plain four-colour reduction, then let the interlace fill in
    // the shades between them.
    var quantized = ColorQuantizer.Quantize(bgra.PixelData, DisplayWidth * DisplayHeight, ColorCount);
    var colors = new byte[ColorCount];
    for (var value = 0; value < ColorCount && value < quantized.Count; ++value)
      colors[value] = Atari8BitGraphics.FindNearestColorByte(
        gtia, quantized.Palette[value * 3], quantized.Palette[value * 3 + 1], quantized.Palette[value * 3 + 2]);

    // The ten reachable shades, each remembering which two registers produce it.
    var blends = new byte[BlendCount * 3];
    var pairs = new (byte First, byte Second)[BlendCount];
    var used = 0;
    for (var a = 0; a < ColorCount; ++a)
    for (var b = a; b < ColorCount; ++b) {
      _Blend(gtia.AsSpan(colors[a] * 3, 3), gtia.AsSpan(colors[b] * 3, 3), blends.AsSpan(used * 3, 3));
      pairs[used] = ((byte)a, (byte)b);
      ++used;
    }

    var firstPixels = new byte[Atari8BitGraphics.Gr7Width * DisplayHeight];
    var secondPixels = new byte[Atari8BitGraphics.Gr7Width * DisplayHeight];
    for (var y = 0; y < DisplayHeight; ++y)
    for (var x = 0; x < Atari8BitGraphics.Gr7Width; ++x) {
      // Both bitmaps hold 160 pixels per row, each drawn two screen pixels wide.
      var source = (y * DisplayWidth + x * 2) * 4;
      var pair = pairs[_NearestBlend(blends, bgra.PixelData[source + 2], bgra.PixelData[source + 1], bgra.PixelData[source])];
      var target = y * Atari8BitGraphics.Gr7Width + x;
      firstPixels[target] = pair.First;
      secondPixels[target] = pair.Second;
    }

    return new() {
      FirstFrame = Atari8BitGraphics.PackGr7(firstPixels, DisplayHeight),
      SecondFrame = Atari8BitGraphics.PackGr7(secondPixels, DisplayHeight),
      Colors = colors,
    };
  }

  private static int _NearestBlend(byte[] blends, byte red, byte green, byte blue) {
    var best = 0;
    var bestDistance = int.MaxValue;
    for (var i = 0; i < BlendCount; ++i) {
      int dr = blends[i * 3] - red, dg = blends[i * 3 + 1] - green, db = blends[i * 3 + 2] - blue;
      var distance = dr * dr + dg * dg + db * db;
      if (distance >= bestDistance)
        continue;

      bestDistance = distance;
      best = i;
      if (distance == 0)
        break;
    }

    return best;
  }
}
