using System;
using System.Collections.Generic;
using FileFormat.Core;

namespace FileFormat.WebP;

/// <summary>Carries metadata in and out of the chunks WebP defines for it.</summary>
/// <remarks>
/// The format states three: <c>EXIF</c> holds a TIFF stream, <c>XMP </c> — the trailing space is
/// part of the name — holds a UTF-8 packet, and <c>ICCP</c> holds a colour profile. The reader
/// already kept these as raw bytes and the writer already emitted them, so a WebP read and written
/// here never lost them. What was missing was the bridge to the interchange model, without which
/// metadata could not travel through a WebP to any other format — which is most of what the model
/// is for.
/// <para/>
/// The raw chunks are kept alongside, so a file that only passes through comes out byte for byte as
/// it went in rather than re-encoded from the parse.
/// </remarks>
public static class WebPMetadataCodec {

  internal const string ExifChunk = "EXIF", XmpChunk = "XMP ", IccChunk = "ICCP";

  /// <summary>Reads what the chunks carry, or null when they carry nothing.</summary>
  public static ImageMetadata? Read(IReadOnlyList<(string ChunkId, byte[] Data)> chunks) {
    if (chunks == null || chunks.Count == 0)
      return null;

    byte[]? exif = null, xmp = null, icc = null;
    foreach (var (id, payload) in chunks)
      switch (id) {
        case ExifChunk: exif ??= payload; break;
        case XmpChunk: xmp ??= payload; break;
        case IccChunk: icc ??= payload; break;
      }

    var metadata = new ImageMetadata {
      // The chunk holds a TIFF stream, which is what EXIF is, so the same parser reads it.
      Exif = exif is { Length: > 0 } ? ExifCodec.TryParse(exif) : null,
      XmpPacket = xmp is { Length: > 0 } ? xmp : null,
      IccProfile = icc is { Length: > 0 } ? icc : null,
    };

    return metadata.IsEmpty ? null : metadata;
  }

  /// <summary>Builds the chunks a picture's metadata belongs in, in the order the format lists them.</summary>
  public static List<(string ChunkId, byte[] Data)> Write(ImageMetadata? metadata) {
    var chunks = new List<(string, byte[])>();
    if (metadata == null)
      return chunks;

    if (metadata.IccProfile is { Length: > 0 } icc)
      chunks.Add((IccChunk, icc));

    if (metadata.Exif != null) {
      var exif = ExifCodec.Write(metadata.Exif);
      if (exif.Length > 0)
        chunks.Add((ExifChunk, exif));
    }

    if (metadata.XmpPacket is { Length: > 0 } xmp)
      chunks.Add((XmpChunk, xmp));

    return chunks;
  }
}
