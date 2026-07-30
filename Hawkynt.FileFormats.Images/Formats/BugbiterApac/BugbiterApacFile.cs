using System;
using FileFormat.Core;

namespace FileFormat.BugbiterApac;

/// <summary>In-memory representation of a Bugbiter APAC239i picture (.bgp).</summary>
/// <remarks>
/// Two APAC fields shown alternately: each pairs a Graphics 9 luminance field with a Graphics 11
/// hue field on alternate scanlines, and the two whole pictures are then averaged again by the
/// display. Layering the trick on itself is what the trailing "i" means, and it doubles again the
/// number of distinct shades a machine with nine colour registers can put on screen.
/// <para/>
/// All four fields are interleaved row by row rather than stored one after another, so a field's
/// rows sit eighty bytes apart. The file also carries a text comment of its own length, which is
/// why everything after the header moves.
/// </remarks>
public readonly record struct BugbiterApacFile
  : IImageFormatReader<BugbiterApacFile>, IImageToRawImage<BugbiterApacFile> {

  /// <summary>Screen pixels across.</summary>
  public const int Width = 320;

  /// <summary>Rows. The odd count is what the format's name refers to.</summary>
  public const int Height = 239;

  /// <summary>Bytes one field's row occupies.</summary>
  public const int RowLength = Width / 8;

  /// <summary>Bytes a row of the picture occupies, since two fields interleave.</summary>
  public const int Stride = RowLength * 2;

  /// <summary>The string every file starts with.</summary>
  public const string Signature = "BUGBITER_APAC239I_PICTURE_V1.0";

  /// <summary>Offset of the comment's length.</summary>
  public const int TextLengthOffset = 37;

  /// <summary>Offset of the comment.</summary>
  public const int TextOffset = 39;

  /// <summary>Size of a file carrying no comment.</summary>
  public const int BaseFileSize = 19163;

  /// <summary>Offset of the first field's luminances, relative to the end of the comment.</summary>
  public const int FirstLuminanceOffset = 2;

  /// <summary>Offset of the second field's luminances, relative to the end of the comment.</summary>
  public const int SecondLuminanceOffset = FirstLuminanceOffset + RowLength;

  /// <summary>Offset of the second field's hues, relative to the end of the comment.</summary>
  public const int SecondHueOffset = 9564;

  /// <summary>Offset of the first field's hues, relative to the end of the comment.</summary>
  public const int FirstHueOffset = SecondHueOffset + RowLength;

  static string IImageFormatMetadata<BugbiterApacFile>.PrimaryExtension => ".bgp";
  static string[] IImageFormatMetadata<BugbiterApacFile>.FileExtensions => [".bgp"];
  static BugbiterApacFile IImageFormatReader<BugbiterApacFile>.FromSpan(ReadOnlySpan<byte> data)
    => BugbiterApacReader.FromSpan(data);
  static VideoMode[] IImageFormatMetadata<BugbiterApacFile>.VideoModes => [
    new("APAC239i", [(Width, Height)], [256])
  ];

  /// <summary>The whole file.</summary>
  public byte[] Data { get; init; }

  /// <summary>Where the picture starts, past the comment.</summary>
  public int PictureOffset { get; init; }

  public static RawImage ToRawImage(BugbiterApacFile file) {
    var data = file.Data ?? [];
    var picture = file.PictureOffset;

    var first = new byte[Width * Height];
    Atari8BitGraphics.DecodeGr9Into(
      data, picture + FirstLuminanceOffset, Stride, first, 0, Width * 2, Width, (Height + 1) / 2, 0);
    Atari8BitGraphics.BlendGr11Into(data, picture + FirstHueOffset, Stride, first, Width, Height, 1);

    var second = new byte[Width * Height];
    Atari8BitGraphics.DecodeGr9Into(
      data, picture + SecondLuminanceOffset, Stride, second, Width, Width * 2, Width, Height / 2, 0);
    Atari8BitGraphics.BlendGr11Into(data, picture + SecondHueOffset, Stride, second, Width, Height, 0);

    return new() {
      Width = Width,
      Height = Height,
      Format = PixelFormat.Rgb24,
      PixelData = FrameBlend.Average(
        Atari8BitGraphics.ApplyPalette(first), Atari8BitGraphics.ApplyPalette(second)),
    };
  }
}
