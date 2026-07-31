using System;
using FileFormat.Core;

namespace FileFormat.AtariIce;

/// <summary>In-memory representation of an Interlace Character Editor picture (.ice).</summary>
/// <remarks>
/// A character set, shown as the two alternating fields the editor displayed it in. What makes the
/// format worth its own reader is that the two fields need not be in the same graphics mode: the
/// editor's whole purpose was pairing one ANTIC mode with another, or the same one under a
/// different GTIA setting, so that the two averaged into colours neither could show. There are
/// thirty-three such pairings and the first byte of the file says which.
/// <para/>
/// Version 2.0 dropped the character screen entirely: its pictures are the character set in a fixed
/// arrangement, coloured by a multiplier that changes down the picture, and the two fields take
/// that multiplier in different orders.
/// </remarks>
public readonly record struct AtariIceFile
  : IImageFormatReader<AtariIceFile>, IImageToRawImage<AtariIceFile> {

  static string IImageFormatMetadata<AtariIceFile>.PrimaryExtension => ".ice";
  static string[] IImageFormatMetadata<AtariIceFile>.FileExtensions => [".ice", ".icn"];
  static AtariIceFile IImageFormatReader<AtariIceFile>.FromSpan(ReadOnlySpan<byte> data)
    => AtariIceReader.FromSpan(data);
  static VideoMode[] IImageFormatMetadata<AtariIceFile>.VideoModes => [
    new("Atari 8-bit", [(256, 128), (256, 256), (256, 288), (320, 192)], [256])
  ];

  /// <summary>The whole file.</summary>
  public byte[] Data { get; init; }

  /// <summary>Pixels across.</summary>
  public int Width { get; init; }

  /// <summary>Rows.</summary>
  public int Height { get; init; }

  /// <summary>The two fields, in display order.</summary>
  public IceField[] Fields { get; init; }

  public static RawImage ToRawImage(AtariIceFile file) {
    var data = file.Data ?? [];
    var fields = file.Fields ?? [];

    var first = IceRenderer.Render(data, fields[0], file.Width, file.Height);
    var second = IceRenderer.Render(data, fields[1], file.Width, file.Height);

    return new() {
      Width = file.Width,
      Height = file.Height,
      Format = PixelFormat.Rgb24,
      PixelData = FrameBlend.Average(Atari8BitGraphics.ApplyPalette(first), Atari8BitGraphics.ApplyPalette(second)),
    };
  }
}
