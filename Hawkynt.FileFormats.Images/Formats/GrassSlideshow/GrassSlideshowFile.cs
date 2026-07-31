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
  : IImageFormatReader<GrassSlideshowFile>, IImageToRawImage<GrassSlideshowFile> {

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

  static string IImageFormatMetadata<GrassSlideshowFile>.PrimaryExtension => ".hpm";
  static string[] IImageFormatMetadata<GrassSlideshowFile>.FileExtensions => [".hpm"];
  static GrassSlideshowFile IImageFormatReader<GrassSlideshowFile>.FromSpan(ReadOnlySpan<byte> data)
    => GrassSlideshowReader.FromSpan(data);
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
}
