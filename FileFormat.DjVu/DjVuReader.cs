using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Text;
using FileFormat.DjVu.Codec;

namespace FileFormat.DjVu;

/// <summary>Reads DjVu files from bytes, streams, or file paths.</summary>
public static class DjVuReader {

  /// <summary>The IFF85 magic bytes: "AT&amp;T" followed by "FORM".</summary>
  private static ReadOnlySpan<byte> _Magic => "AT&T"u8;

  private static ReadOnlySpan<byte> _FormTag => "FORM"u8;

  private const int _MIN_SIZE = 20; // 4 (AT&T) + 4 (FORM) + 4 (form size) + 4 (DJVU) + 4 (at least one chunk ID)
  private const int _INFO_SIZE = 10;

  /// <summary>The chunk ID used for our simplified raw pixel data storage.</summary>
  internal const string PM44_CHUNK_ID = "PM44";

  /// <summary>Custom marker bytes at the start of PM44 data to identify our raw pixel format.</summary>
  private static ReadOnlySpan<byte> _RawPixelMarker => "RAWP"u8;

  public static DjVuFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("DjVu file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static DjVuFile FromStream(Stream stream) {
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

  public static DjVuFile FromSpan(ReadOnlySpan<byte> data) => FromBytes(data.ToArray());

  public static DjVuFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length < _MIN_SIZE)
      throw new InvalidDataException("Data too small for a valid DjVu file.");

    var span = data.AsSpan();

    // Validate AT&T magic
    if (!span[..4].SequenceEqual(_Magic))
      throw new InvalidDataException("Invalid DjVu magic: expected 'AT&T'.");

    // Validate FORM tag
    if (!span[4..8].SequenceEqual(_FormTag))
      throw new InvalidDataException("Invalid DjVu container: expected 'FORM' tag.");

    // Read FORM size (big-endian)
    var formSize = (int)BinaryPrimitives.ReadUInt32BigEndian(span[8..]);

    // Validate DJVU form type
    var formType = Encoding.ASCII.GetString(data, 12, 4);
    if (formType != "DJVU")
      throw new InvalidDataException($"Invalid DjVu form type: expected 'DJVU', got '{formType}'.");

    // Parse chunks starting after FORM header (offset 16)
    var chunks = _ParseChunks(data, 16, Math.Min(12 + formSize, data.Length));

    // Extract INFO chunk and collect IW44 chunks
    DjVuChunk? infoChunk = null;
    DjVuChunk? pm44Chunk = null;
    var bg44Chunks = new List<DjVuChunk>();
    var rawChunks = new List<DjVuChunk>();

    foreach (var chunk in chunks) {
      switch (chunk.ChunkId) {
        case "INFO":
          infoChunk = chunk;
          break;
        case PM44_CHUNK_ID:
          pm44Chunk ??= chunk;
          break;
        case "BG44":
        case "FG44":
          bg44Chunks.Add(chunk);
          break;
        default:
          rawChunks.Add(chunk);
          break;
      }
    }

    if (infoChunk == null)
      throw new InvalidDataException("DjVu file missing required INFO chunk.");

    if (infoChunk.Data.Length < _INFO_SIZE)
      throw new InvalidDataException($"INFO chunk too small: expected at least {_INFO_SIZE} bytes, got {infoChunk.Data.Length}.");

    var infoSpan = infoChunk.Data.AsSpan();
    var width = BinaryPrimitives.ReadUInt16LittleEndian(infoSpan);
    var height = BinaryPrimitives.ReadUInt16LittleEndian(infoSpan[2..]);
    var versionMinor = infoSpan[4];
    var versionMajor = infoSpan[5];
    var dpi = BinaryPrimitives.ReadUInt16LittleEndian(infoSpan[6..]);
    var gamma = infoSpan[8];
    var flags = infoSpan[9];

    if (width == 0)
      throw new InvalidDataException("Invalid DjVu width: 0.");
    if (height == 0)
      throw new InvalidDataException("Invalid DjVu height: 0.");

    // Try to decode pixel data from IW44 chunks (BG44), then fall back to PM44
    var pixelData = _DecodeIw44PixelData(bg44Chunks, width, height)
      ?? _DecodePixelData(pm44Chunk, width, height);

    return new DjVuFile {
      Width = width,
      Height = height,
      VersionMajor = versionMajor,
      VersionMinor = versionMinor,
      Dpi = dpi == 0 ? 300 : dpi,
      Gamma = gamma,
      Flags = flags,
      PixelData = pixelData,
      RawChunks = rawChunks,
    };
  }

  internal static List<DjVuChunk> ParseChunks(byte[] data, int offset, int endOffset) => _ParseChunks(data, offset, endOffset);

  private static List<DjVuChunk> _ParseChunks(byte[] data, int offset, int endOffset) {
    var chunks = new List<DjVuChunk>();
    while (offset + 8 <= endOffset) {
      var chunkId = Encoding.ASCII.GetString(data, offset, 4);
      var chunkSize = (int)BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(offset + 4));
      offset += 8;

      var dataEnd = Math.Min(offset + chunkSize, endOffset);
      var chunkDataLength = dataEnd - offset;
      if (chunkDataLength < 0)
        break;

      var chunkData = new byte[chunkDataLength];
      data.AsSpan(offset, chunkDataLength).CopyTo(chunkData.AsSpan(0));

      chunks.Add(new DjVuChunk {
        ChunkId = chunkId,
        Data = chunkData,
      });

      // Advance past data, word-aligned (2-byte boundary)
      offset += chunkSize;
      if ((offset & 1) != 0)
        ++offset;
    }
    return chunks;
  }

  /// <summary>Attempts to decode pixel data from BG44/FG44 IW44 wavelet chunks.</summary>
  private static byte[]? _DecodeIw44PixelData(List<DjVuChunk> bg44Chunks, int width, int height) {
    if (bg44Chunks.Count == 0)
      return null;

    try {
      var decoder = new Iw44Decoder(width, height, isColor: true);
      foreach (var chunk in bg44Chunks) {
        if (!decoder.DecodeSlice(chunk.Data))
          break;
      }
      return decoder.Reconstruct();
    } catch {
      // Fall back to blank if IW44 decode fails
      return null;
    }
  }

  private static byte[] _DecodePixelData(DjVuChunk? pm44Chunk, int width, int height) {
    var expectedSize = width * height * 3;

    if (pm44Chunk == null)
      return new byte[expectedSize];

    var pm44Data = pm44Chunk.Data;

    // Check for our custom raw pixel marker
    if (pm44Data.Length >= 4 && pm44Data.AsSpan(0, 4).SequenceEqual(_RawPixelMarker)) {
      var pixelStart = 4;
      var available = pm44Data.Length - pixelStart;
      var pixelData = new byte[expectedSize];
      pm44Data.AsSpan(pixelStart, Math.Min(available, expectedSize)).CopyTo(pixelData.AsSpan(0));
      return pixelData;
    }

    // Unknown PM44 format -- return blank image (dimensions are still correct from INFO)
    return new byte[expectedSize];
  }
}
