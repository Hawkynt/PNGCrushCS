using System;
using System.Text;
using FileFormat.Core;

namespace FileFormat.AtariGraphicsStudio;

/// <summary>In-memory representation of an Atari Graphics Studio picture (.ags).</summary>
/// <remarks>
/// Two unrelated screens behind one header, chosen by a mode byte. Both spend their pixels on
/// height rather than width, which is what the editor was for: a picture meant to be redisplayed
/// under program control rather than shown once.
/// <para/>
/// Mode 11 stores two Graphics 15 fields with a set of colour registers each and interleaves them
/// by scanline, so alternate rows draw from different colours — not two frames blended, but one
/// picture with twice the palette down its height. Mode 19 stores a Graphics 9 luminance field and
/// draws every row of it four times, trading vertical resolution for a file a quarter the size.
/// </remarks>
public readonly record struct AtariGraphicsStudioFile
  : IImageFormatReader<AtariGraphicsStudioFile>, IImageToRawImage<AtariGraphicsStudioFile>,
    IImageFromRawImage<AtariGraphicsStudioFile>, IImageFormatWriter<AtariGraphicsStudioFile> {

  /// <summary>The text every file starts with.</summary>
  public const string Signature = "AGS";

  /// <summary>Offset of the colour registers.</summary>
  public const int ColorsOffset = 7;

  /// <summary>Offset of the bitmap.</summary>
  public const int BitmapOffset = 16;

  /// <summary>The mode byte of the two-field Graphics 15 form.</summary>
  public const byte InterleavedMode = 11;

  /// <summary>The mode byte of the quadrupled Graphics 9 form.</summary>
  public const byte QuadrupledMode = 19;

  static string IImageFormatMetadata<AtariGraphicsStudioFile>.PrimaryExtension => ".ags";
  static string[] IImageFormatMetadata<AtariGraphicsStudioFile>.FileExtensions => [".ags"];
  static AtariGraphicsStudioFile IImageFormatReader<AtariGraphicsStudioFile>.FromSpan(ReadOnlySpan<byte> data)
    => AtariGraphicsStudioReader.FromSpan(data);
  static byte[] IImageFormatWriter<AtariGraphicsStudioFile>.ToBytes(AtariGraphicsStudioFile file)
    => AtariGraphicsStudioWriter.ToBytes(file);
  static VideoMode[] IImageFormatMetadata<AtariGraphicsStudioFile>.VideoModes => [
    new("Atari Graphics Studio", [(IntegerRange.Any, IntegerRange.Any)], [256])
  ];

  /// <summary>The whole file.</summary>
  public byte[] Data { get; init; }

  /// <summary>Which of the two screens the file holds.</summary>
  public byte Mode { get; init; }

  /// <summary>Pixels across.</summary>
  public int Width { get; init; }

  /// <summary>Rows.</summary>
  public int Height { get; init; }

  public static RawImage ToRawImage(AtariGraphicsStudioFile file) {
    var data = file.Data ?? [];
    int width = file.Width, height = file.Height;
    var frame = new byte[width * height];

    if (file.Mode == InterleavedMode) {
      var stride = width >> 3;
      Atari8BitGraphics.DecodeGr15Into(
        data, BitmapOffset, stride, frame, 0, width * 2, width, height / 2,
        Atari8BitGraphics.ReadPf012Bak(data, ColorsOffset));
      Atari8BitGraphics.DecodeGr15Into(
        data, BitmapOffset + (width * height >> 4), stride, frame, width, width * 2, width, height / 2,
        Atari8BitGraphics.ReadPf012Bak(data, ColorsOffset + 4));
    } else
      // Every stored row is drawn four times, which is where the mode's height comes from.
      for (var y = 0; y < 4; ++y)
        Atari8BitGraphics.DecodeGr9Into(
          data, BitmapOffset, width >> 3, frame, y * width, width * 4, width, height / 4, 0);

    return new() {
      Width = width,
      Height = height,
      Format = PixelFormat.Rgb24,
      PixelData = Atari8BitGraphics.ApplyPalette(frame),
    };
  }

  /// <summary>Builds the quadrupled Graphics 9 form, which is the one that carries a whole picture.</summary>
  /// <remarks>
  /// Of the two screens behind this header, the interleaved one gives alternate scanlines different
  /// colour registers — a picture with twice the palette down its height, and a choice about which
  /// rows get which colours that only the artist can make. The quadrupled one is a plain luminance
  /// field drawn four times, so it is what a picture converts into without inventing intent.
  /// <para/>
  /// Sixteen luminances of a single hue, one nibble covering four screen pixels. Those four are
  /// read at the leftmost rather than averaged: they are one pixel as far as the hardware is
  /// concerned, and averaging would only blur its edge before quantising it back.
  /// </remarks>
  public static AtariGraphicsStudioFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);

    var stored = Math.Clamp(image.Width / 8, 1, 255);
    var width = stored << 3;
    var rows = Math.Clamp(image.Height / 4, 1, 0xFFFF);
    var height = rows << 2;

    var rgb = image.SampleTo(width, height);

    // Both forms reserve room for two fields. The interleaved one fills both; this one leaves the
    // second half unused, and the length is what a reader checks, so it is written all the same.
    var data = new byte[BitmapOffset + stored * rows * 2];

    Encoding.ASCII.GetBytes(Signature).CopyTo(data, 0);
    data[3] = QuadrupledMode;
    data[4] = (byte)stored;
    data[5] = (byte)rows;
    data[6] = (byte)(rows >> 8);

    for (var row = 0; row < rows; ++row)
    for (var x = 0; x < width; x += 4) {
      // Every stored row is drawn four times, so it is read from the first of the four it covers.
      var at = (row * 4 * width + x) * 3;
      var luminance = _Nearest(rgb.PixelData, at);

      data[BitmapOffset + row * stored + (x >> 3)] |= (byte)(luminance << (~x & 4));
    }

    return new() { Data = data, Mode = QuadrupledMode, Width = width, Height = height };
  }

  /// <summary>Which of the sixteen luminances a pixel is closest to.</summary>
  private static int _Nearest(ReadOnlySpan<byte> rgb, int pixel) {
    var gtia = Atari8BitGraphics.Palette;
    var best = 0;
    var bestCost = long.MaxValue;

    for (var luminance = 0; luminance < 16; ++luminance) {
      var entry = luminance * 3;
      long dr = rgb[pixel] - gtia[entry], dg = rgb[pixel + 1] - gtia[entry + 1], db = rgb[pixel + 2] - gtia[entry + 2];
      var cost = dr * dr * 77 + dg * dg * 150 + db * db * 29;

      if (cost >= bestCost)
        continue;

      bestCost = cost;
      best = luminance;
    }

    return best;
  }
}
