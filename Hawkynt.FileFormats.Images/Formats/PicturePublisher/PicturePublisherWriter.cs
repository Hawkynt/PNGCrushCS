using System;
using System.Buffers.Binary;
using System.IO;
using System.IO.Compression;

namespace FileFormat.PicturePublisher;

/// <summary>Writes a minimal Picture Publisher 5 document with one opaque full-canvas RGB object.</summary>
public static class PicturePublisherWriter {

  private const ushort _RecordObjectHeader = 1;
  private const ushort _RecordImage = 2;
  private const int _ObjectHeaderSize = 106;
  private const int _CompressionZlib = 213;

  public static byte[] ToBytes(PicturePublisherFile file) {
    if (file.Width is < 1 or > 32768 || file.Height is < 1 or > 32768)
      throw new ArgumentException($"Picture Publisher dimensions must be 1..32768; got {file.Width}x{file.Height}.", nameof(file));
    var expected = checked(file.Width * file.Height * 3);
    if (file.PixelData == null || file.PixelData.Length < expected)
      throw new ArgumentException($"Picture Publisher needs {expected} RGB bytes.", nameof(file));

    var raster = _Raster(file.PixelData.AsSpan(0, expected), file.Width, file.Height);
    var total = checked(PicturePublisherFile.HeaderSize + 6 + _ObjectHeaderSize + 6 + raster.Length);
    var output = new byte[total];
    PicturePublisherFile.Signature.CopyTo(output);
    BinaryPrimitives.WriteUInt32LittleEndian(output.AsSpan(18, 4), (uint)file.Width);
    BinaryPrimitives.WriteUInt32LittleEndian(output.AsSpan(22, 4), (uint)file.Height);
    BinaryPrimitives.WriteUInt32LittleEndian(output.AsSpan(26, 4), (uint)(file.Resolution > 0 ? file.Resolution : 96));
    BinaryPrimitives.WriteUInt16LittleEndian(output.AsSpan(30, 2), 3);

    var at = PicturePublisherFile.HeaderSize;
    BinaryPrimitives.WriteUInt32LittleEndian(output.AsSpan(at, 4), _ObjectHeaderSize);
    BinaryPrimitives.WriteUInt16LittleEndian(output.AsSpan(at + 4, 2), _RecordObjectHeader);
    at += 6;
    // Name/unused fields stay zero. Rectangle coordinates are inclusive in the reader.
    BinaryPrimitives.WriteInt32LittleEndian(output.AsSpan(at + 38, 4), 0);
    BinaryPrimitives.WriteInt32LittleEndian(output.AsSpan(at + 42, 4), 0);
    BinaryPrimitives.WriteInt32LittleEndian(output.AsSpan(at + 46, 4), file.Width - 1);
    BinaryPrimitives.WriteInt32LittleEndian(output.AsSpan(at + 50, 4), file.Height - 1);
    BinaryPrimitives.WriteUInt32LittleEndian(output.AsSpan(at + 54, 4), 255);
    at += _ObjectHeaderSize;

    BinaryPrimitives.WriteUInt32LittleEndian(output.AsSpan(at, 4), (uint)raster.Length);
    BinaryPrimitives.WriteUInt16LittleEndian(output.AsSpan(at + 4, 2), _RecordImage);
    at += 6;
    raster.CopyTo(output, at);
    return output;
  }

  private static byte[] _Raster(ReadOnlySpan<byte> pixels, int width, int height) {
    byte[] compressed;
    using (var memory = new MemoryStream()) {
      using (var zlib = new ZLibStream(memory, CompressionLevel.Optimal, leaveOpen: true))
        zlib.Write(pixels);
      compressed = memory.ToArray();
    }

    const int entries = 10;
    const int directoryAt = 8;
    const int directoryBytes = 2 + entries * 12 + 4;
    const int bitsAt = directoryAt + directoryBytes;
    const int stripAt = bitsAt + 6;
    var result = new byte[checked(stripAt + compressed.Length)];
    result[0] = (byte)'I'; result[1] = (byte)'I'; result[2] = 42; result[3] = 0;
    BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(4, 4), directoryAt);
    BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(directoryAt, 2), entries);

    var e = directoryAt + 2;
    _Entry(result, ref e, 256, 4, 1, width);              // ImageWidth
    _Entry(result, ref e, 257, 4, 1, height);             // ImageLength
    _Entry(result, ref e, 258, 3, 3, bitsAt);             // BitsPerSample
    _Entry(result, ref e, 259, 3, 1, _CompressionZlib);   // Compression
    _Entry(result, ref e, 262, 3, 1, 2);                  // PhotometricInterpretation RGB
    _Entry(result, ref e, 273, 4, 1, stripAt);            // StripOffsets
    _Entry(result, ref e, 277, 3, 1, 3);                  // SamplesPerPixel
    _Entry(result, ref e, 278, 4, 1, height);             // RowsPerStrip
    _Entry(result, ref e, 284, 3, 1, 1);                  // PlanarConfiguration chunky
    _Entry(result, ref e, 317, 3, 1, 1);                  // Predictor none
    // next IFD stays zero
    BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(bitsAt, 2), 8);
    BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(bitsAt + 2, 2), 8);
    BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(bitsAt + 4, 2), 8);
    compressed.CopyTo(result, stripAt);
    return result;
  }

  private static void _Entry(byte[] data, ref int at, ushort tag, ushort type, uint count, int value) {
    BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(at, 2), tag);
    BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(at + 2, 2), type);
    BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(at + 4, 4), count);
    if (type == 3 && count == 1)
      BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(at + 8, 2), checked((ushort)value));
    else
      BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(at + 8, 4), checked((uint)value));
    at += 12;
  }
}
