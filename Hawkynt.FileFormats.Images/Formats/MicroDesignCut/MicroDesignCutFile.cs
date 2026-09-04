using System;
using FileFormat.Core;

namespace FileFormat.MicroDesignCut;

/// <summary>In-memory representation of the experimentally reconstructed MicroDesign CUT bitmap.</summary>
/// <remarks>
/// The historical height field is a code rather than a direct pixel count. Integer decoding maps
/// two adjacent codes to most heights, so the original code is preserved instead of being silently
/// canonicalized on read/write round trips.
/// </remarks>
[FormatDetectionPriority(999)]
public readonly record struct MicroDesignCutFile
  : IImageFormatReader<MicroDesignCutFile>, IImageToRawImage<MicroDesignCutFile>,
    IImageFromRawImage<MicroDesignCutFile>, IImageFormatWriter<MicroDesignCutFile> {

  /// <summary>Number of bytes in the two-word CUT header.</summary>
  public const int HeaderSize = 4;

  /// <summary>Largest decoded image accepted by this managed implementation.</summary>
  public const int MaximumPixels = 100_000_000;

  static string IImageFormatMetadata<MicroDesignCutFile>.PrimaryExtension => ".cut";
  static string[] IImageFormatMetadata<MicroDesignCutFile>.FileExtensions => [".cut"];
  static MicroDesignCutFile IImageFormatReader<MicroDesignCutFile>.FromSpan(ReadOnlySpan<byte> data)
    => MicroDesignCutReader.FromSpan(data);
  static byte[] IImageFormatWriter<MicroDesignCutFile>.ToBytes(MicroDesignCutFile file)
    => MicroDesignCutWriter.ToBytes(file);

  /// <summary>Raw 16-bit height code stored by MicroDesign.</summary>
  public ushort HeightCode { get; init; }

  /// <summary>Raw 16-bit width code stored by MicroDesign.</summary>
  public ushort WidthCode { get; init; }

  /// <summary>Decoded image width in pixels.</summary>
  public int Width => GetWidth(WidthCode);

  /// <summary>Decoded image height in pixels.</summary>
  public int Height => GetHeight(HeightCode);

  /// <summary>
  /// Uncompressed packed monochrome raster, row by row. Bit 7 is leftmost and one is white. CUT
  /// rows deliberately contain one wholly unused byte when the width is an exact multiple of eight.
  /// </summary>
  public byte[] RasterData { get; init; }

  /// <summary>Decodes the stored width code according to the reconstructed format definition.</summary>
  public static int GetWidth(ushort widthCode) => widthCode + 2;

  /// <summary>Decodes the stored height code using the format's integer division rule.</summary>
  public static int GetHeight(ushort heightCode) => (heightCode + 3) / 2;

  /// <summary>Gets the number of bytes physically stored for one CUT row.</summary>
  public static int GetRowStride(int width) => checked((width + 8) / 8);

  /// <summary>Converts the CUT raster to an indexed black-and-white image.</summary>
  public static RawImage ToRawImage(MicroDesignCutFile file) {
    Validate(file, nameof(file));

    var compactStride = MonochromePage.BytesPerRow(file.Width);
    var storedStride = GetRowStride(file.Width);
    var compact = new byte[checked(compactStride * file.Height)];
    for (var y = 0; y < file.Height; ++y)
      file.RasterData.AsSpan(y * storedStride, compactStride)
        .CopyTo(compact.AsSpan(y * compactStride, compactStride));

    return MonochromePage.Decode(compact, file.Width, file.Height, inkIsWhite: true);
  }

  /// <summary>The height code this writer stores for a given pixel height.</summary>
  /// <remarks>
  /// Decoding halves with integer division, so two codes give every height and neither is more
  /// correct than the other. Refusing to pick one left a complete encoder unreachable through the
  /// registry, which is a worse answer than picking the even one and saying so: 2h-2 decodes to h
  /// for every height from one upward, and a caller that needs the odd code passes it to the
  /// overload below.
  /// </remarks>
  public static ushort HeightCodeFor(int height) {
    ArgumentOutOfRangeException.ThrowIfLessThan(height, 1);
    ArgumentOutOfRangeException.ThrowIfGreaterThan(height, (ushort.MaxValue + 2) / 2);
    return checked((ushort)(height * 2 - 2));
  }

  /// <summary>Creates a CUT bitmap from pixels alone, choosing the height code.</summary>
  public static MicroDesignCutFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    return FromRawImage(image, HeightCodeFor(image.Height));
  }

  /// <summary>
  /// Creates a CUT bitmap while preserving caller-selected height-code semantics.
  /// </summary>
  /// <remarks>
  /// Most pixel heights have two possible on-disk height codes because decoding uses integer
  /// division. The caller therefore supplies the code explicitly instead of this API inventing a
  /// preferred historical encoding.
  /// </remarks>
  public static MicroDesignCutFile FromRawImage(RawImage image, ushort heightCode) {
    ArgumentNullException.ThrowIfNull(image);
    if (image.Width is < 2 or > ushort.MaxValue + 2)
      throw new ArgumentOutOfRangeException(nameof(image), $"MicroDesign CUT width must be in the range 2..{ushort.MaxValue + 2} pixels.");
    if (GetHeight(heightCode) != image.Height)
      throw new ArgumentException($"Height code {heightCode} decodes to {GetHeight(heightCode)} lines, not {image.Height}.", nameof(heightCode));

    ValidateDimensions(image.Width, image.Height, nameof(image));

    var widthCode = checked((ushort)(image.Width - 2));
    var compact = MonochromePage.Encode(image, image.Width, image.Height, inkIsWhite: true);
    var compactStride = MonochromePage.BytesPerRow(image.Width);
    var storedStride = GetRowStride(image.Width);
    var raster = new byte[checked(storedStride * image.Height)];
    for (var y = 0; y < image.Height; ++y)
      compact.AsSpan(y * compactStride, compactStride)
        .CopyTo(raster.AsSpan(y * storedStride, compactStride));

    return new() {
      HeightCode = heightCode,
      WidthCode = widthCode,
      RasterData = raster,
    };
  }

  internal static void Validate(MicroDesignCutFile file, string parameterName) {
    ValidateDimensions(file.Width, file.Height, parameterName);
    var expected = checked(GetRowStride(file.Width) * file.Height);
    if (file.RasterData is null || file.RasterData.Length != expected)
      throw new ArgumentException($"MicroDesign CUT raster length must be exactly {expected} bytes.", parameterName);
  }

  internal static void ValidateDimensions(int width, int height, string parameterName) {
    if (width < 2 || width > ushort.MaxValue + 2)
      throw new ArgumentOutOfRangeException(parameterName, $"MicroDesign CUT width must be in the range 2..{ushort.MaxValue + 2} pixels.");
    if (height <= 0 || height > GetHeight(ushort.MaxValue))
      throw new ArgumentOutOfRangeException(parameterName, $"MicroDesign CUT height must be in the range 1..{GetHeight(ushort.MaxValue)} pixels.");
    if ((long)width * height > MaximumPixels)
      throw new ArgumentOutOfRangeException(parameterName, $"MicroDesign CUT image exceeds the {MaximumPixels:N0}-pixel implementation safety limit.");
  }
}
