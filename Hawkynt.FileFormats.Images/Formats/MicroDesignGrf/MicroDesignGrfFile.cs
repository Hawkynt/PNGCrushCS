using System;
using FileFormat.Core;

namespace FileFormat.MicroDesignGrf;

/// <summary>In-memory representation of a MicroDesign GRF monochrome bitmap.</summary>
/// <remarks>
/// The published GRF description was reconstructed experimentally rather than supplied as part of
/// Creative Technology's formal MDA/MDP specification. It uses the same MicroDesign bilevel bitmap
/// convention: most-significant bit first, one for white and zero for black.
/// </remarks>
public readonly record struct MicroDesignGrfFile
  : IImageFormatReader<MicroDesignGrfFile>, IImageToRawImage<MicroDesignGrfFile>,
    IImageFromRawImage<MicroDesignGrfFile>, IImageFormatWriter<MicroDesignGrfFile> {

  /// <summary>Number of bytes occupied by the width/height header.</summary>
  public const int HeaderSize = 4;

  /// <summary>Largest decoded image accepted by this managed implementation.</summary>
  public const int MaximumPixels = 100_000_000;

  static string IImageFormatMetadata<MicroDesignGrfFile>.PrimaryExtension => ".grf";
  static string[] IImageFormatMetadata<MicroDesignGrfFile>.FileExtensions => [".grf"];
  static MicroDesignGrfFile IImageFormatReader<MicroDesignGrfFile>.FromSpan(ReadOnlySpan<byte> data)
    => MicroDesignGrfReader.FromSpan(data);
  static byte[] IImageFormatWriter<MicroDesignGrfFile>.ToBytes(MicroDesignGrfFile file)
    => MicroDesignGrfWriter.ToBytes(file);

  /// <summary>Image width in pixels.</summary>
  public int Width { get; init; }

  /// <summary>Image height in pixels.</summary>
  public int Height { get; init; }

  /// <summary>
  /// Packed monochrome rows. Bit 7 is the leftmost pixel; one is white and zero is black. Padding
  /// bits after the declared width are preserved when a file is read and written again.
  /// </summary>
  public byte[] RasterData { get; init; }

  /// <summary>Gets the number of packed bytes occupied by one row.</summary>
  public static int GetRowStride(int width) => (width + 7) >> 3;

  /// <summary>Converts the packed MicroDesign bitmap to indexed black and white pixels.</summary>
  public static RawImage ToRawImage(MicroDesignGrfFile file) {
    Validate(file, nameof(file));
    return MonochromePage.Decode(file.RasterData, file.Width, file.Height, inkIsWhite: true);
  }

  /// <summary>Creates a MicroDesign GRF bitmap from an arbitrary image.</summary>
  public static MicroDesignGrfFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    ValidateDimensions(image.Width, image.Height, nameof(image));

    return new() {
      Width = image.Width,
      Height = image.Height,
      RasterData = MonochromePage.Encode(image, image.Width, image.Height, inkIsWhite: true),
    };
  }

  internal static void Validate(MicroDesignGrfFile file, string parameterName) {
    ValidateDimensions(file.Width, file.Height, parameterName);
    var expected = checked(GetRowStride(file.Width) * file.Height);
    if (file.RasterData is null || file.RasterData.Length != expected)
      throw new ArgumentException($"MicroDesign GRF raster length must be exactly {expected} bytes.", parameterName);
  }

  internal static void ValidateDimensions(int width, int height, string parameterName) {
    if (width <= 0 || width > ushort.MaxValue)
      throw new ArgumentOutOfRangeException(parameterName, $"MicroDesign GRF width must be in the range 1..{ushort.MaxValue}.");
    if (height <= 0 || height > ushort.MaxValue)
      throw new ArgumentOutOfRangeException(parameterName, $"MicroDesign GRF height must be in the range 1..{ushort.MaxValue}.");
    if ((long)width * height > MaximumPixels)
      throw new ArgumentOutOfRangeException(parameterName, $"MicroDesign GRF image exceeds the {MaximumPixels:N0}-pixel implementation safety limit.");
  }
}
