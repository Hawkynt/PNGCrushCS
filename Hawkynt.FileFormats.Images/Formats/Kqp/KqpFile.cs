using System;
using FileFormat.Core;

namespace FileFormat.Kqp;

/// <summary>In-memory representation of a Konica Quality Photo picture (.kqp).</summary>
/// <remarks>
/// What Konica's PC PictureShow wrote for the Q-M100 cameras. The first eighty-two bytes are an
/// ordinary Windows bitmap file header and a bitmap info header of sixty-eight bytes rather than
/// forty, saying twenty-four bits deep and a compression of <c>JPEG</c>; then a palette of the
/// colours the picture uses, for the benefit of an eight-bit screen; and from the offset the file
/// header names, a JPEG.
/// <para/>
/// That JPEG is not complete. It carries its start marker, a JFIF segment, a private <c>PIC</c>
/// segment, a frame header and a scan header, and then the entropy-coded data — with no
/// quantisation tables and no Huffman tables at all. Both sets have to be supplied from outside,
/// which is why splitting the file at the offset and renaming it <c>.jpg</c> does not work and every
/// viewer that tries it reports a table that was never defined.
/// <para/>
/// The Huffman tables are the standard ones from Annex K of the JPEG specification, which the
/// library already keeps. The quantisation tables are not in the file anywhere — the bytes between
/// the info header and the picture are a genuine palette, one entry per colour the header counts —
/// so they are carried here as a constant. They are the right ones: the splash screen Konica shipped
/// with the software decodes with lettering that is sharp to the pixel, which the wrong tables
/// cannot produce.
/// </remarks>
public readonly record struct KqpFile
  : IImageFormatReader<KqpFile>, IImageToRawImage<KqpFile>,
    IImageFromRawImage<KqpFile>, IImageFormatWriter<KqpFile> {

  /// <summary>The two bytes a Windows bitmap file header opens with.</summary>
  public static ReadOnlySpan<byte> Magic => [(byte)'B', (byte)'M'];

  /// <summary>The compression field reads as four letters rather than a number.</summary>
  public static ReadOnlySpan<byte> JpegCompression => [(byte)'J', (byte)'P', (byte)'E', (byte)'G'];

  /// <summary>A bitmap file header is fourteen bytes and the info header follows it.</summary>
  public const int FileHeaderSize = 14;

  /// <summary>Forty bytes of ordinary info header and twenty-eight more Konica added.</summary>
  public const int InfoHeaderSize = 68;

  /// <summary>Where the offset of the picture data is stored.</summary>
  public const int DataOffsetField = 10;

  /// <summary>
  /// The quantisation tables every one of these is coded against, as a ready-made segment.
  /// </summary>
  /// <remarks>
  /// Eight-bit precision, two tables: nought for luminance and one for both chrominance components,
  /// which is what the scan header asks for. The camera never wrote them into the file, so a decoder
  /// has to know them; these are the ones the Konica software used, and an encoder has to quantise
  /// against exactly these or the file it writes decodes to some other picture.
  /// </remarks>
  internal static ReadOnlySpan<byte> QuantisationTables => [
    0xFF, 0xDB, 0x00, 0x84,
    0x00,
    0x0C, 0x08, 0x09, 0x0A, 0x09, 0x07, 0x0C, 0x0A,
    0x0A, 0x0A, 0x0E, 0x0D, 0x0C, 0x0E, 0x12, 0x1F,
    0x14, 0x12, 0x11, 0x11, 0x12, 0x26, 0x1B, 0x1C,
    0x16, 0x1F, 0x2D, 0x27, 0x2F, 0x2E, 0x2C, 0x27,
    0x2B, 0x2A, 0x32, 0x38, 0x47, 0x3C, 0x32, 0x35,
    0x43, 0x35, 0x2A, 0x2B, 0x3E, 0x55, 0x3F, 0x43,
    0x4A, 0x4C, 0x50, 0x51, 0x50, 0x30, 0x3C, 0x58,
    0x5E, 0x57, 0x4E, 0x5D, 0x47, 0x4E, 0x50, 0x4D,
    0x01,
    0x0F, 0x10, 0x10, 0x16, 0x13, 0x16, 0x2C, 0x18,
    0x18, 0x2C, 0x5C, 0x3D, 0x34, 0x3D, 0x5C, 0x5C,
    0x5C, 0x5C, 0x5C, 0x5C, 0x5C, 0x5C, 0x5C, 0x5C,
    0x5C, 0x5C, 0x5C, 0x5C, 0x5C, 0x5C, 0x5C, 0x5C,
    0x5C, 0x5C, 0x5C, 0x5C, 0x5C, 0x5C, 0x5C, 0x5C,
    0x5C, 0x5C, 0x5C, 0x5C, 0x5C, 0x5C, 0x5C, 0x5C,
    0x5C, 0x5C, 0x5C, 0x5C, 0x5C, 0x5C, 0x5C, 0x5C,
    0x5C, 0x5C, 0x5C, 0x5C, 0x5C, 0x5C, 0x5C, 0x5C,
  ];

  static string IImageFormatMetadata<KqpFile>.PrimaryExtension => ".kqp";
  static string[] IImageFormatMetadata<KqpFile>.FileExtensions => [".kqp"];
  static KqpFile IImageFormatReader<KqpFile>.FromSpan(ReadOnlySpan<byte> data) => KqpReader.FromSpan(data);
  static byte[] IImageFormatWriter<KqpFile>.ToBytes(KqpFile file) => KqpWriter.ToBytes(file);
  static VideoMode[] IImageFormatMetadata<KqpFile>.VideoModes => [
    new("Default", [(IntegerRange.Any, IntegerRange.Any)], [16777216])
  ];

  /// <summary>Image width in pixels, as the bitmap info header states it.</summary>
  public int Width { get; init; }

  /// <summary>Image height in pixels, as the bitmap info header states it.</summary>
  public int Height { get; init; }

  /// <summary>Raw pixel data in RGB24 interleaved order.</summary>
  public byte[] PixelData { get; init; }

  public static RawImage ToRawImage(KqpFile file) {
    return new() {
      Width = file.Width,
      Height = file.Height,
      Format = PixelFormat.Rgb24,
      PixelData = file.PixelData[..],
    };
  }

  public static KqpFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);

    return new() {
      Width = image.Width,
      Height = image.Height,
      PixelData = image.ToRgb24(),
    };
  }
}
