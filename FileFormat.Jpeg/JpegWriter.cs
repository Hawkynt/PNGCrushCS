using System;
using System.IO;

namespace FileFormat.Jpeg;

/// <summary>Encodes JPEG file bytes via lossless transcoding or lossy re-encoding.</summary>
internal static class JpegWriter {

  /// <summary>Serializes a <see cref="JpegFile"/> to bytes. Uses lossless transcode when raw bytes are available; otherwise lossy encode at quality 90.</summary>
  public static byte[] ToBytes(JpegFile file) {
    ArgumentNullException.ThrowIfNull(file);

    if (file.RawJpegBytes != null)
      return LosslessTranscode(file.RawJpegBytes, JpegMode.Baseline, optimizeHuffman: true, stripMetadata: false);

    if (file.RgbPixelData == null)
      throw new ArgumentException("Either RawJpegBytes or RgbPixelData must be non-null.", nameof(file));

    return LossyEncode(file.RgbPixelData, file.Width, file.Height, 90, JpegMode.Baseline, JpegSubsampling.Chroma444, optimizeHuffman: true, file.IsGrayscale);
  }

  public static byte[] LosslessTranscode(
    byte[] inputJpeg,
    JpegMode mode,
    bool optimizeHuffman,
    bool stripMetadata
  ) {
    // Decode to coefficient level using the pure-managed decoder pipeline.
    var image = JpegManagedDecoder.DecodeToCoefficients(inputJpeg);

    // Re-emit as the target mode with the requested Huffman optimization.
    return JpegCoefficientWriter.Write(image, mode, optimizeHuffman, stripMetadata);
  }

  public static byte[] LossyEncode(
    byte[] rgbPixelData,
    int width,
    int height,
    int quality,
    JpegMode mode,
    JpegSubsampling subsampling,
    bool optimizeHuffman,
    bool isGrayscale
  ) => JpegManagedEncoder.Encode(
    rgbPixelData, width, height, quality, mode, subsampling, optimizeHuffman, isGrayscale);
}
