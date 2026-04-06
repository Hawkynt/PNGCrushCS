using System;
using FileFormat.Core;

namespace FileFormat.Jbig2;

/// <summary>In-memory representation of a JBIG2 (ITU-T T.88) bi-level image.</summary>
[FormatMagicBytes([0x97, 0x4A, 0x42, 0x32])]
public readonly record struct Jbig2File : IImageFormatReader<Jbig2File>, IImageToRawImage<Jbig2File>, IImageFromRawImage<Jbig2File>, IImageFormatWriter<Jbig2File> {

  static string IImageFormatMetadata<Jbig2File>.PrimaryExtension => ".jb2";
  static string[] IImageFormatMetadata<Jbig2File>.FileExtensions => [".jb2", ".jbig2"];
  static Jbig2File IImageFormatReader<Jbig2File>.FromSpan(ReadOnlySpan<byte> data) => Jbig2Reader.FromSpan(data);
  static FormatCapability IImageFormatMetadata<Jbig2File>.Capabilities => FormatCapability.MonochromeOnly;
  static byte[] IImageFormatWriter<Jbig2File>.ToBytes(Jbig2File file) => Jbig2Writer.ToBytes(file);

  /// <summary>Image width in pixels.</summary>
  public int Width { get; init; }

  /// <summary>Image height in pixels.</summary>
  public int Height { get; init; }

  /// <summary>1bpp packed pixel data (MSB first, ceil(width/8) bytes per row). Bit=1 means black, bit=0 means white.</summary>
  public byte[] PixelData { get; init; }

  /// <summary>The parsed segments from the JBIG2 file.</summary>
  public Jbig2Segment[] Segments { get; init; }

  private static readonly byte[] _BlackWhitePalette = [0, 0, 0, 255, 255, 255];

  public static RawImage ToRawImage(Jbig2File file) {

    return new() {
      Width = file.Width,
      Height = file.Height,
      Format = PixelFormat.Indexed1,
      PixelData = file.PixelData[..],
      Palette = _BlackWhitePalette[..],
      PaletteCount = 2,
    };
  }

  public static Jbig2File FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    if (image.Format != PixelFormat.Indexed1)
      throw new ArgumentException("RawImage must use PixelFormat.Indexed1.", nameof(image));

    return new() {
      Width = image.Width,
      Height = image.Height,
      PixelData = image.PixelData[..],
    };
  }
}
