using System;
using FileFormat.Core;

namespace FileFormat.Psp;

/// <summary>In-memory representation of a Paint Shop Pro image.</summary>
[FormatMagicBytes([0x50, 0x61, 0x69, 0x6E, 0x74, 0x20, 0x53, 0x68])]
public readonly record struct PspFile : IImageFormatReader<PspFile>, IImageToRawImage<PspFile>, IImageFromRawImage<PspFile>, IImageFormatWriter<PspFile> {

  static string IImageFormatMetadata<PspFile>.PrimaryExtension => ".psp";

  /// <summary>Every name Paint Shop Pro saves this same block layout under.</summary>
  /// <remarks>
  /// A tube, a brush, a frame, a mask and a template are all one format: the file header is the same
  /// string, the blocks are the same blocks, and what tells them apart is a data block beside the
  /// picture rather than any difference in how the picture is stored. Naming them separately would
  /// have meant five readers of one format.
  /// </remarks>
  static string[] IImageFormatMetadata<PspFile>.FileExtensions =>
    [".psp", ".pspimage", ".tub", ".psptube", ".pspbrush", ".pspframe", ".pspmask", ".pspt"];
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

  /// <summary>Whether the picture carries the layer transparency mask alongside its colour.</summary>
  /// <remarks>
  /// A tube is transparent everywhere except where it paints, so drawing one without its mask draws
  /// the rectangle it was cut from rather than the shape.
  /// </remarks>
  public bool HasAlpha { get; init; }

  /// <summary>Raw pixel data, row-major: RGB24, or RGBA32 when <see cref="HasAlpha"/> is set.</summary>
  public byte[] PixelData { get; init; }

  /// <summary>The 32-byte file magic identifying a PSP file.</summary>
  internal static readonly byte[] Magic = _BuildMagic();

  /// <summary>Block ID for General Image Attributes.</summary>
  internal const ushort BlockIdGeneralAttributes = 0x00;

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
      Format = file.HasAlpha ? PixelFormat.Rgba32 : PixelFormat.Rgb24,
      PixelData = file.PixelData[..],
    };
  }

  public static PspFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    var hasAlpha = image.HasAlpha;
    image = image.EnsureFormat(hasAlpha ? PixelFormat.Rgba32 : PixelFormat.Rgb24);

    return new() {
      Width = image.Width,
      Height = image.Height,
      BitDepth = 24,
      HasAlpha = hasAlpha,
      PixelData = image.PixelData[..],
    };
  }
}
