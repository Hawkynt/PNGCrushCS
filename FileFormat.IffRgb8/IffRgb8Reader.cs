using System;
using System.Buffers.Binary;
using System.IO;
using System.Text;
using FileFormat.Core;

namespace FileFormat.IffRgb8;

/// <summary>Reads IFF RGB8 files from bytes, streams, or file paths.</summary>
public static class IffRgb8Reader {

  private const int _MIN_IFF_SIZE = 12; // "FORM" + size + form type
  private const int _NUM_PLANES = 25;

  public static IffRgb8File FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("IFF RGB8 file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static IffRgb8File FromStream(Stream stream) {
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

  public static IffRgb8File FromSpan(ReadOnlySpan<byte> data) => FromBytes(data.ToArray());

  public static IffRgb8File FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length < _MIN_IFF_SIZE)
      throw new InvalidDataException("Data too small for a valid IFF RGB8 file.");

    var span = data.AsSpan();

    // Validate FORM magic
    var formId = Encoding.ASCII.GetString(data, 0, 4);
    if (formId != "FORM")
      throw new InvalidDataException($"Invalid IFF magic: expected 'FORM', got '{formId}'.");

    // Validate RGB8 form type
    var formType = Encoding.ASCII.GetString(data, 8, 4);
    if (formType != "RGB8")
      throw new InvalidDataException($"Invalid IFF form type: expected 'RGB8', got '{formType}'.");

    var formSize = BinaryPrimitives.ReadInt32BigEndian(span[4..]);

    // Parse chunks
    Rgb8BmhdChunk? bmhd = null;
    byte[]? body = null;

    var offset = 12; // skip FORM header + form type
    var endOffset = Math.Min(8 + formSize, data.Length);

    while (offset + 8 <= endOffset) {
      var chunkId = Encoding.ASCII.GetString(data, offset, 4);
      var chunkSize = BinaryPrimitives.ReadInt32BigEndian(span[(offset + 4)..]);
      var chunkDataOffset = offset + 8;

      if (chunkDataOffset + chunkSize > data.Length)
        break;

      switch (chunkId) {
        case "BMHD":
          if (chunkSize >= Rgb8BmhdChunk.StructSize)
            bmhd = Rgb8BmhdChunk.ReadFrom(span[chunkDataOffset..]);
          break;
        case "BODY":
          body = new byte[chunkSize];
          span.Slice(chunkDataOffset, chunkSize).CopyTo(body);
          break;
      }

      // Advance to next chunk (2-byte aligned)
      offset = chunkDataOffset + chunkSize + (chunkSize & 1);
    }

    if (bmhd == null)
      throw new InvalidDataException("IFF RGB8 file missing required BMHD chunk.");

    if (body == null)
      throw new InvalidDataException("IFF RGB8 file missing required BODY chunk.");

    var header = bmhd.Value;
    var width = (int)header.Width;
    var height = (int)header.Height;
    var compression = (IffRgb8Compression)header.Compression;

    // Each pixel is 4 bytes (R, G, B, pad) in the BODY
    var expectedBodySize = width * height * 4;

    // Decompress BODY if needed
    var rawPixelData = compression == IffRgb8Compression.ByteRun1
      ? Rgb8ByteRun1Compressor.Decode(body, expectedBodySize)
      : body;

    // Convert from 4-byte groups (R, G, B, pad) to RGB24 (R, G, B)
    var pixelCount = width * height;
    var rgb24 = PixelConverter.Rgba32ToRgb24(rawPixelData, pixelCount);

    return new IffRgb8File {
      Width = width,
      Height = height,
      Compression = compression,
      PixelData = rgb24,
    };
  }
}
