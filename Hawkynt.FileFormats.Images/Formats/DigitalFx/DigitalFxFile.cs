using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.DigitalFx;

/// <summary>In-memory representation of a Digital F/X picture (.tdim).</summary>
/// <remarks>
/// Four bytes reading <c>00 02 00 20</c>, four more that nothing reads, the size as two big-endian
/// words with the height first, and a big-endian long giving where the picture begins. The picture
/// is four bytes a pixel run-length coded: a control byte that is not negative repeats the four
/// bytes that follow it that many times plus one, and one that is negative takes the low seven bits
/// plus one as a count of whole pixels to copy. Runs carry on across the end of a row into the next.
/// <para/>
/// The first of the four bytes is not drawn — XnView reports four components and hands back the
/// second, third and fourth as red, green and blue — so the pixel is stored alpha first. That was
/// settled by handing it a picture whose four channels were all different and reading back which
/// three came out.
/// </remarks>
public readonly record struct DigitalFxFile
  : IImageFormatReader<DigitalFxFile>, IImageToRawImage<DigitalFxFile> {

  /// <summary>The four bytes every one of these opens with.</summary>
  public static ReadOnlySpan<byte> Magic => [0x00, 0x02, 0x00, 0x20];

  /// <summary>Where the size stands, height first, each as a big-endian word.</summary>
  internal const int HeightAt = 8, WidthAt = 10;

  /// <summary>Where the long giving the start of the picture stands.</summary>
  internal const int PictureOffsetAt = 12;

  /// <summary>The largest side the reader accepts.</summary>
  public const int MaximumSide = 32000;

  /// <summary>Bytes one stored pixel takes.</summary>
  public const int BytesPerPixel = 4;

  static string IImageFormatMetadata<DigitalFxFile>.PrimaryExtension => ".tdim";
  static string[] IImageFormatMetadata<DigitalFxFile>.FileExtensions => [".tdim"];
  static DigitalFxFile IImageFormatReader<DigitalFxFile>.FromSpan(ReadOnlySpan<byte> data) => DigitalFxReader.FromSpan(data);
  static VideoMode[] IImageFormatMetadata<DigitalFxFile>.VideoModes => [
    new("Default", [(IntegerRange.Any, IntegerRange.Any)], [16777216])
  ];

  /// <summary>How wide the picture is.</summary>
  public int Width { get; init; }

  /// <summary>How tall it is.</summary>
  public int Height { get; init; }

  /// <summary>The pixels already unpacked, four bytes each, alpha first, top row first.</summary>
  public byte[] PixelData { get; init; }

  public static RawImage ToRawImage(DigitalFxFile file) {
    if (file.PixelData == null)
      throw new InvalidOperationException("No picture was read.");

    var count = file.Width * file.Height;
    var rgb = new byte[(long)count * 3];
    for (var i = 0; i < count; ++i) {
      rgb[i * 3] = file.PixelData[i * BytesPerPixel + 1];
      rgb[i * 3 + 1] = file.PixelData[i * BytesPerPixel + 2];
      rgb[i * 3 + 2] = file.PixelData[i * BytesPerPixel + 3];
    }

    return new() { Width = file.Width, Height = file.Height, Format = PixelFormat.Rgb24, PixelData = rgb };
  }
}
