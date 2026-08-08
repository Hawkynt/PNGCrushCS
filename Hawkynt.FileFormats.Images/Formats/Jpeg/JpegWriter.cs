using System;
using System.Collections.Generic;
using System.IO;
using FileFormat.Core;

namespace FileFormat.Jpeg;

/// <summary>Encodes JPEG file bytes via lossless transcoding or lossy re-encoding.</summary>
/// <remarks>
/// <see cref="JpegFile.Metadata"/> only ever overrides what a lossless transcode embeds when the
/// caller explicitly set it (non-<c>null</c>) — a plain decode-then-write with <see cref="JpegFile.Metadata"/>
/// left at its default keeps re-emitting whatever <see cref="JpegFile.RawJpegBytes"/> already carries,
/// byte-identical to today. That is what every existing lossless-transcode caller (the optimizer, the
/// round-trip tests) already relies on; only a caller that actively read/edited metadata via
/// <see cref="JpegMetadataCodec"/> and set it back onto the file opts into the override.
/// </remarks>
internal static class JpegWriter {

  /// <summary>Serializes a <see cref="JpegFile"/> to bytes. Uses lossless transcode when raw bytes are available; otherwise lossy encode at quality 90.</summary>
  public static byte[] ToBytes(JpegFile file) {
    ArgumentNullException.ThrowIfNull(file);

    if (file.RawJpegBytes != null)
      return LosslessTranscode(file.RawJpegBytes, JpegMode.Baseline, optimizeHuffman: true, stripMetadata: false, metadataOverride: file.Metadata);

    if (file.RgbPixelData == null)
      throw new ArgumentException("Either RawJpegBytes or RgbPixelData must be non-null.", nameof(file));

    return LossyEncode(file.RgbPixelData, file.Width, file.Height, 90, JpegMode.Baseline, JpegSubsampling.Chroma444, optimizeHuffman: true, file.IsGrayscale, file.Metadata);
  }

  public static byte[] LosslessTranscode(
    byte[] inputJpeg,
    JpegMode mode,
    bool optimizeHuffman,
    bool stripMetadata,
    ImageMetadata? metadataOverride = null
  ) {
    // Decode to coefficient level using the pure-managed decoder pipeline.
    var image = JpegManagedDecoder.DecodeToCoefficients(inputJpeg);

    // An explicit override replaces only the metadata-shaped segments (EXIF/XMP/IPTC/COM) it
    // recognises; every other segment (JFIF-adjacent APPn, Adobe APP14, unrecognised APPn, ...) is
    // left exactly as decoded, so an override that only touches e.g. Exif can't accidentally erase
    // an unrelated colour-transform marker it never looked at.
    if (metadataOverride != null) {
      var kept = _StripRecognizedMetadataSegments(image.MarkerSegments);
      kept.AddRange(JpegMetadataCodec.ToMarkerSegments(metadataOverride));
      image.MarkerSegments.Clear();
      image.MarkerSegments.AddRange(kept);
    }

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
    bool isGrayscale,
    ImageMetadata? metadata = null
  ) => JpegManagedEncoder.Encode(
    rgbPixelData, width, height, quality, mode, subsampling, optimizeHuffman, isGrayscale,
    stripMetadata: metadata == null, metadataSegments: metadata != null ? JpegMetadataCodec.ToMarkerSegments(metadata) : null);

  /// <summary>Drops segments <see cref="JpegMetadataCodec"/> understands (EXIF/XMP-flavoured APP1,
  /// Photoshop APP13, COM) from a decoded marker list, keeping everything else — the counterpart half
  /// of applying a <see cref="ImageMetadata"/> override during lossless transcode.</summary>
  private static List<JpegMarkerSegment> _StripRecognizedMetadataSegments(List<JpegMarkerSegment> segments) {
    var result = new List<JpegMarkerSegment>();
    foreach (var seg in segments) {
      if (seg.Marker == JpegMarker.COM) continue;
      if (seg.Marker == JpegMarker.APP1 && (_StartsWith(seg.Data, "Exif\0\0"u8) || _StartsWith(seg.Data, "http://ns.adobe.com/xap/1.0/\0"u8))) continue;
      if (seg.Marker == JpegMarker.APP0 + 13 && _StartsWith(seg.Data, "Photoshop 3.0\0"u8)) continue;
      result.Add(seg);
    }
    return result;
  }

  private static bool _StartsWith(byte[] data, ReadOnlySpan<byte> prefix)
    => data.Length >= prefix.Length && data.AsSpan(0, prefix.Length).SequenceEqual(prefix);
}
