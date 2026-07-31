using System;
using FileFormat.AtariIce;
using FileFormat.Core;

namespace FileFormat.IcePcinPlus;

/// <summary>In-memory representation of an ICE PCIN+ picture (.ip2).</summary>
/// <remarks>
/// A full screen drawn with the Interlace Character Editor's technique rather than one of its
/// character sheets: a screen of character codes, two character sets, and two fields read in
/// different graphics modes — mode 12 in one and GTIA 10 in the other — so that the pair averages
/// into colours neither mode holds.
/// <para/>
/// Both fields read the same screen; what differs is which character set they draw it from and how
/// the bits of a cell are interpreted, which is why one file gives two quite different pictures.
/// </remarks>
public readonly record struct IcePcinPlusFile
  : IImageFormatReader<IcePcinPlusFile>, IImageToRawImage<IcePcinPlusFile> {

  /// <summary>Pixels across.</summary>
  public const int Width = 320;

  /// <summary>Rows.</summary>
  public const int Height = 192;

  /// <summary>Where the screen's character codes are.</summary>
  public const int ScreenOffset = 16398;

  /// <summary>Total file size.</summary>
  public const int FileSize = 17358;

  static string IImageFormatMetadata<IcePcinPlusFile>.PrimaryExtension => ".ip2";
  static string[] IImageFormatMetadata<IcePcinPlusFile>.FileExtensions => [".ip2"];
  static IcePcinPlusFile IImageFormatReader<IcePcinPlusFile>.FromSpan(ReadOnlySpan<byte> data)
    => IcePcinPlusReader.FromSpan(data);
  static VideoMode[] IImageFormatMetadata<IcePcinPlusFile>.VideoModes => [
    new("Atari 8-bit", [(Width, Height)], [256])
  ];

  /// <summary>The whole file.</summary>
  public byte[] Data { get; init; }

  /// <summary>The two fields, in display order.</summary>
  public IceField[] Fields { get; init; }

  public static RawImage ToRawImage(IcePcinPlusFile file) {
    var data = file.Data ?? [];
    var fields = file.Fields ?? [];

    var first = IceRenderer.Render(data, fields[0], Width, Height);
    var second = IceRenderer.Render(data, fields[1], Width, Height);

    return new() {
      Width = Width,
      Height = Height,
      Format = PixelFormat.Rgb24,
      PixelData = FrameBlend.Average(Atari8BitGraphics.ApplyPalette(first), Atari8BitGraphics.ApplyPalette(second)),
    };
  }
}
