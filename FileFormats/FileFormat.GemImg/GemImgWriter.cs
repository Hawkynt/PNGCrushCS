using System;
using System.IO;

namespace FileFormat.GemImg;

/// <summary>Assembles GEM IMG file bytes from pixel data.</summary>
public static class GemImgWriter {

  public static byte[] ToBytes(GemImgFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var bytesPerRow = (file.Width + 7) / 8;
    var headerLengthInWords = GemImgHeader.StructSize / 2;

    using var ms = new MemoryStream();

    // Write header
    var headerBytes = new byte[GemImgHeader.StructSize];
    var header = new GemImgHeader(
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
        // Pad if pixel data is shorter than expected
        for (var p = count; p < bytesPerRow; ++p)
          ms.WriteByte(0);
      }
    }

    return ms.ToArray();
  }
}
