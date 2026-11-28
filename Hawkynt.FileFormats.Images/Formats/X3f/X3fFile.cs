using System;
using FileFormat.Core;

namespace FileFormat.X3f;

/// <summary>In-memory representation of a Sigma/Foveon X3F raw file (.x3f).</summary>
/// <remarks>
/// A sectioned container. Two hundred and thirty-two bytes of header — <c>FOVb</c>, a version, an
/// identifier, the size of the sensor and a white balance — and then sections laid end to end, with
/// a directory at the very end of the file that the last four bytes point at. The directory names
/// each section's offset, length and kind; the picture-bearing ones are <c>IMAG</c> and <c>IMA2</c>,
/// and each states its own type, storage format, width, height and row stride.
/// <para/>
/// The storage formats are not all the same thing. Some cameras put a full-size JPEG in one of these
/// sections and their sensor data in another; the Polaroid x530 does exactly that, and its JPEG is
/// the picture at the size the camera says it took. Others — the Sigma bodies — store only Foveon
/// data, coded with a Huffman scheme and needing a large block of per-camera correction values to
/// turn three stacked layers into colour, and carry nothing else above thumbnail size.
/// <para/>
/// The Foveon sensor data is read too, in every storage LibRaw reads: the Polaroid x530's packed
/// and the Sigma SD9/SD10/SD14 bodies' compressed Huffman coding of mapped 10-bit differences, and
/// the TRUE engines of the DP and Merrill bodies and the Quattro's with its half-size lower layers.
/// It is handed out as <see cref="Sensor"/>, three 16-bit layers a site exactly as LibRaw unpacks
/// them, with no black level, gain or colour correction, because those come from the camera's
/// correction block through a processing chain the format does not define. The picture is the
/// full-size JPEG the camera stored where it stored one; a file with only sensor data hands out the
/// layers themselves as 48-bit samples rather than a preview drawn as the picture, which would be
/// the wrong answer given confidently, and a file with neither is refused.
/// <para/>
/// Writing uses the public X3F 2.2 container's processed-image path: one full-size, uncompressed
/// RGB24 <c>IMAG</c> section with four-byte row alignment. It deliberately does not invent Foveon
/// sensor samples, lens/exposure metadata, or a camera-specific correction block.
/// </remarks>
public readonly record struct X3fFile
  : IImageFormatReader<X3fFile>, IImageToRawImage<X3fFile>,
    IImageFromRawImage<X3fFile>, IImageFormatWriter<X3fFile> {

  /// <summary>The four bytes every one of these opens with.</summary>
  public static ReadOnlySpan<byte> Magic => [(byte)'F', (byte)'O', (byte)'V', (byte)'b'];

  /// <summary>The four bytes the directory opens with.</summary>
  public static ReadOnlySpan<byte> DirectoryMagic => [(byte)'S', (byte)'E', (byte)'C', (byte)'d'];

  /// <summary>Where the sensor's column count sits in the header.</summary>
  public const int ColumnsField = 0x1C;

  /// <summary>Where the sensor's row count sits in the header.</summary>
  public const int RowsField = 0x20;

  /// <summary>The header runs to here, which is where the first section starts.</summary>
  public const int HeaderSize = 232;

  /// <summary>A directory entry is an offset, a length and a four-letter kind.</summary>
  public const int DirectoryEntrySize = 12;

  /// <summary>Section magic, version, type, format, columns, rows and row stride.</summary>
  public const int ImageSectionHeaderSize = 28;

  /// <summary>Storage format for a section holding an ordinary JPEG stream.</summary>
  public const int FormatJpeg = 18;

  /// <summary>Storage format for a section holding uncompressed three-byte samples.</summary>
  public const int FormatRgb24 = 3;

  static string IImageFormatMetadata<X3fFile>.PrimaryExtension => ".x3f";
  static string[] IImageFormatMetadata<X3fFile>.FileExtensions => [".x3f"];
  static X3fFile IImageFormatReader<X3fFile>.FromSpan(ReadOnlySpan<byte> data) => X3fReader.FromSpan(data);
  static byte[] IImageFormatWriter<X3fFile>.ToBytes(X3fFile file) => X3fWriter.ToBytes(file);
  static VideoMode[] IImageFormatMetadata<X3fFile>.VideoModes => [
    new("Default", [(IntegerRange.Any, IntegerRange.Any)], [16777216])
  ];

  /// <summary>Image width in pixels.</summary>
  public int Width { get; init; }

  /// <summary>Image height in pixels.</summary>
  public int Height { get; init; }

  /// <summary>The picture in RGB24 interleaved order, or <c>null</c> where the file stores only sensor data.</summary>
  public byte[] PixelData { get; init; }

  /// <summary>The Foveon sensor's three layers, unprocessed, where the file stores them.</summary>
  public X3fSensor? Sensor { get; init; }

  /// <summary>
  /// The full-size picture the camera stored where it stored one; otherwise the sensor's three layers
  /// as <see cref="PixelFormat.Rgb48"/>, top layer first, linear and uncorrected.
  /// </summary>
  public static RawImage ToRawImage(X3fFile file) {
    if (file.PixelData == null && file.Sensor is { } sensor) {
      var pixels = new byte[sensor.Layers.Length * 2];
      for (var i = 0; i < sensor.Layers.Length; ++i)
        System.Buffers.Binary.BinaryPrimitives.WriteUInt16BigEndian(pixels.AsSpan(2 * i), sensor.Layers[i]);
      return new() { Width = sensor.Width, Height = sensor.Height, Format = PixelFormat.Rgb48, PixelData = pixels };
    }

    return new() {
      Width = file.Width,
      Height = file.Height,
      Format = PixelFormat.Rgb24,
      PixelData = file.PixelData[..],
    };
  }

  public static X3fFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    image = image.EnsureFormat(PixelFormat.Rgb24);

    return new() {
      Width = image.Width,
      Height = image.Height,
      PixelData = image.PixelData[..],
    };
  }
}
