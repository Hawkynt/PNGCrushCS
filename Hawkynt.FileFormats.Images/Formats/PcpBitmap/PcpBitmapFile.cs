using System;
using System.Buffers.Binary;
using FileFormat.Core;

namespace FileFormat.PcpBitmap;

/// <summary>In-memory representation of a .pcp bitmap.</summary>
/// <remarks>
/// Named after its extension, because that is what is known about it. There is a sample and a tool
/// that draws it, and the layout below reproduces that drawing on every pixel; nothing here says
/// whose format it is.
/// <para/>
/// Six bytes of header, then one bit a pixel, most significant bit leftmost, rows padded to a byte.
/// The first two words are the largest coordinates rather than the sizes — 458 and 279 for a picture
/// 459 by 280 — which is worth stating because reading them as sizes loses the last row and column
/// and shifts everything after the first row.
/// <para/>
/// <c>.pcp</c> was claimed only by Atari Grafik, which takes one fixed length and refused this.
/// </remarks>
public readonly record struct PcpBitmapFile
  : IImageFormatReader<PcpBitmapFile>, IImageToRawImage<PcpBitmapFile>,
    IImageFromRawImage<PcpBitmapFile>, IImageFormatWriter<PcpBitmapFile> {

  /// <summary>Two words of largest coordinate, then two bytes this does not use.</summary>
  public const int HeaderSize = 6;

  public const int ColorCount = 2;

  static string IImageFormatMetadata<PcpBitmapFile>.PrimaryExtension => ".pcp";
  static string[] IImageFormatMetadata<PcpBitmapFile>.FileExtensions => [".pcp"];
  static PcpBitmapFile IImageFormatReader<PcpBitmapFile>.FromSpan(ReadOnlySpan<byte> data) => PcpBitmapReader.FromSpan(data);
  static byte[] IImageFormatWriter<PcpBitmapFile>.ToBytes(PcpBitmapFile file) => PcpBitmapWriter.ToBytes(file);
  static VideoMode[] IImageFormatMetadata<PcpBitmapFile>.VideoModes => [
    new("Default", [(new IntegerRange(1, ushort.MaxValue), new IntegerRange(1, ushort.MaxValue))], [ColorCount])
  ];

  public int Width { get; init; }

  public int Height { get; init; }

  /// <summary>The two bytes after the coordinates, kept so writing one back preserves them.</summary>
  public ushort Trailer { get; init; }

  public byte[] PixelData { get; init; }

  public static RawImage ToRawImage(PcpBitmapFile file)
    => MonochromePage.Decode(file.PixelData ?? [], file.Width, file.Height, inkIsWhite: true);

  public static PcpBitmapFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    if (image.Width < 1 || image.Height < 1)
      throw new ArgumentException($"A picture needs at least one pixel; got {image.Width}x{image.Height}.", nameof(image));

    return new() {
      Width = image.Width,
      Height = image.Height,
      Trailer = 1,
      PixelData = MonochromePage.Encode(image, image.Width, image.Height, inkIsWhite: true),
    };
  }
}
