using System;
using System.Buffers.Binary;
using System.IO;
using System.Text;

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
    var pixelCount = width * height;
    var rgb24 = new byte[pixelCount * 3];

    var dstIndex = 0;
    var srcIndex = 0;
    while (dstIndex < pixelCount && srcIndex + 1 < body.Length) {
      var hi = body[srcIndex];
      var lo = body[srcIndex + 1];
      srcIndex += 2;

      var r = (byte)((hi >> 4) * 17);
      var g = (byte)((hi & 0x0F) * 17);
      var b = (byte)((lo >> 4) * 17);
      var repeat = lo & 0x07;
      var count = repeat > 0 ? repeat + 1 : 1;

      for (var i = 0; i < count && dstIndex < pixelCount; ++i) {
        var off = dstIndex * 3;
        rgb24[off] = r;
        rgb24[off + 1] = g;
        rgb24[off + 2] = b;
        ++dstIndex;
      }
    }

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
