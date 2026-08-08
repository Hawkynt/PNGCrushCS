using System;
using System.Buffers.Binary;
using FileFormat.Core;

namespace FileFormat.CelGrey;

/// <summary>In-memory representation of a four-bit greyscale .cel.</summary>
/// <remarks>
/// Four bytes of header — the width and the height as little-endian words — then four bits a pixel,
/// high nibble leftmost, rows padded to a whole byte. The value is the shade: index times 17, so 15
/// is white.
/// <para/>
/// Named after the extension because that is what is known. <c>.cel</c> is held by three unrelated
/// formats here already — the Atari one, Autodesk's and KiSS — and all three refuse this, each for
/// a magic it does not carry. Nothing establishes whose this is; what there is is a sample, a tool
/// that draws it, and a layout that reproduces that drawing on every pixel.
/// </remarks>
public readonly record struct CelGreyFile
  : IImageFormatReader<CelGreyFile>, IImageToRawImage<CelGreyFile>,
    IImageFromRawImage<CelGreyFile>, IImageFormatWriter<CelGreyFile> {

  /// <summary>The width and the height, a word apiece.</summary>
  public const int HeaderSize = 4;

  /// <summary>Shades a picture holds.</summary>
  public const int ColorCount = 16;

  static string IImageFormatMetadata<CelGreyFile>.PrimaryExtension => ".cel";
  static string[] IImageFormatMetadata<CelGreyFile>.FileExtensions => [".cel"];
  static CelGreyFile IImageFormatReader<CelGreyFile>.FromSpan(ReadOnlySpan<byte> data) => CelGreyReader.FromSpan(data);
  static byte[] IImageFormatWriter<CelGreyFile>.ToBytes(CelGreyFile file) => CelGreyWriter.ToBytes(file);
  static VideoMode[] IImageFormatMetadata<CelGreyFile>.VideoModes => [
    new("Default", [(new IntegerRange(1, ushort.MaxValue), new IntegerRange(1, ushort.MaxValue))], [ColorCount])
  ];

  public int Width { get; init; }

  public int Height { get; init; }

  /// <summary>Four bits a pixel.</summary>
  public byte[] PixelData { get; init; }

  /// <summary>Bytes one row takes, two pixels to the byte.</summary>
  public static int BytesPerRow(int width) => (width + 1) / 2;

  public static RawImage ToRawImage(CelGreyFile file) {
    var data = file.PixelData ?? [];
    var stride = BytesPerRow(file.Width);
    var grey = new byte[file.Width * file.Height];

    for (var y = 0; y < file.Height; ++y)
    for (var x = 0; x < file.Width; ++x) {
      var at = y * stride + (x >> 1);
      var v = at < data.Length ? ((x & 1) == 0 ? data[at] >> 4 : data[at] & 0x0F) : 0;
      grey[y * file.Width + x] = (byte)(v * 17);
    }

    return new() { Width = file.Width, Height = file.Height, Format = PixelFormat.Gray8, PixelData = grey };
  }

  public static CelGreyFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    var grey = PixelConverter.Convert(image, PixelFormat.Gray8).PixelData;
    var stride = BytesPerRow(image.Width);
    var data = new byte[stride * image.Height];

    for (var y = 0; y < image.Height; ++y)
    for (var x = 0; x < image.Width; ++x) {
      var level = (grey[y * image.Width + x] * 15 + 127) / 255;
      var at = y * stride + (x >> 1);
      data[at] |= (byte)((x & 1) == 0 ? level << 4 : level);
    }

    return new() { Width = image.Width, Height = image.Height, PixelData = data };
  }
}
