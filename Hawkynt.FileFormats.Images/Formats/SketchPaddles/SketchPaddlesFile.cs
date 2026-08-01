using System;
using FileFormat.Core;

namespace FileFormat.SketchPaddles;

/// <summary>In-memory representation of a Sketch-PadDles picture (.skp).</summary>
/// <remarks>
/// A bare Graphics 15 screen with no header and no palette — the file is nothing but the bitmap,
/// and the four colours are the ones the program itself always worked in. That is why it can be
/// exactly 7680 bytes and still be a complete picture: everything else about it was fixed by the
/// editor rather than chosen by the artist.
/// </remarks>
public readonly record struct SketchPaddlesFile
  : IImageFormatReader<SketchPaddlesFile>, IImageToRawImage<SketchPaddlesFile>,
    IImageFromRawImage<SketchPaddlesFile>, IImageFormatWriter<SketchPaddlesFile> {

  /// <summary>Screen pixels across; each of the 160 logical pixels is drawn two wide.</summary>
  public const int Width = 320;

  /// <summary>Rows.</summary>
  public const int Height = 192;

  /// <summary>Bytes one row occupies.</summary>
  public const int Stride = Width / 8;

  /// <summary>Total file size.</summary>
  public const int FileSize = Stride * Height;

  /// <summary>The registers Sketch-PadDles worked in: background, PF0, PF1 and PF2.</summary>
  public static ReadOnlySpan<byte> Registers => [38, 40, 0, 12];

  static string IImageFormatMetadata<SketchPaddlesFile>.PrimaryExtension => ".skp";
  static string[] IImageFormatMetadata<SketchPaddlesFile>.FileExtensions => [".skp"];
  static SketchPaddlesFile IImageFormatReader<SketchPaddlesFile>.FromSpan(ReadOnlySpan<byte> data)
    => SketchPaddlesReader.FromSpan(data);
  static byte[] IImageFormatWriter<SketchPaddlesFile>.ToBytes(SketchPaddlesFile file)
    => SketchPaddlesWriter.ToBytes(file);
  static VideoMode[] IImageFormatMetadata<SketchPaddlesFile>.VideoModes => [
    new("Sketch-PadDles", [(Width, Height)], [4])
  ];

  /// <summary>The bitmap.</summary>
  public byte[] Data { get; init; }

  public static RawImage ToRawImage(SketchPaddlesFile file) => new() {
    Width = Width,
    Height = Height,
    Format = PixelFormat.Rgb24,
    PixelData = Atari8BitGraphics.DecodeGr15Frame(file.Data ?? [], 0, Stride, Width, Height, Registers),
  };

  /// <summary>Builds a picture in the four colours the editor worked in.</summary>
  /// <remarks>
  /// Nothing about the colours is stored: the editor fixed all four, which is why a file can be
  /// exactly 7680 bytes and still be a complete picture. That leaves only the choice of which of
  /// the four each logical pixel takes.
  /// <para/>
  /// A logical pixel is two screen pixels wide, so it is read at the left one of the pair rather
  /// than averaged — the hardware cannot show anything between them, and averaging would only
  /// invent a colour to be quantised away again.
  /// </remarks>
  public static SketchPaddlesFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);

    var rgb = image.SampleTo(Width, Height);
    var data = new byte[FileSize];

    for (var y = 0; y < Height; ++y)
    for (var column = 0; column < Stride; ++column) {
      var value = 0;
      for (var pixel = 0; pixel < 4; ++pixel) {
        var at = (y * Width + column * 8 + pixel * 2) * 3;
        value |= _Nearest(rgb.PixelData, at) << (6 - pixel * 2);
      }

      data[y * Stride + column] = (byte)value;
    }

    return new() { Data = data };
  }

  /// <summary>Which of the four registers a pixel is closest to.</summary>
  private static int _Nearest(ReadOnlySpan<byte> rgb, int pixel) {
    var gtia = Atari8BitGraphics.Palette;
    var best = 0;
    var bestCost = long.MaxValue;

    for (var register = 0; register < Registers.Length; ++register) {
      // The low bit of a register is not a colour: the hardware ignores it in this mode.
      var entry = (Registers[register] & 254) * 3;
      long dr = rgb[pixel] - gtia[entry];
      long dg = rgb[pixel + 1] - gtia[entry + 1];
      long db = rgb[pixel + 2] - gtia[entry + 2];
      var cost = dr * dr * 77 + dg * dg * 150 + db * db * 29;

      if (cost >= bestCost)
        continue;

      bestCost = cost;
      best = register;
    }

    return best;
  }
}
