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

    // A GEM IMG holds one coded scanline per plane per row, taken row by row — this wrote all of
    // plane nought before any of plane one, which is the same way round the reader used to have it,
    // so the pair round-tripped with each other while agreeing with no file either would be given.
    for (var row = 0; row < file.Height; ++row) {
      for (var plane = 0; plane < file.NumPlanes; ++plane) {
        var rowOffset = (plane * file.Height + row) * bytesPerRow;
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
