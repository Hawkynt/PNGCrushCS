using System;
using System.Collections.Generic;
using System.IO;

namespace FileFormat.Codecs.Hap;

/// <summary>
/// Walks a Hap frame's sections and hands back the one or two textures it holds, each still in its
/// own block-compressed form.
/// </summary>
/// <remarks>
/// A Hap frame is one top-level section. Its type byte names a pixel format and, in its high nibble,
/// how the section's data gets from the file to that pixel format: as-is, through Snappy whole, or —
/// the "consult decode instructions" forms — as a nested instructions section naming how many chunks
/// follow, each one's own second-stage compressor, and each one's size, immediately followed by the
/// chunks themselves. The one type byte that names no pixel format at all, 0x0D, instead holds one or
/// two further top-level sections whose textures are combined into a single picture — the only
/// combination the format defines being a Scaled YCoCg DXT5 colour image with a separate RGTC1/BC4
/// alpha image (Hap Q Alpha).
/// </remarks>
internal static class HapFrameParser {

  private const byte _MULTI_IMAGE = 0x0D;
  private const byte _DECODE_INSTRUCTIONS_CONTAINER = 0x01;
  private const byte _CHUNK_COMPRESSOR_TABLE = 0x02;
  private const byte _CHUNK_SIZE_TABLE = 0x03;
  private const byte _CHUNK_OFFSET_TABLE = 0x04;

  private const byte _CHUNK_UNCOMPRESSED = 0x0A;
  private const byte _CHUNK_SNAPPY = 0x0B;

  /// <summary>Parses one whole frame, returning its one or two textures.</summary>
  public static IReadOnlyList<HapTexture> ParseFrame(ReadOnlySpan<byte> data) {
    var top = HapSection.ReadAt(data, 0, "A Hap frame");
    return _ParseTopLevel(data, top, "The frame's top-level section");
  }

  private static IReadOnlyList<HapTexture> _ParseTopLevel(ReadOnlySpan<byte> data, HapSection section, string what) {
    if (section.Type == _MULTI_IMAGE)
      return _ParseMultiImage(data, section, what);

    return [_ParseSingleImage(data, section, what)];
  }

  private static IReadOnlyList<HapTexture> _ParseMultiImage(ReadOnlySpan<byte> data, HapSection section, string what) {
    var results = new List<HapTexture>(2);
    var at = section.DataOffset;
    var end = section.EndOffset;

    while (at < end) {
      var inner = HapSection.ReadAt(data, at, $"{what}'s multiple-image section");
      if (inner.EndOffset > end)
        throw new InvalidDataException($"{what}'s multiple-image section holds a nested section that runs past its own end.");

      if (inner.Type == _MULTI_IMAGE)
        throw new InvalidDataException($"{what}'s multiple-image section nests another multiple-image section, which the format does not allow.");

      results.Add(_ParseSingleImage(data, inner, $"{what}'s nested image"));
      at = inner.EndOffset;

      if (results.Count > 2)
        throw new NotSupportedException($"{what}'s multiple-image section holds more than two images; the format defines no combination that wide.");
    }

    if (results.Count is not (1 or 2))
      throw new InvalidDataException($"{what}'s multiple-image section holds {results.Count} images.");

    return results;
  }

  private static HapTexture _ParseSingleImage(ReadOnlySpan<byte> data, HapSection section, string what) {
    var format = _PixelFormatOf(section.Type, what);
    var compressor = (byte)(section.Type & 0xF0);
    var payload = data.Slice(section.DataOffset, section.DataLength);

    var bytes = compressor switch {
      0xA0 => payload.ToArray(),
      0xB0 => HapSnappyDecoder.Decompress(payload),
      0xC0 => _DecodeWithInstructions(payload, what),
      _ => throw new NotSupportedException($"{what} states type byte 0x{section.Type:X2}, whose high nibble names no second-stage compressor this codec knows."),
    };

    return new(format, bytes);
  }

  private static HapPixelFormat _PixelFormatOf(byte type, string what) => (byte)(type & 0x0F) switch {
    0x0B => HapPixelFormat.Dxt1Rgb,
    0x0E => HapPixelFormat.Dxt5Rgba,
    0x0F => HapPixelFormat.Dxt5ScaledYCoCg,
    0x0C => HapPixelFormat.Bc7Rgba,
    0x01 => HapPixelFormat.Rgtc1Alpha,
    0x02 => HapPixelFormat.Bc6UnsignedFloat,
    0x03 => HapPixelFormat.Bc6SignedFloat,
    _ => throw new NotSupportedException($"{what} states type byte 0x{type:X2}, which names no pixel format this codec knows."),
  };

