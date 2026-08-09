using System;
using System.IO;
using FileFormat.Core;
using FileFormat.Jpeg;

namespace FileFormat.DispThumbnail;

/// <summary>In-memory representation of a Thumbnail file (.tnl).</summary>
/// <remarks>
/// Seven letters reading <c>DISPTNL</c> and then one more that decides which of two files this is.
/// Where that byte is <c>5</c> the rest of the header is skipped and the picture is an ordinary
/// JPEG beginning at 168. Where it is anything else the file states its own size — two little-endian
/// longs at 16 and 20 — and the picture is one byte a pixel from 168, a grey with no colour table
/// anywhere in it and none looked for.
/// <para/>
/// Both readings come from the reader XnView carries for the name. A grey thumbnail built here to
/// that layout is read by it at the size it was built with, and the bytes it hands back are the ones
/// written.
/// </remarks>
public readonly record struct DispThumbnailFile
  : IImageFormatReader<DispThumbnailFile>, IImageToRawImage<DispThumbnailFile> {

  /// <summary>The seven letters a thumbnail opens with.</summary>
  public static ReadOnlySpan<byte> Magic => "DISPTNL"u8;

  /// <summary>The byte after them, which says the picture is a JPEG rather than a grey.</summary>
  public const byte JpegMarker = (byte)'5';

  /// <summary>Where the grey form states its size, each as a little-endian long.</summary>
  internal const int WidthAt = 16, HeightAt = 20;

  /// <summary>Where the picture begins, in both forms.</summary>
  public const int PictureOffset = 0xA8;

  /// <summary>The largest side accepted, the header stating a long that nothing bounds.</summary>
  public const int MaximumSide = 65535;

  static string IImageFormatMetadata<DispThumbnailFile>.PrimaryExtension => ".tnl";
  static string[] IImageFormatMetadata<DispThumbnailFile>.FileExtensions => [".tnl"];
  static DispThumbnailFile IImageFormatReader<DispThumbnailFile>.FromSpan(ReadOnlySpan<byte> data) => DispThumbnailReader.FromSpan(data);
  static VideoMode[] IImageFormatMetadata<DispThumbnailFile>.VideoModes => [
    new("Default", [(IntegerRange.Any, IntegerRange.Any)], [256, 16777216])
  ];

  /// <summary>How wide the picture is.</summary>
  public int Width { get; init; }

  /// <summary>How tall it is.</summary>
  public int Height { get; init; }

  /// <summary>The greys, one byte a pixel, top row first — empty where the file carries a JPEG.</summary>
  public byte[] PixelData { get; init; }

  /// <summary>The JPEG the file carries, where it carries one.</summary>
  public byte[] Embedded { get; init; }

  public static RawImage ToRawImage(DispThumbnailFile file) {
    if (file.Embedded != null)
      return JpegFile.ToRawImage(JpegReader.FromBytes(file.Embedded));

    if (file.PixelData == null)
      throw new InvalidOperationException("No picture was read.");

    var rgb = new byte[(long)file.Width * file.Height * 3];
    for (var i = 0; i < file.PixelData.Length; ++i) {
      var grey = file.PixelData[i];
      rgb[i * 3] = grey;
      rgb[i * 3 + 1] = grey;
      rgb[i * 3 + 2] = grey;
    }

    return new() { Width = file.Width, Height = file.Height, Format = PixelFormat.Rgb24, PixelData = rgb };
  }
}
