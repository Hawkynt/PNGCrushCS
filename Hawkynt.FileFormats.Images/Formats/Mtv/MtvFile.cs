using System;
using FileFormat.Core;

namespace FileFormat.Mtv;

/// <summary>In-memory representation of an MTV Ray Tracer image.</summary>
public readonly record struct MtvFile : IImageFormatReader<MtvFile>, IImageToRawImage<MtvFile>, IImageFromRawImage<MtvFile>, IImageFormatWriter<MtvFile> {

  static string IImageFormatMetadata<MtvFile>.PrimaryExtension => ".mtv";

  /// <summary><c>.pic</c> is the same raster under Rayshade's name for it.</summary>
  /// <remarks>
  /// Rayshade writes Utah RLE only when it is built against that toolkit; without it, per its own
  /// README, it "can be configured to create image files using a generic format identical to that
  /// used by Mark VandeWettering's 'mtv' ray tracer", and that is what lands in a <c>.pic</c>.
  /// XnView lists Rayshade and MTV as two formats but its writers emit the same bytes — one 61x37
  /// picture converted both ways gave files that compare equal.
  /// <para/>
  /// Several other formats answer to <c>.pic</c>, all of them with a binary magic number, so the
  /// registry reaches this reader only after those have looked at the bytes and declined. What
  /// keeps it from taking their files anyway is the size line: two numbers, and a payload that
  /// fills them.
  /// </remarks>
  static string[] IImageFormatMetadata<MtvFile>.FileExtensions => [".mtv", ".pic"];
  static MtvFile IImageFormatReader<MtvFile>.FromSpan(ReadOnlySpan<byte> data) => MtvReader.FromSpan(data);
  static byte[] IImageFormatWriter<MtvFile>.ToBytes(MtvFile file) => MtvWriter.ToBytes(file);
  public int Width { get; init; }
  public int Height { get; init; }

  /// <summary>Raw RGB pixel data (3 bytes per pixel).</summary>
  public byte[] PixelData { get; init; }

  public static RawImage ToRawImage(MtvFile file) {
    return new() {
      Width = file.Width,
      Height = file.Height,
      Format = PixelFormat.Rgb24,
      PixelData = file.PixelData[..],
    };
  }

  public static MtvFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    image = image.EnsureFormat(PixelFormat.Rgb24);

    return new() {
      Width = image.Width,
      Height = image.Height,
      PixelData = image.PixelData[..],
    };
  }
}