  /// <summary>
  /// The "consult decode instructions" form: a Decode Instructions Container naming how the frame
  /// data that follows it is split into chunks, then the chunks themselves.
  /// </summary>
  private static byte[] _DecodeWithInstructions(ReadOnlySpan<byte> payload, string what) {
    var container = HapSection.ReadAt(payload, 0, $"{what}'s decode-instructions section");
    if (container.Type != _DECODE_INSTRUCTIONS_CONTAINER)
      throw new NotSupportedException($"{what} names a second-stage compressor of 'consult decode instructions' but opens with section type 0x{container.Type:X2} rather than the Decode Instructions Container.");

    var instructions = payload.Slice(container.DataOffset, container.DataLength);
    var frameData = payload[container.EndOffset..];

    byte[]? compressorTable = null;
    uint[]? sizeTable = null;
    uint[]? offsetTable = null;

    var at = 0;
    while (at < instructions.Length) {
      var section = HapSection.ReadAt(instructions, at, $"{what}'s decode-instructions container");
      var sectionData = instructions.Slice(section.DataOffset, section.DataLength);

      switch (section.Type) {
        case _CHUNK_COMPRESSOR_TABLE:
          compressorTable = sectionData.ToArray();
          break;

        case _CHUNK_SIZE_TABLE:
          sizeTable = _ReadUInt32Table(sectionData, $"{what}'s chunk size table");
          break;

        case _CHUNK_OFFSET_TABLE:
          offsetTable = _ReadUInt32Table(sectionData, $"{what}'s chunk offset table");
          break;

        // A decoder is required to press on past a section type it does not recognise, provided the
        // ones it does recognise are enough to decode the frame.
      }

      at = section.EndOffset;
    }

    if (compressorTable == null || sizeTable == null)
      throw new InvalidDataException($"{what}'s decode instructions name no chunk compressor table and chunk size table, and the frame data cannot be split into chunks without both.");

    if (compressorTable.Length != sizeTable.Length)
      throw new InvalidDataException($"{what}'s decode instructions carry {compressorTable.Length} chunk compressors for {sizeTable.Length} chunk sizes; the format requires one of each per chunk.");

    if (offsetTable != null && offsetTable.Length != sizeTable.Length)
      throw new InvalidDataException($"{what}'s decode instructions carry {offsetTable.Length} chunk offsets for {sizeTable.Length} chunk sizes; the format requires one of each per chunk, or none at all.");

    var chunkCount = sizeTable.Length;
    var totalSize = 0L;
    for (var i = 0; i < chunkCount; ++i)
      totalSize += sizeTable[i];

    if (totalSize > int.MaxValue)
      throw new InvalidDataException($"{what}'s decode instructions name chunks totalling {totalSize} bytes, more than a single frame can hold.");

    var chunks = new byte[chunkCount][];
    var runningOffset = 0L;
    var decodedLength = 0;

    for (var i = 0; i < chunkCount; ++i) {
      var offset = offsetTable != null ? offsetTable[i] : runningOffset;
      var size = sizeTable[i];
      if (offset + size > frameData.Length)
        throw new InvalidDataException($"{what}'s chunk {i} runs from byte {offset} for {size} bytes, past the {frameData.Length} bytes of frame data that follow the decode instructions.");

      var chunkPayload = frameData.Slice((int)offset, (int)size);
      var chunkCompressor = compressorTable[i];
      chunks[i] = chunkCompressor switch {
        _CHUNK_UNCOMPRESSED => chunkPayload.ToArray(),
        _CHUNK_SNAPPY => HapSnappyDecoder.Decompress(chunkPayload),
        _ => throw new NotSupportedException($"{what}'s chunk {i} names second-stage compressor 0x{chunkCompressor:X2}, which is neither uncompressed nor Snappy."),
      };

      decodedLength += chunks[i].Length;
      runningOffset += size;
    }

    var result = new byte[decodedLength];
    var at2 = 0;
    foreach (var chunk in chunks) {
      chunk.CopyTo(result, at2);
      at2 += chunk.Length;
    }

    return result;
  }

  private static uint[] _ReadUInt32Table(ReadOnlySpan<byte> data, string what) {
    if (data.Length % 4 != 0)
      throw new InvalidDataException($"{what} holds {data.Length} bytes, not a whole number of four-byte fields.");

    var table = new uint[data.Length / 4];
    for (var i = 0; i < table.Length; ++i) {
      var o = i * 4;
      table[i] = (uint)(data[o] | (data[o + 1] << 8) | (data[o + 2] << 16) | (data[o + 3] << 24));
    }

    return table;
  }
}
