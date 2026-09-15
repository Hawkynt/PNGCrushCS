using System;

namespace FileFormat.Codecs.Hap;

/// <summary>Writes one block-compressed texture as a Hap image section.</summary>
/// <remarks>
/// A single chunk keeps the compact whole-texture form. More than one chunk uses Hap's Decode
/// Instructions Container with the required second-stage-compressor and chunk-size tables. Each
/// chunk is independently offered to Snappy and kept compressed only where that chunk shrinks, so
/// incompressible regions remain legal uncompressed chunks without preventing the rest from being
/// decoded in parallel.
/// </remarks>
internal static class HapFrameEncoding {

  private const byte _COMPRESSOR_NONE = 0xA0;
  private const byte _COMPRESSOR_SNAPPY = 0xB0;
  private const byte _COMPRESSOR_COMPLEX = 0xC0;

  private const byte _DECODE_INSTRUCTIONS_CONTAINER = 0x01;
  private const byte _CHUNK_COMPRESSOR_TABLE = 0x02;
  private const byte _CHUNK_SIZE_TABLE = 0x03;

  private const byte _CHUNK_UNCOMPRESSED = 0x0A;
  private const byte _CHUNK_SNAPPY = 0x0B;

  /// <summary>Writes one texture section with exactly <paramref name="chunkCount"/> chunks.</summary>
  public static byte[] WriteImageSection(ReadOnlySpan<byte> texture, byte formatCode, int chunkCount) {
    if (texture.IsEmpty)
      throw new ArgumentException("A Hap texture cannot be empty.", nameof(texture));
    if (chunkCount <= 0)
      throw new ArgumentOutOfRangeException(nameof(chunkCount), "A Hap texture needs at least one chunk.");
    if (texture.Length % chunkCount != 0)
      throw new ArgumentException(
        $"A {texture.Length}-byte Hap texture cannot be split into {chunkCount} equal block-aligned chunks.",
        nameof(chunkCount));

    if (chunkCount == 1)
      return _WriteSingleChunk(texture, formatCode);

    return _WriteChunked(texture, formatCode, chunkCount);
  }

  private static byte[] _WriteSingleChunk(ReadOnlySpan<byte> texture, byte formatCode) {
    var compressed = HapSnappyEncoder.Compress(texture);
    var useSnappy = compressed.Length < texture.Length;
    var payload = useSnappy ? compressed.AsSpan() : texture;
    var compressor = useSnappy ? _COMPRESSOR_SNAPPY : _COMPRESSOR_NONE;
    return WriteLongSection((byte)(compressor | formatCode), payload);
  }

  private static byte[] _WriteChunked(ReadOnlySpan<byte> texture, byte formatCode, int chunkCount) {
    var uncompressedChunkLength = texture.Length / chunkCount;
    var stored = new byte[chunkCount][];
    var compressors = new byte[chunkCount];
    var storedBytes = 0;

    for (var chunk = 0; chunk < chunkCount; ++chunk) {
      var source = texture.Slice(chunk * uncompressedChunkLength, uncompressedChunkLength);
      var compressed = HapSnappyEncoder.Compress(source);
      if (compressed.Length < source.Length) {
        stored[chunk] = compressed;
        compressors[chunk] = _CHUNK_SNAPPY;
      } else {
        stored[chunk] = source.ToArray();
        compressors[chunk] = _CHUNK_UNCOMPRESSED;
      }

      storedBytes = checked(storedBytes + stored[chunk].Length);
    }

    var compressorTable = _WriteShortSection(_CHUNK_COMPRESSOR_TABLE, compressors);
    var sizes = new byte[checked(chunkCount * 4)];
    for (var chunk = 0; chunk < chunkCount; ++chunk)
      _WriteU32(sizes, chunk * 4, (uint)stored[chunk].Length);
    var sizeTable = _WriteShortSection(_CHUNK_SIZE_TABLE, sizes);

    var instructions = new byte[checked(compressorTable.Length + sizeTable.Length)];
    compressorTable.CopyTo(instructions, 0);
    sizeTable.CopyTo(instructions, compressorTable.Length);
    var instructionSection = _WriteShortSection(_DECODE_INSTRUCTIONS_CONTAINER, instructions);

    var payload = new byte[checked(instructionSection.Length + storedBytes)];
    instructionSection.CopyTo(payload, 0);
    var at = instructionSection.Length;
    foreach (var chunk in stored) {
      chunk.CopyTo(payload, at);
      at += chunk.Length;
    }

    return WriteLongSection((byte)(_COMPRESSOR_COMPLEX | formatCode), payload);
  }

  /// <summary>Writes the eight-byte Hap section-header form used for complete image sections.</summary>
  public static byte[] WriteLongSection(byte type, ReadOnlySpan<byte> payload) {
    var result = new byte[checked(8 + payload.Length)];
    result[3] = type;
    _WriteU32(result, 4, (uint)payload.Length);
    payload.CopyTo(result.AsSpan(8));
    return result;
  }

  private static byte[] _WriteShortSection(byte type, ReadOnlySpan<byte> payload) {
    if (payload.IsEmpty || payload.Length > 0x00FF_FFFF)
      throw new ArgumentOutOfRangeException(nameof(payload), "A four-byte Hap section header needs a 1..0xFFFFFF-byte payload.");

    var result = new byte[checked(4 + payload.Length)];
    result[0] = (byte)payload.Length;
    result[1] = (byte)(payload.Length >> 8);
    result[2] = (byte)(payload.Length >> 16);
    result[3] = type;
    payload.CopyTo(result.AsSpan(4));
    return result;
  }

  private static void _WriteU32(Span<byte> destination, int offset, uint value) {
    destination[offset] = (byte)value;
    destination[offset + 1] = (byte)(value >> 8);
    destination[offset + 2] = (byte)(value >> 16);
    destination[offset + 3] = (byte)(value >> 24);
  }
}
