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
      DataOffset: WpgHeader.RecordsOffset,
      ProductType: WpgHeader.WordPerfect,
      FileType: WpgHeader.GraphicFileType,
      MajorVersion: 1,
      MinorVersion: 0,
      EncryptionKey: 0,
      Reserved: 0
    );
    var headerBuf = new byte[WpgHeader.StructSize];
    header.WriteTo(headerBuf);
    ms.Write(headerBuf, 0, headerBuf.Length);

    // Start of the graphic: the precision the coordinates are in, the version, and the size the
    // records that follow are drawn at. An empty one is enough for our own reader, which takes the
    // size off the bitmap, but not for anything that lays the records out before reading them.
    ms.WriteByte((byte)WpgRecordType.StartWpg);
    _WriteRecordSize(ms, 6);
    ms.WriteByte(1);
    ms.WriteByte(0);
    ms.WriteByte((byte)width);
    ms.WriteByte((byte)(width >> 8));
    ms.WriteByte((byte)height);
    ms.WriteByte((byte)(height >> 8));

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

  /// <summary>Writes a record length in whichever of the three forms it fits.</summary>
  /// <remarks>
  /// A word with its top bit set is the marker for the long form, so a length from 0x8000 up cannot
  /// use the word form even though it fits in one — it has to go long or it reads back as a marker.
  /// </remarks>
  private static void _WriteRecordSize(MemoryStream ms, int size) {
    if (size < 0xFF) {
      ms.WriteByte((byte)size);
      return;
    }

    ms.WriteByte(0xFF);

    if (size < 0x8000) {
      ms.WriteByte((byte)size);
      ms.WriteByte((byte)(size >> 8));
      return;
    }

    var high = (size >> 16) | 0x8000;
    ms.WriteByte((byte)high);
    ms.WriteByte((byte)(high >> 8));
    ms.WriteByte((byte)size);
    ms.WriteByte((byte)(size >> 8));
  }
}
