using System;
using System.Buffers.Binary;
using System.IO;
using System.Text;

namespace FileFormat.Exr;

/// <summary>Assembles OpenEXR file bytes from pixel data.</summary>
public static class ExrWriter {

  public static byte[] ToBytes(ExrFile file) {
    ArgumentNullException.ThrowIfNull(file);

    using var ms = new MemoryStream();

    // Magic header (8 bytes)
    var headerBytes = new byte[ExrMagicHeader.StructSize];
    var magic = new ExrMagicHeader(ExrMagicHeader.ExpectedMagic, ExrMagicHeader.ExpectedVersion);
    magic.WriteTo(headerBytes);
    ms.Write(headerBytes);

    // Write required attributes
    _WriteAttribute(ms, "channels", "chlist", _BuildChannelList(file));
    _WriteAttribute(ms, "compression", "compression", [(byte)file.Compression]);
    _WriteAttribute(ms, "dataWindow", "box2i", _BuildBox2i(0, 0, file.Width - 1, file.Height - 1));
    _WriteAttribute(ms, "displayWindow", "box2i", _BuildBox2i(0, 0, file.Width - 1, file.Height - 1));
    _WriteAttribute(ms, "lineOrder", "lineOrder", [(byte)file.LineOrder]);
    _WriteAttribute(ms, "pixelAspectRatio", "float", _BuildFloat(1.0f));
    _WriteAttribute(ms, "screenWindowCenter", "v2f", _BuildV2f(0.0f, 0.0f));
    _WriteAttribute(ms, "screenWindowWidth", "float", _BuildFloat(1.0f));

    // End of header
    ms.WriteByte(0);

    // Calculate bytes per scanline
    var bytesPerScanline = 0;
    foreach (var ch in file.Channels) {
      var bytesPerPixel = ch.PixelType switch {
        ExrPixelType.UInt => 4,
        ExrPixelType.Half => 2,
        ExrPixelType.Float => 4,
        _ => 4
      };
      bytesPerScanline += bytesPerPixel * file.Width;
    }

    // Write offset table placeholder — we'll fill it in later
    var offsetTablePosition = (int)ms.Position;
    var scanlineCount = file.Height;
    var offsetTableBytes = new byte[scanlineCount * 8];
    ms.Write(offsetTableBytes);

    // Write scanline blocks and record offsets
    var offsets = new long[scanlineCount];
    for (var y = 0; y < scanlineCount; ++y) {
      offsets[y] = ms.Position;

      // Y coordinate (int32)
      var yCoordBytes = new byte[4];
      BinaryPrimitives.WriteInt32LittleEndian(yCoordBytes, y);
      ms.Write(yCoordBytes);

      // Pixel data size (int32)
      var dataSize = Math.Min(bytesPerScanline, file.PixelData.Length - y * bytesPerScanline);
      if (dataSize < 0)
        dataSize = 0;

      var dataSizeBytes = new byte[4];
      BinaryPrimitives.WriteInt32LittleEndian(dataSizeBytes, dataSize);
      ms.Write(dataSizeBytes);

      // Pixel data
      if (dataSize > 0)
        ms.Write(file.PixelData, y * bytesPerScanline, dataSize);
    }

    // Go back and fill in the offset table
    var result = ms.ToArray();
    for (var i = 0; i < scanlineCount; ++i)
      BinaryPrimitives.WriteInt64LittleEndian(result.AsSpan(offsetTablePosition + i * 8), offsets[i]);

    return result;
  }

  private static void _WriteAttribute(MemoryStream ms, string name, string typeName, byte[] value) {
    // Name (null-terminated)
    var nameBytes = Encoding.ASCII.GetBytes(name);
    ms.Write(nameBytes);
    ms.WriteByte(0);

    // Type name (null-terminated)
    var typeBytes = Encoding.ASCII.GetBytes(typeName);
    ms.Write(typeBytes);
    ms.WriteByte(0);

    // Value size (int32 LE)
    var sizeBytes = new byte[4];
    BinaryPrimitives.WriteInt32LittleEndian(sizeBytes, value.Length);
    ms.Write(sizeBytes);

    // Value
    ms.Write(value);
  }

  private static byte[] _BuildChannelList(ExrFile file) {
    using var ms = new MemoryStream();

    foreach (var ch in file.Channels) {
      // Channel name (null-terminated)
      var nameBytes = Encoding.ASCII.GetBytes(ch.Name);
      ms.Write(nameBytes);
      ms.WriteByte(0);

      // pixel type (int32)
      var buf = new byte[16];
      BinaryPrimitives.WriteInt32LittleEndian(buf.AsSpan(0), (int)ch.PixelType);
      // pLinear (1 byte) + reserved (3 bytes) = 0
      BinaryPrimitives.WriteInt32LittleEndian(buf.AsSpan(8), ch.XSampling);
      BinaryPrimitives.WriteInt32LittleEndian(buf.AsSpan(12), ch.YSampling);
      ms.Write(buf);
    }

    // End of channel list (null byte)
    ms.WriteByte(0);

    return ms.ToArray();
  }

  private static byte[] _BuildBox2i(int xMin, int yMin, int xMax, int yMax) {
    var buf = new byte[16];
    BinaryPrimitives.WriteInt32LittleEndian(buf.AsSpan(0), xMin);
    BinaryPrimitives.WriteInt32LittleEndian(buf.AsSpan(4), yMin);
    BinaryPrimitives.WriteInt32LittleEndian(buf.AsSpan(8), xMax);
    BinaryPrimitives.WriteInt32LittleEndian(buf.AsSpan(12), yMax);
    return buf;
  }

  private static byte[] _BuildFloat(float value) {
    var buf = new byte[4];
    BinaryPrimitives.WriteSingleLittleEndian(buf, value);
    return buf;
  }

  private static byte[] _BuildV2f(float x, float y) {
    var buf = new byte[8];
    BinaryPrimitives.WriteSingleLittleEndian(buf.AsSpan(0), x);
    BinaryPrimitives.WriteSingleLittleEndian(buf.AsSpan(4), y);
    return buf;
  }
}
