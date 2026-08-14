using System;
using FileFormat.Core;
using FileFormat.Pcd;

namespace FileFormat.Pcds;

/// <summary>A Kodak Photo CD read as sRGB rather than as Photo YCC (.pcds).</summary>
/// <remarks>
/// Not a second container and not a stacked or multi-picture form of the first one, which is what
/// the name suggests and what it is not. It is the same file: the same 2048-byte preamble, the same
/// <c>PCD_IPI</c> at 2048, the same pyramid of sizes at the same fixed offsets, the same interleave
/// of two luminance rows against one row of each chrominance at half the width. Written from the
/// same picture the two are byte for byte identical, which is how this was established rather than
/// assumed.
/// <para/>
/// What differs is the last step of reading. A <c>.pcd</c> says its three planes are Photo YCC and
/// runs them through the transform that space needs — the one whose luminance carries past white on
/// purpose, so its output has to be fitted into a byte rather than clipped. A <c>.pcds</c> says they
/// are already sRGB, so the luminance plane is red and the two chrominance planes are green and blue,
/// unscaled and untransformed. The same bytes therefore make two different pictures, which is the
/// whole of the format.
/// <para/>
/// Writing is the same door the other way: the three channels go into the three planes, the
/// chrominance ones at half resolution each way as the container requires, at all three sizes —
/// a reader takes whichever it wants, so leaving one out produces a file that opens at a size
/// nobody asked for.
/// </remarks>
public readonly record struct PcdsFile
  : IImageFormatReader<PcdsFile>, IImageToRawImage<PcdsFile>,
    IImageFromRawImage<PcdsFile>, IImageFormatWriter<PcdsFile> {

  static string IImageFormatMetadata<PcdsFile>.PrimaryExtension => ".pcds";
  static string[] IImageFormatMetadata<PcdsFile>.FileExtensions => [".pcds"];
  static PcdsFile IImageFormatReader<PcdsFile>.FromSpan(ReadOnlySpan<byte> data) => PcdsReader.FromSpan(data);
  static byte[] IImageFormatWriter<PcdsFile>.ToBytes(PcdsFile file) => PcdsWriter.ToBytes(file);

  /// <summary>Image width in pixels.</summary>
  public int Width { get; init; }

  /// <summary>Image height in pixels.</summary>
  public int Height { get; init; }

  /// <summary>Raw RGB pixel data (3 bytes per pixel).</summary>
  public byte[] PixelData { get; init; }

  public static RawImage ToRawImage(PcdsFile file) {
    return new() {
      Width = file.Width,
      Height = file.Height,
      Format = PixelFormat.Rgb24,
      PixelData = file.PixelData[..],
    };
  }

  public static PcdsFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    image = image.EnsureFormat(PixelFormat.Rgb24);

    return new() {
      Width = image.Width,
      Height = image.Height,
      PixelData = image.PixelData[..],
    };
  }
}
