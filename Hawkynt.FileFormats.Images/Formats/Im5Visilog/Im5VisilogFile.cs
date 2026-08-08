using System;
using FileFormat.Core;

namespace FileFormat.Im5Visilog;

/// <summary>In-memory representation of an IM5 Visilog grayscale image.</summary>
public readonly record struct Im5VisilogFile : IImageFormatReader<Im5VisilogFile>, IImageToRawImage<Im5VisilogFile>, IImageFromRawImage<Im5VisilogFile>, IImageFormatWriter<Im5VisilogFile> {

  static string IImageFormatMetadata<Im5VisilogFile>.PrimaryExtension => ".im5";
  static string[] IImageFormatMetadata<Im5VisilogFile>.FileExtensions => [".im5"];
  static Im5VisilogFile IImageFormatReader<Im5VisilogFile>.FromSpan(ReadOnlySpan<byte> data) => Im5VisilogReader.FromSpan(data);
  static byte[] IImageFormatWriter<Im5VisilogFile>.ToBytes(Im5VisilogFile file) => Im5VisilogWriter.ToBytes(file);

  /// <summary>Header size: width(4) + height(4) + depth(4) = 12 bytes.</summary>
  internal const int HeaderSize = 12;

  /// <summary>Minimum valid file size.</summary>
  public const int MinFileSize = HeaderSize;

  /// <summary>Image width in pixels.</summary>
  public int Width { get; init; }

  /// <summary>Image height in pixels.</summary>
  public int Height { get; init; }

  /// <summary>Bit depth (8 or 16).</summary>
  public int Depth { get; init; }

  /// <summary>Raw grayscale pixel data.</summary>
  public byte[] PixelData { get; init; }

  /// <summary>Converts this IM5 image to a platform-independent <see cref="RawImage"/> in Rgb24 format.</summary>
  public static RawImage ToRawImage(Im5VisilogFile file) {

    var pixelCount = file.Width * file.Height;
    var rgb = new byte[pixelCount * 3];

    if (file.Depth == 16) {
      for (var i = 0; i < pixelCount; ++i) {
        var value = (byte)(BitConverter.ToUInt16(file.PixelData, i * 2) >> 8);
        var offset = i * 3;
        rgb[offset] = value;
        rgb[offset + 1] = value;
        rgb[offset + 2] = value;
      }
    } else {
      for (var i = 0; i < pixelCount; ++i) {
        var value = file.PixelData[i];
        var offset = i * 3;
        rgb[offset] = value;
        rgb[offset + 1] = value;
        rgb[offset + 2] = value;
      }
    }

    return new() {
      Width = file.Width,
      Height = file.Height,
      Format = PixelFormat.Rgb24,
      PixelData = rgb,
    };
  }

  /// <summary>Writes a picture as an IM5 image, at whatever of the two depths it arrived with.</summary>
  /// <remarks>
  /// Visilog is machine vision, where the samples are measurements: a sixteen-bit picture is stored
  /// at sixteen rather than being halved, since the header has a depth field precisely so it need
  /// not be. The size lives there too, so any size fits.
  /// <para/>
  /// Samples are little-endian words, which is how the reader takes them and the opposite of the
  /// big-endian <see cref="PixelFormat.Gray16"/> buffer they come from.
  /// </remarks>
  public static Im5VisilogFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);

    var pixelCount = image.Width * image.Height;
    var deep = image.Format is PixelFormat.Gray16 or PixelFormat.Gray10;

    byte[] samples;
    if (deep) {
      var gray16 = image.EnsureFormat(PixelFormat.Gray16).PixelData;
      samples = new byte[pixelCount * 2];
      for (var i = 0; i < pixelCount; ++i) {
        samples[i * 2] = gray16[i * 2 + 1];
        samples[i * 2 + 1] = gray16[i * 2];
      }
    } else
      samples = image.EnsureFormat(PixelFormat.Gray8).PixelData[..pixelCount];

    return new() {
      Width = image.Width,
      Height = image.Height,
      Depth = deep ? 16 : 8,
      PixelData = samples,
    };
  }

}
