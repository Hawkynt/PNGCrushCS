using System;
using FileFormat.Core;

namespace FileFormat.It01;

/// <summary>In-memory representation of a picture opening with "IT01" (.fit).</summary>
/// <remarks>
/// Named after its magic because that is what is known about it. One sample exists, one tool reads
/// it, and the layout below reproduces that tool's rendering on every pixel — but nothing here
/// establishes whose format it is or what the fields between the size and the data offset mean.
/// <para/>
/// A header of big-endian integers: the width, the height, then a run of fields, and last the
/// offset the picture starts at. The picture itself is ordinary interleaved bytes, top row first.
/// The sample states 512 by 512 in three bands at offset 56, which is exactly its length.
/// <para/>
/// <c>.fit</c> was claimed only by FITS, which is a different format under the same name and
/// refuses this one for lacking a SIMPLE keyword.
/// </remarks>
public readonly record struct It01File
  : IImageFormatReader<It01File>, IImageToRawImage<It01File>,
    IImageFromRawImage<It01File>, IImageFormatWriter<It01File> {

  /// <summary>The four bytes every one of these opens with.</summary>
  public static ReadOnlySpan<byte> Magic => [(byte)'I', (byte)'T', (byte)'0', (byte)'1'];

  /// <summary>The header this writes, which is the one the sample carries.</summary>
  public const int DefaultDataOffset = 56;

  internal const int WidthAt = 4, HeightAt = 8, BandsAt = 16, DataOffsetAt = 52;

  static string IImageFormatMetadata<It01File>.PrimaryExtension => ".fit";
  static string[] IImageFormatMetadata<It01File>.FileExtensions => [".fit"];
  static It01File IImageFormatReader<It01File>.FromSpan(ReadOnlySpan<byte> data) => It01Reader.FromSpan(data);
  static byte[] IImageFormatWriter<It01File>.ToBytes(It01File file) => It01Writer.ToBytes(file);
  static VideoMode[] IImageFormatMetadata<It01File>.VideoModes => [
    new("Default", [(IntegerRange.Any, IntegerRange.Any)])
  ];

  public int Width { get; init; }

  public int Height { get; init; }

  /// <summary>How many bands a pixel has: three for colour, one for grey.</summary>
  public int Bands { get; init; }

  public byte[] PixelData { get; init; }

  public static RawImage ToRawImage(It01File file) => new() {
    Width = file.Width,
    Height = file.Height,
    Format = file.Bands == 1 ? PixelFormat.Gray8 : PixelFormat.Rgb24,
    PixelData = (file.PixelData ?? [])[..],
  };

  public static It01File FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    image = image.EnsureAnyFormat(PixelFormat.Rgb24, PixelFormat.Gray8);

    return new() {
      Width = image.Width,
      Height = image.Height,
      Bands = image.Format == PixelFormat.Gray8 ? 1 : 3,
      PixelData = image.PixelData[..],
    };
  }
}
