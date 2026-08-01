using System;
using FileFormat.Core;

namespace FileFormat.GrassSlideshow;

/// <summary>In-memory representation of a Grass' Slideshow picture (.hpm).</summary>
/// <remarks>
/// A packed Graphics 15 screen whose colours are not stored at all. One byte after the picture
/// picks from a handful of register sets the slideshow program had built in — so a file names a
/// palette rather than carrying one, and a byte that names none falls back to a plain grey ramp.
/// <para/>
/// One of the sets means two different things depending on how long the file is, which is the kind
/// of thing that only makes sense in a program that shipped with its own pictures.
/// </remarks>
public readonly record struct GrassSlideshowFile
  : IImageFormatReader<GrassSlideshowFile>, IImageToRawImage<GrassSlideshowFile>,
    IImageFromRawImage<GrassSlideshowFile>, IImageFormatWriter<GrassSlideshowFile> {

  /// <summary>Screen pixels across.</summary>
  public const int Width = 320;

  /// <summary>Rows.</summary>
  public const int Height = 192;

  /// <summary>Bytes one row occupies.</summary>
  public const int Stride = Width / 8;

  /// <summary>Size of the screen a file unpacks to.</summary>
  public const int ScreenSize = Stride * Height;

  /// <summary>The file length at which one register set means something else.</summary>
  public const int ShortFileSize = 3494;

  /// <summary>A byte naming no built-in set, which falls back to a plain ramp.</summary>
  public const byte UnnamedRegisterSet = 0;

  /// <summary>The registers a file that names no set is drawn with.</summary>
  public static ReadOnlySpan<byte> FallbackRegisters => [0, 4, 8, 12];

  static string IImageFormatMetadata<GrassSlideshowFile>.PrimaryExtension => ".hpm";
  static string[] IImageFormatMetadata<GrassSlideshowFile>.FileExtensions => [".hpm"];
  static GrassSlideshowFile IImageFormatReader<GrassSlideshowFile>.FromSpan(ReadOnlySpan<byte> data)
    => GrassSlideshowReader.FromSpan(data);
  static byte[] IImageFormatWriter<GrassSlideshowFile>.ToBytes(GrassSlideshowFile file)
    => GrassSlideshowWriter.ToBytes(file);
  static VideoMode[] IImageFormatMetadata<GrassSlideshowFile>.VideoModes => [
    new("Slideshow", [(Width, Height)], [4])
  ];

  /// <summary>The unpacked screen.</summary>
  public byte[] ScreenData { get; init; }

  /// <summary>The registers the named set resolved to: background, PF0, PF1 and PF2.</summary>
  public byte[] Registers { get; init; }

  public static RawImage ToRawImage(GrassSlideshowFile file) => new() {
    Width = Width,
    Height = Height,
    Format = PixelFormat.Rgb24,
    PixelData = Atari8BitGraphics.DecodeGr15Frame(
      file.ScreenData ?? [], 0, Stride, Width, Height, file.Registers ?? []),
  };

  /// <summary>Builds a screen in the four registers a file naming no set is drawn with.</summary>
  /// <remarks>
  /// The colours are not in the file: a byte after the picture names one of the sets the slideshow
  /// program shipped with. Naming one would recolour the picture to whatever that set holds, so
  /// none is named and the encoding is done against the fallback the reader then applies — the
  /// picture and its colours agree, which is the only thing the format lets a writer guarantee.
  /// </remarks>
  public static GrassSlideshowFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);

    var rgb = image.SampleTo(Width, Height);
    var gtia = Atari8BitGraphics.Palette;
    var screen = new byte[ScreenSize];

    for (var y = 0; y < Height; ++y)
    for (var column = 0; column < Stride; ++column) {
      var value = 0;
      for (var pixel = 0; pixel < 4; ++pixel) {
        var at = (y * Width + column * 8 + pixel * 2) * 3;

        var best = 0;
        var bestCost = long.MaxValue;
        for (var register = 0; register < FallbackRegisters.Length; ++register) {
          // The low bit of a register is not a colour in this mode.
          var entry = (FallbackRegisters[register] & 254) * 3;
          long dr = rgb.PixelData[at] - gtia[entry];
          long dg = rgb.PixelData[at + 1] - gtia[entry + 1];
          long db = rgb.PixelData[at + 2] - gtia[entry + 2];
          var cost = dr * dr * 77 + dg * dg * 150 + db * db * 29;

          if (cost >= bestCost)
            continue;

          bestCost = cost;
          best = register;
        }

        value |= best << (6 - pixel * 2);
      }

      screen[y * Stride + column] = (byte)value;
    }

    return new() { ScreenData = screen, Registers = FallbackRegisters.ToArray() };
  }
}
