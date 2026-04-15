using System;
using System.Buffers.Binary;
using System.IO;

namespace FileFormat.ExtendedGemImg;

/// <summary>Assembles Extended GEM Bit Image (XIMG) file bytes from pixel data.</summary>
public static class ExtendedGemImgWriter {

  public static byte[] ToBytes(ExtendedGemImgFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var bytesPerRow = (file.Width + 7) / 8;
    var paletteCount = Math.Min(1 << file.NumPlanes, 256);
    var actualPaletteEntries = Math.Min(paletteCount, file.PaletteData.Length / 3);

    // XIMG extension: 4 bytes marker + 2 bytes color model + palette (3 shorts per entry)
    var ximgExtensionSize = ExtendedGemImgHeader.XimgExtensionFixedSize + actualPaletteEntries * 3 * 2;
    var headerLengthInWords = (ExtendedGemImgHeader.StructSize + ximgExtensionSize) / 2;

    using var ms = new MemoryStream();

    // Write standard GEM IMG header
    var headerBytes = new byte[ExtendedGemImgHeader.StructSize];
    var header = new ExtendedGemImgHeader(
      (short)file.Version,
      (short)headerLengthInWords,
      (short)file.NumPlanes,
      (short)file.PatternLength,
      (short)file.PixelWidth,
      (short)file.PixelHeight,
      (short)file.Width,
      (short)file.Height
    );
    header.WriteTo(headerBytes);
    ms.Write(headerBytes, 0, headerBytes.Length);

    // Write XIMG extension marker "XIMG" + color model via struct
    var extBytes = new byte[ximgExtensionSize];
    var extHeader = new ExtendedGemImgExtensionHeader(
      ExtendedGemImgHeader.XimgMarker1,
      ExtendedGemImgHeader.XimgMarker2,
      (short)file.ColorModel
    );
    extHeader.WriteTo(extBytes.AsSpan());

    // Write palette entries (3 big-endian shorts per entry)
    for (var i = 0; i < actualPaletteEntries * 3; ++i)
      BinaryPrimitives.WriteInt16BigEndian(extBytes.AsSpan(6 + i * 2), file.PaletteData[i]);

    ms.Write(extBytes, 0, extBytes.Length);

    // Encode scan-line data per-plane using bit-string (0x80) opcodes
    for (var plane = 0; plane < file.NumPlanes; ++plane) {
      var planeOffset = plane * bytesPerRow * file.Height;
      for (var row = 0; row < file.Height; ++row) {
        var rowOffset = planeOffset + row * bytesPerRow;
        ms.WriteByte(0x80); // bit string opcode
        ms.WriteByte((byte)bytesPerRow); // count
        var count = Math.Min(bytesPerRow, file.PixelData.Length - rowOffset);
        if (count > 0)
          ms.Write(file.PixelData, rowOffset, count);
        for (var p = count; p < bytesPerRow; ++p)
          ms.WriteByte(0);
      }
    }

    return ms.ToArray();
  }
}
