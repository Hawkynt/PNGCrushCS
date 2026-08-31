using System;
using FileFormat.Core;

namespace FileFormat.Mda;

/// <summary>MicroDesign Area on-disk compression generation.</summary>
public enum MdaVersion {
  /// <summary>MicroDesign 2 AREA2 format, file version v1.00.</summary>
  Area2,

  /// <summary>MicroDesign 3 AREA3 format, file version v1.30.</summary>
  Area3,
}

/// <summary>In-memory representation of a MicroDesign Area (.MDA) monochrome bitmap.</summary>
[FormatDetectionPriority(100)]
[FormatMagicBytes([0x2E, 0x4D, 0x44, 0x41])]
public readonly record struct MdaFile : IImageFormatReader<MdaFile>, IImageToRawImage<MdaFile>, IImageFromRawImage<MdaFile>, IImageFormatWriter<MdaFile> {

  /// <summary>Fixed number of bytes used by the MicroDesign stamp.</summary>
  public const int StampSize = 128;

  /// <summary>Length of the preserved ASCII user serial field.</summary>
  public const int SerialNumberLength = 7;

  /// <summary>Largest decoded image accepted by this managed implementation.</summary>
  public const int MaximumPixels = 100_000_000;

  /// <summary>Default serial used when converting a raw image into a new MDA file.</summary>
  public const string DefaultSerialNumber = "0000000";

  static string IImageFormatMetadata<MdaFile>.PrimaryExtension => ".mda";
  static string[] IImageFormatMetadata<MdaFile>.FileExtensions => [".mda"];
  static MdaFile IImageFormatReader<MdaFile>.FromSpan(ReadOnlySpan<byte> data) => MdaReader.FromSpan(data);
  static byte[] IImageFormatWriter<MdaFile>.ToBytes(MdaFile file) => MdaWriter.ToBytes(file);

  /// <summary>Image width in pixels. MDA stores whole bytes, so this must be divisible by eight.</summary>
  public int Width { get; init; }

  /// <summary>Image height in lines. The format requires a multiple of four.</summary>
  public int Height { get; init; }

  /// <summary>AREA2 or AREA3 compression generation used when writing the file.</summary>
  public MdaVersion Version { get; init; }

  /// <summary>Seven-character ASCII user serial number from the file stamp.</summary>
  public string SerialNumber { get; init; }

  /// <summary>
  /// Uncompressed packed monochrome raster, top-to-bottom and left-to-right. Within each byte,
  /// bit 7 is the leftmost pixel; one is white and zero is black.
  /// </summary>
  public byte[] RasterData { get; init; }

  /// <summary>Gets the packed raster byte count for one row.</summary>
  public static int GetRowStride(int width) => checked(width >> 3);

  /// <summary>Converts an MDA bitmap to an indexed black-and-white image.</summary>
  public static RawImage ToRawImage(MdaFile file) {
    Validate(file, nameof(file));

    var pixels = new byte[checked(file.Width * file.Height)];
    var stride = GetRowStride(file.Width);
    for (var y = 0; y < file.Height; ++y)
      for (var x = 0; x < file.Width; ++x) {
        var encoded = file.RasterData[y * stride + (x >> 3)];
        pixels[y * file.Width + x] = (byte)((encoded >> (7 - (x & 7))) & 1);
      }

    return new RawImage {
      Width = file.Width,
      Height = file.Height,
      Format = PixelFormat.Indexed8,
      PixelData = pixels,
      Palette = [0, 0, 0, 255, 255, 255],
      PaletteCount = 2,
    };
  }

  /// <summary>Creates an AREA3 MDA bitmap from an arbitrary image.</summary>
  public static MdaFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    _ValidateDimensions(image.Width, image.Height, nameof(image));
    image = image.EnsureAnyFormat(PixelFormat.Rgb24);

    var stride = GetRowStride(image.Width);
    var raster = new byte[checked(stride * image.Height)];
    for (var y = 0; y < image.Height; ++y)
      for (var x = 0; x < image.Width; ++x) {
        var source = (y * image.Width + x) * 3;
        var r = image.PixelData[source];
        var g = image.PixelData[source + 1];
        var b = image.PixelData[source + 2];
        var luma = (299 * r + 587 * g + 114 * b + 500) / 1000;
        if (luma < 128)
          continue;

        raster[y * stride + (x >> 3)] |= (byte)(1 << (7 - (x & 7)));
      }

    return new MdaFile {
      Width = image.Width,
      Height = image.Height,
      Version = MdaVersion.Area3,
      SerialNumber = DefaultSerialNumber,
      RasterData = raster,
    };
  }

  internal static void Validate(MdaFile file, string parameterName) {
    _ValidateDimensions(file.Width, file.Height, parameterName);
    if (file.Version is not (MdaVersion.Area2 or MdaVersion.Area3))
      throw new ArgumentOutOfRangeException(parameterName, $"Unsupported MDA version value {file.Version}.");

    ValidateSerialNumber(file.SerialNumber, parameterName);

    var expected = checked(GetRowStride(file.Width) * file.Height);
    if (file.RasterData is null || file.RasterData.Length != expected)
      throw new ArgumentException($"MDA raster length must be exactly {expected} bytes.", parameterName);
  }

  internal static void ValidateSerialNumber(string serialNumber, string parameterName) {
    if (serialNumber is null || serialNumber.Length != SerialNumberLength)
      throw new ArgumentException($"MDA serial number must contain exactly {SerialNumberLength} ASCII characters.", parameterName);

    foreach (var character in serialNumber)
      if (character is < ' ' or > '~')
        throw new ArgumentException("MDA serial number must contain printable 7-bit ASCII characters only.", parameterName);
  }

  private static void _ValidateDimensions(int width, int height, string parameterName) {
    if (width <= 0 || (width & 7) != 0 || (width >> 3) > ushort.MaxValue)
      throw new ArgumentOutOfRangeException(parameterName, $"MDA width must be a positive multiple of 8 not exceeding {ushort.MaxValue * 8} pixels.");
    if (height <= 0 || height > ushort.MaxValue || (height & 3) != 0)
      throw new ArgumentOutOfRangeException(parameterName, $"MDA height must be a positive multiple of 4 not exceeding {ushort.MaxValue} lines.");

    var pixels = (long)width * height;
    if (pixels > MaximumPixels)
      throw new ArgumentOutOfRangeException(parameterName, $"MDA image exceeds the {MaximumPixels:N0}-pixel implementation safety limit.");
  }
}
