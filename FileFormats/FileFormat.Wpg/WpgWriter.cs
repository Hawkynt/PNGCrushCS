using System;
using System.IO;

namespace FileFormat.Wpg;

/// <summary>Assembles WPG file bytes from pixel data.</summary>
public static class WpgWriter {

  public static byte[] ToBytes(WpgFile file) {
    ArgumentNullException.ThrowIfNull(file);
    return _Assemble(file.PixelData, file.Width, file.Height, file.BitsPerPixel, file.Palette);
  }

  internal static byte[] _Assemble(byte[] pixelData, int width, int height, int bitsPerPixel, byte[]? palette) {
    using var ms = new MemoryStream();

    // Write 16-byte header
    var header = new WpgHeader(
      Magic1: WpgHeader.MagicByte1,
      Magic2: WpgHeader.MagicByte2,
      Magic3: WpgHeader.MagicByte3,
      Magic4: WpgHeader.MagicByte4,
      ProductType: 1,
      FileType: 1,
      MajorVersion: 1,
      MinorVersion: 0,
      EncryptionKey: 0,
      Reserved: 0
    );
    var headerBuf = new byte[WpgHeader.StructSize];
    header.WriteTo(headerBuf);
    ms.Write(headerBuf, 0, headerBuf.Length);

    // StartWpg record (type 15, size 0)
    ms.WriteByte((byte)WpgRecordType.StartWpg);
    ms.WriteByte(0); // size = 0

    // ColorMap record (if palette present)
    if (palette is { Length: > 0 }) {
      ms.WriteByte((byte)WpgRecordType.ColorMap);
      var colorMapSize = WpgColorMapSubHeader.StructSize + palette.Length;

      _WriteRecordSize(ms, colorMapSize);

      var colorMapSub = new WpgColorMapSubHeader(0, (ushort)(palette.Length / 3));
      Span<byte> colorMapSubBuf = stackalloc byte[WpgColorMapSubHeader.StructSize];
      colorMapSub.WriteTo(colorMapSubBuf);
      ms.Write(colorMapSubBuf);

      ms.Write(palette, 0, palette.Length);
    }

    // BitmapType1 record
    ms.WriteByte((byte)WpgRecordType.BitmapType1);

    // Bitmap sub-header: width(2) + height(2) + depth(2) + xdpi(2) + ydpi(2) = 10 bytes + pixel data
    var bitmapSize = WpgBitmapSubHeader.StructSize + pixelData.Length;
    _WriteRecordSize(ms, bitmapSize);

    var bmpSub = new WpgBitmapSubHeader((ushort)width, (ushort)height, (ushort)bitsPerPixel, 96, 96);
    Span<byte> bmpSubBuf = stackalloc byte[WpgBitmapSubHeader.StructSize];
    bmpSub.WriteTo(bmpSubBuf);
    ms.Write(bmpSubBuf);

    // Uncompressed pixel data
    ms.Write(pixelData, 0, pixelData.Length);

    // EndWpg record (type 16, size 0)
    ms.WriteByte((byte)WpgRecordType.EndWpg);
    ms.WriteByte(0); // size = 0

    return ms.ToArray();
  }

  private static void _WriteRecordSize(MemoryStream ms, int size) {
    if (size < 0xFF) {
      ms.WriteByte((byte)size);
    } else if (size <= 0xFFFF) {
      ms.WriteByte(0xFF);
      ms.WriteByte((byte)(size & 0xFF));
      ms.WriteByte((byte)(size >> 8));
    } else {
      ms.WriteByte(0xFE);
      ms.WriteByte((byte)(size & 0xFF));
      ms.WriteByte((byte)((size >> 8) & 0xFF));
      ms.WriteByte((byte)((size >> 16) & 0xFF));
      ms.WriteByte((byte)((size >> 24) & 0xFF));
    }
  }
}
