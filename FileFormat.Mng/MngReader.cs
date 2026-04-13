using System;
using System.Collections.Generic;
using System.IO;

namespace FileFormat.Mng;

/// <summary>Reads MNG files from bytes, streams, or file paths.</summary>
public static class MngReader {

  private static readonly byte[] _MNG_SIGNATURE = { 0x8A, 0x4D, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
  private static readonly byte[] _PNG_SIGNATURE = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };

  public static MngFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("MNG file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static MngFile FromStream(Stream stream) {
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

  public static MngFile FromSpan(ReadOnlySpan<byte> data) {

    if (data.Length < 8)
      throw new InvalidDataException("Data too small for a valid MNG file.");

    var span = data;

    for (var i = 0; i < _MNG_SIGNATURE.Length; ++i)
      if (span[i] != _MNG_SIGNATURE[i])
        throw new InvalidDataException("Invalid MNG signature.");

    var offset = 8;
    MngHeader header = default;
    var frames = new List<byte[]>();
    var termAction = MngTermAction.ShowLast;
    var numPlays = 0;
    var foundMhdr = false;

    // Accumulate PNG chunks for current frame
    List<byte[]>? currentFrameChunks = null;

    while (offset + 8 <= data.Length) {
      var chunkLength = (int)_ReadUInt32BE(span[offset..]);
      var chunkType = _ReadChunkType(span[(offset + 4)..]);
      var chunkDataStart = offset + 8;

      if (chunkDataStart + chunkLength + 4 > data.Length)
        break;

      switch (chunkType) {
        case "MHDR":
          if (chunkLength >= MngHeader.StructSize)
            header = MngHeader.ReadFrom(span[chunkDataStart..]);
          foundMhdr = true;
          break;
        case "TERM":
          if (chunkLength >= 1)
            termAction = (MngTermAction)span[chunkDataStart];
          if (chunkLength >= 10)
            numPlays = (int)_ReadUInt32BE(span[(chunkDataStart + 6)..]);
          break;
        case "IHDR":
          currentFrameChunks = [];
          _AddChunkRaw(currentFrameChunks, span, offset, chunkLength);
          break;
        case "IEND":
          if (currentFrameChunks != null) {
            _AddChunkRaw(currentFrameChunks, span, offset, chunkLength);
            frames.Add(_AssemblePng(currentFrameChunks));
            currentFrameChunks = null;
          }
          break;
        case "MEND":
          break;
        default:
          if (currentFrameChunks != null)
            _AddChunkRaw(currentFrameChunks, span, offset, chunkLength);
          break;
      }

      offset = chunkDataStart + chunkLength + 4; // skip CRC
    }

    if (!foundMhdr)
      throw new InvalidDataException("Missing MHDR chunk in MNG file.");

    return new MngFile {
      Width = (int)header.Width,
      Height = (int)header.Height,
      TicksPerSecond = (int)header.TicksPerSecond,
      NumPlays = numPlays,
      TermAction = termAction,
      Frames = frames
    };
    }

  public static MngFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }

  private static void _AddChunkRaw(List<byte[]> chunks, ReadOnlySpan<byte> data, int chunkStart, int chunkLength) {
    var totalChunkSize = 4 + 4 + chunkLength + 4; // length + type + data + crc
    var chunk = new byte[totalChunkSize];
    data.Slice(chunkStart, totalChunkSize).CopyTo(chunk);
    chunks.Add(chunk);
  }

  private static byte[] _AssemblePng(List<byte[]> chunks) {
    var totalSize = _PNG_SIGNATURE.Length;
    foreach (var chunk in chunks)
      totalSize += chunk.Length;

    var result = new byte[totalSize];
    _PNG_SIGNATURE.AsSpan(0, _PNG_SIGNATURE.Length).CopyTo(result);
    var offset = _PNG_SIGNATURE.Length;
    foreach (var chunk in chunks) {
      chunk.AsSpan(0, chunk.Length).CopyTo(result.AsSpan(offset));
      offset += chunk.Length;
    }

    return result;
  }

  private static uint _ReadUInt32BE(ReadOnlySpan<byte> data)
    => (uint)(data[0] << 24 | data[1] << 16 | data[2] << 8 | data[3]);

  private static string _ReadChunkType(ReadOnlySpan<byte> data)
    => $"{(char)data[0]}{(char)data[1]}{(char)data[2]}{(char)data[3]}";
}
