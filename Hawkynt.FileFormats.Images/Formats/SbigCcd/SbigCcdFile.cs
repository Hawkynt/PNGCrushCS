using System;
using FileFormat.Core;

namespace FileFormat.SbigCcd;

/// <summary>In-memory representation of an SBIG CCD camera image (16-bit grayscale).</summary>
public readonly record struct SbigCcdFile : IImageFormatReader<SbigCcdFile>, IImageToRawImage<SbigCcdFile>, IImageFromRawImage<SbigCcdFile>, IImageFormatWriter<SbigCcdFile> {

  /// <summary>Header size: 2 width + 2 height + 8 reserved = 12 bytes.</summary>
  public const int HeaderSize = 12;

  /// <summary>The largest either dimension goes, which is what the header's words hold.</summary>
  public const int MaxDimension = 65535;

  static string IImageFormatMetadata<SbigCcdFile>.PrimaryExtension => ".st4";
  static string[] IImageFormatMetadata<SbigCcdFile>.FileExtensions => [".st4", ".stx", ".st5", ".st6", ".st7", ".st8"];
  static SbigCcdFile IImageFormatReader<SbigCcdFile>.FromSpan(ReadOnlySpan<byte> data) => SbigCcdReader.FromSpan(data);
  static byte[] IImageFormatWriter<SbigCcdFile>.ToBytes(SbigCcdFile file) => SbigCcdWriter.ToBytes(file);

  public int Width { get; init; }
  public int Height { get; init; }

  /// <summary>Raw 16-bit LE grayscale pixel data (2 bytes per pixel).</summary>
  public byte[] PixelData { get; init; }

  public static RawImage ToRawImage(SbigCcdFile file) {
    var pixelCount = file.Width * file.Height;
    var rgb = new byte[pixelCount * 3];
    for (var i = 0; i < pixelCount; ++i) {
      var hi = (i * 2 + 1) < file.PixelData.Length ? file.PixelData[i * 2 + 1] : (byte)0;
      rgb[i * 3] = hi;
      rgb[i * 3 + 1] = hi;
      rgb[i * 3 + 2] = hi;
    }

    return new() {
      Width = file.Width,
      Height = file.Height,
      Format = PixelFormat.Rgb24,
      PixelData = rgb,
    };
  }

  /// <summary>Creates an SBIG CCD frame from a platform-independent <see cref="RawImage"/>.</summary>
  /// <remarks>
  /// A CCD frame is a count of photons at sixteen bits a well, so the samples are kept at sixteen
  /// even though the decoder only shows the top eight of them — halving them here would throw away
  /// the part an astronomer stretches. The header carries the size, so any size fits.
  /// <para/>
  /// Samples are little-endian words; the <see cref="PixelFormat.Gray16"/> buffer they come from is
  /// big-endian.
  /// </remarks>
  public static SbigCcdFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);

    // The header states the size as words; a bigger frame would be written with its dimensions
    // wrapped and read back as a different one rather than as a broken one.
    if (image.Width is < 1 or > MaxDimension || image.Height is < 1 or > MaxDimension)
      throw new ArgumentException(
        $"An SBIG frame is at most {MaxDimension}x{MaxDimension}; got {image.Width}x{image.Height}.", nameof(image));

    var pixelCount = image.Width * image.Height;
    var gray16 = image.EnsureFormat(PixelFormat.Gray16).PixelData;
    var samples = new byte[pixelCount * 2];
    for (var i = 0; i < pixelCount; ++i) {
      samples[i * 2] = gray16[i * 2 + 1];
      samples[i * 2 + 1] = gray16[i * 2];
    }

    return new() {
      Width = image.Width,
      Height = image.Height,
      PixelData = samples,
    };
  }

}
