using System;
using System.Collections.Generic;
using System.IO;
using FileFormat.Jbig2.Codec;

namespace FileFormat.Jbig2;

/// <summary>Reads JBIG2 files from bytes, streams, or file paths.</summary>
public static class Jbig2Reader {

  /// <summary>JBIG2 standalone file magic: 97 4A 42 32 0D 0A 1A 0A.</summary>
  internal static readonly byte[] Magic = [0x97, 0x4A, 0x42, 0x32, 0x0D, 0x0A, 0x1A, 0x0A];

  /// <summary>Minimum file size: 8 (magic) + 1 (flags) = 9 bytes.</summary>
  private const int _MinFileSize = 9;

  public static Jbig2File FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("JBIG2 file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static Jbig2File FromStream(Stream stream) {
    ArgumentNullException.ThrowIfNull(stream);
    if (stream.CanSeek) {
      var data = new byte[stream.Length - stream.Position];
      stream.ReadExactly(data);
      return FromBytes(data);
    }
    using var ms = new MemoryStream();
    stream.CopyTo(ms);
    return FromBytes(ms.ToArray());
  }

  public static Jbig2File FromSpan(ReadOnlySpan<byte> data) => FromBytes(data.ToArray());

  public static Jbig2File FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length < _MinFileSize)
      throw new InvalidDataException($"Data too small for a valid JBIG2 file: expected at least {_MinFileSize} bytes, got {data.Length}.");

    // Validate magic
    for (var i = 0; i < Magic.Length; ++i)
      if (data[i] != Magic[i])
        throw new InvalidDataException("Invalid JBIG2 file magic signature.");

    var offset = 8;

    // Parse file header flags
    var flags = data[offset++];
    var isSequential = (flags & 0x01) != 0;
    var hasKnownPageCount = (flags & 0x02) == 0; // bit 1: 0 = known, 1 = unknown

    var pageCount = 0;
    if (hasKnownPageCount) {
      if (offset + 4 > data.Length)
        throw new InvalidDataException("Data too small for page count.");

      pageCount = _ReadInt32BE(data, offset);
      offset += 4;
    }

    // Parse all segments
    var segments = new List<Jbig2Segment>();
    while (offset < data.Length) {
      var segment = _ParseSegment(data, ref offset);
      if (segment == null)
        break;

      segments.Add(segment);
      if (segment.Type == Jbig2SegmentType.EndOfFile)
        break;
    }

    // Process segments using the full codec pipeline
    var context = new Jbig2SegmentParser.DecodingContext();
    foreach (var segment in segments)
      Jbig2SegmentParser.ProcessSegment(segment, context);

    // Extract page dimensions and bitmap from the decoded context
    var width = context.CurrentPage?.Width ?? 0;
    var height = context.CurrentPage?.Height ?? 0;
    var pixelData = context.CurrentPage?.Bitmap ?? [];

    return new Jbig2File {
      Width = width,
      Height = height,
      PixelData = pixelData,
      Segments = [.. segments],
    };
  }

  private static Jbig2Segment? _ParseSegment(byte[] data, ref int offset) {
    if (offset + 6 > data.Length)
      return null;

    // Segment number (4 bytes)
    var segmentNumber = _ReadInt32BE(data, offset);
    offset += 4;

    // Header flags (1 byte)
    var headerFlags = data[offset++];
    var segmentType = (Jbig2SegmentType)(headerFlags & 0x3F);
    var pageAssocSizeLarge = (headerFlags & 0x40) != 0;
    var deferredNonRetain = (headerFlags & 0x80) != 0;

    // Referred-to segment count
    if (offset >= data.Length)
      return null;

    var referredCountByte = data[offset++];
    var referredCount = (referredCountByte >> 5) & 0x07;
    int[] referredSegments;

    if (referredCount < 5) {
      // Short form: count is in bits 5-7, referred segment numbers follow
      referredSegments = new int[referredCount];
      var refNumSize = segmentNumber <= 255 ? 1 : segmentNumber <= 65535 ? 2 : 4;
      for (var i = 0; i < referredCount; ++i) {
        if (offset + refNumSize > data.Length)
          return null;

        referredSegments[i] = refNumSize switch {
          1 => data[offset],
          2 => (data[offset] << 8) | data[offset + 1],
          _ => _ReadInt32BE(data, offset)
        };
        offset += refNumSize;
      }
    } else {
      // Long form: count follows as 4 bytes (29 bits)
      if (offset + 3 > data.Length)
        return null;

      var longCount = ((referredCountByte & 0x1F) << 24) | (data[offset] << 16) | (data[offset + 1] << 8) | data[offset + 2];
      offset += 3;
      referredSegments = new int[longCount];
      var refNumSize = segmentNumber <= 255 ? 1 : segmentNumber <= 65535 ? 2 : 4;
      for (var i = 0; i < longCount; ++i) {
        if (offset + refNumSize > data.Length)
          return null;

        referredSegments[i] = refNumSize switch {
          1 => data[offset],
          2 => (data[offset] << 8) | data[offset + 1],
          _ => _ReadInt32BE(data, offset)
        };
        offset += refNumSize;
      }
    }

    // Page association
    int pageAssociation;
    if (pageAssocSizeLarge) {
      if (offset + 4 > data.Length)
        return null;

      pageAssociation = _ReadInt32BE(data, offset);
      offset += 4;
    } else {
      if (offset >= data.Length)
        return null;

      pageAssociation = data[offset++];
    }

    // Data length (4 bytes)
    if (offset + 4 > data.Length)
      return null;

    var dataLength = _ReadInt32BE(data, offset);
    offset += 4;

    // For end-of-file and end-of-page, data length may be 0
    byte[] segmentData;
    if (dataLength > 0 && offset + dataLength <= data.Length) {
      segmentData = new byte[dataLength];
      data.AsSpan(offset, dataLength).CopyTo(segmentData.AsSpan(0));
      offset += dataLength;
    } else if (dataLength == 0) {
      segmentData = [];
    } else {
      // Data extends to end of available data
      var remaining = data.Length - offset;
      segmentData = new byte[remaining];
      if (remaining > 0)
        data.AsSpan(offset, remaining).CopyTo(segmentData.AsSpan(0));
      offset = data.Length;
    }

    return new Jbig2Segment {
      Number = segmentNumber,
      Type = segmentType,
      DeferredNonRetain = deferredNonRetain,
      ReferredSegments = referredSegments,
      PageAssociation = pageAssociation,
      Data = segmentData,
    };
  }

  private static int _ReadInt32BE(byte[] data, int offset)
    => (data[offset] << 24) | (data[offset + 1] << 16) | (data[offset + 2] << 8) | data[offset + 3];
}
