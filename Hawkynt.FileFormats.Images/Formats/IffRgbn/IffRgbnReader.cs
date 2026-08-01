using System;
using System.Buffers.Binary;
using System.IO;
using System.Text;
using FileFormat.Core;

namespace FileFormat.IffRgbn;

/// <summary>Reads IFF RGBN files from bytes, streams, or file paths.</summary>
public static class IffRgbnReader {

  private const int _MIN_IFF_SIZE = 12; // "FORM" + size + form type
  private const byte _NUM_PLANES = 13;

  public static IffRgbnFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("IFF RGBN file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static IffRgbnFile FromStream(Stream stream) {
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

  public static IffRgbnFile FromSpan(ReadOnlySpan<byte> data) {

    if (data.Length < _MIN_IFF_SIZE)
      throw new InvalidDataException("Data too small for a valid IFF RGBN file.");

    var span = data;

    var formId = Encoding.ASCII.GetString(data.Slice(0, 4));
    if (formId != "FORM")
      throw new InvalidDataException($"Invalid IFF magic: expected 'FORM', got '{formId}'.");

    var formType = Encoding.ASCII.GetString(data.Slice(8, 4));
    if (formType != "RGBN")
      throw new InvalidDataException($"Invalid IFF form type: expected 'RGBN', got '{formType}'.");

    var formSize = BinaryPrimitives.ReadInt32BigEndian(span[4..]);

    RgbnBmhdChunk? bmhd = null;
    byte[]? body = null;

    var offset = 12;
    var endOffset = Math.Min(8 + formSize, data.Length);

    while (offset + 8 <= endOffset) {
      var chunkId = Encoding.ASCII.GetString(data.Slice(offset, 4));
      var chunkSize = BinaryPrimitives.ReadInt32BigEndian(span[(offset + 4)..]);
      var chunkDataOffset = offset + 8;

      if (chunkDataOffset + chunkSize > data.Length)
        break;

      switch (chunkId) {
        case "BMHD":
          if (chunkSize >= RgbnBmhdChunk.StructSize)
            bmhd = RgbnBmhdChunk.ReadFrom(span[chunkDataOffset..]);
          break;
        case "BODY":
          body = new byte[chunkSize];
          span.Slice(chunkDataOffset, chunkSize).CopyTo(body);
          break;
      }

      offset = chunkDataOffset + chunkSize + (chunkSize & 1);
    }

    if (bmhd == null)
      throw new InvalidDataException("IFF RGBN file missing required BMHD chunk.");

    if (body == null)
      throw new InvalidDataException("IFF RGBN file missing required BODY chunk.");

    var header = bmhd.Value;
    var width = (int)header.Width;
    var height = (int)header.Height;
    // A run count of zero is not one pixel: it says the count follows in a byte of its own, and a
    // zero there in turn says a sixteen-bit count follows. Reading it as one, which is what this
    // did, drops every long run to a single pixel and leaves the picture short.
    var rgb24 = AmigaRgbRuns.Unpack(body, width, height, deep: false);

    return new IffRgbnFile {
      Width = width,
      Height = height,
      PixelData = rgb24,
    };
  }

  public static IffRgbnFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
