using System;
using FileFormat.Core;

namespace FileFormat.ComputerEyes;

/// <summary>In-memory representation of a ComputerEyes grayscale image.</summary>
public readonly record struct ComputerEyesFile : IImageFormatReader<ComputerEyesFile>, IImageToRawImage<ComputerEyesFile>, IImageFromRawImage<ComputerEyesFile>, IImageFormatWriter<ComputerEyesFile> {

  /// <summary>Header size: 2 width + 2 height = 4 bytes.</summary>
  public const int HeaderSize = 4;

  /// <summary>The largest either dimension goes, which is what the header's words hold.</summary>
  public const int MaxDimension = 65535;

  static string IImageFormatMetadata<ComputerEyesFile>.PrimaryExtension => ".ce";
  static string[] IImageFormatMetadata<ComputerEyesFile>.FileExtensions => [".ce", ".ce1", ".ce2"];
  static ComputerEyesFile IImageFormatReader<ComputerEyesFile>.FromSpan(ReadOnlySpan<byte> data) => ComputerEyesReader.FromSpan(data);
  static byte[] IImageFormatWriter<ComputerEyesFile>.ToBytes(ComputerEyesFile file) => ComputerEyesWriter.ToBytes(file);

  public int Width { get; init; }
  public int Height { get; init; }

  /// <summary>Raw 8-bit grayscale pixel data.</summary>
  public byte[] PixelData { get; init; }

  public static RawImage ToRawImage(ComputerEyesFile file) {
    var pixelCount = file.Width * file.Height;
    var rgb = new byte[pixelCount * 3];
    for (var i = 0; i < pixelCount; ++i) {
      var value = i < file.PixelData.Length ? file.PixelData[i] : (byte)0;
      rgb[i * 3] = value;
      rgb[i * 3 + 1] = value;
      rgb[i * 3 + 2] = value;
    }

    return new() {
      Width = file.Width,
      Height = file.Height,
      Format = PixelFormat.Rgb24,
      PixelData = rgb,
    };
  }

  /// <summary>Creates a ComputerEyes picture from a platform-independent <see cref="RawImage"/>.</summary>
  /// <remarks>
  /// The digitiser produced one grey byte a pixel and the header carries the size, so a colour
  /// picture is reduced to grey and one of any size is kept as it is.
  /// </remarks>
  public static ComputerEyesFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);

    // The header states the size as words; a bigger picture would be written with its dimensions
    // wrapped and read back as a different one rather than as a broken one.
    if (image.Width is < 1 or > MaxDimension || image.Height is < 1 or > MaxDimension)
      throw new ArgumentException(
        $"A ComputerEyes picture is at most {MaxDimension}x{MaxDimension}; got {image.Width}x{image.Height}.", nameof(image));

    var source = image.EnsureFormat(PixelFormat.Gray8);

    return new() {
      Width = source.Width,
      Height = source.Height,
      PixelData = source.PixelData[..],
    };
  }

}
