using System;
using FileFormat.Core;

namespace FileFormat.Psp;

/// <summary>In-memory representation of a Paint Shop Pro image.</summary>
[FormatMagicBytes([0x50, 0x61, 0x69, 0x6E, 0x74, 0x20, 0x53, 0x68])]
public readonly record struct PspFile : IImageFormatReader<PspFile>, IImageToRawImage<PspFile>, IImageFromRawImage<PspFile>, IImageFormatWriter<PspFile> {

  static string IImageFormatMetadata<PspFile>.PrimaryExtension => ".psp";
  static string[] IImageFormatMetadata<PspFile>.FileExtensions => [".psp", ".pspimage"];
  static PspFile IImageFormatReader<PspFile>.FromSpan(ReadOnlySpan<byte> data) => PspReader.FromSpan(data);
  static byte[] IImageFormatWriter<PspFile>.ToBytes(PspFile file) => PspWriter.ToBytes(file);

  /// <summary>Image width in pixels.</summary>
  public int Width { get; init; }

  /// <summary>Image height in pixels.</summary>
  public int Height { get; init; }

  /// <summary>Bits per pixel (default 24 for RGB24).</summary>
  public int BitDepth { get; init; }

  /// <summary>Major version of the PSP file format.</summary>
  public ushort MajorVersion { get; init; }

  /// <summary>Minor version of the PSP file format.</summary>
  public ushort MinorVersion { get; init; }

  /// <summary>Raw RGB24 pixel data (3 bytes per pixel, row-major).</summary>
  public byte[] PixelData { get; init; }

  /// <summary>The 32-byte file magic identifying a PSP file.</summary>
  internal static readonly byte[] Magic = _BuildMagic();

  /// <summary>Block ID for General Image Attributes.</summary>
  internal const ushort BlockIdGeneralAttributes = 0x00;

  /// <summary>Block ID for Composite Image Bank.</summary>
  internal const ushort BlockIdCompositeImage = 0x24;

  private static byte[] _BuildMagic() {
    var magic = new byte[32];
    var text = "Paint Shop Pro Image File\n\x1a"u8;
    text.CopyTo(magic);
    return magic;
  }

  public static RawImage ToRawImage(PspFile file) {
    return new() {
      Width = file.Width,
      Height = file.Height,
      Format = PixelFormat.Rgb24,
      PixelData = file.PixelData[..],
    };
  }

  public static PspFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    if (image.Format != PixelFormat.Rgb24)
      throw new ArgumentException($"Expected {PixelFormat.Rgb24} but got {image.Format}.", nameof(image));

    return new() {
      Width = image.Width,
      Height = image.Height,
      BitDepth = 24,
      PixelData = image.PixelData[..],
    };
  }
}
