using System;
using System.Buffers.Binary;
using System.IO;

namespace FileFormat.XBin;

public static class XBinWriter {

  public static byte[] ToBytes(XBinFile file) {
    ArgumentNullException.ThrowIfNull(file.Cells);
    if (file.ColumnCount is <= 0 or > 4096 || file.RowCount is <= 0 or > 4096)
      throw new InvalidOperationException($"XBIN dimensions out of range: {file.ColumnCount}×{file.RowCount}.");
    if (file.FontHeight is <= 0 or > 32)
      throw new InvalidOperationException($"XBIN font height out of range: {file.FontHeight}.");

    using var ms = new MemoryStream();
    using var w = new BinaryWriter(ms);
    w.Write("XBIN"u8);
    w.Write((byte)0x1A);
    Span<byte> u16 = stackalloc byte[2];
    BinaryPrimitives.WriteUInt16LittleEndian(u16, (ushort)file.ColumnCount); w.Write(u16);
    BinaryPrimitives.WriteUInt16LittleEndian(u16, (ushort)file.RowCount);    w.Write(u16);
    w.Write((byte)file.FontHeight);
    w.Write((byte)file.Flags);

    if ((file.Flags & XBinFlags.Palette) != 0) {
      if (file.Palette is null || file.Palette.Length < 48)
        throw new InvalidOperationException("XBIN with Palette flag needs a 48-byte palette.");
      w.Write(file.Palette, 0, 48);
    }
    if ((file.Flags & XBinFlags.Font) != 0) {
      var glyphCount = (file.Flags & XBinFlags.Font512) != 0 ? 512 : 256;
      var fontBytes = glyphCount * file.FontHeight;
      if (file.Font is null || file.Font.Length < fontBytes)
        throw new InvalidOperationException($"XBIN with Font flag needs ≥ {fontBytes} font bytes.");
      w.Write(file.Font, 0, fontBytes);
    }

    var useBlinkBit = (file.Flags & XBinFlags.NonBlink) == 0;
    // Always write uncompressed pairs for simplicity. The reader handles RLE for incoming files; we
    // emit the lossless plain form on save (the file may declare Compressed in Flags, but we strip
    // that bit in the output header to keep this writer's promise honest).
    var flagsOut = file.Flags & ~XBinFlags.Compressed;
    // Rewrite the flags byte if we cleared the bit.
    if (flagsOut != file.Flags) {
      var pos = ms.Position;
      ms.Position = 10;
      ms.WriteByte((byte)flagsOut);
      ms.Position = pos;
    }
    foreach (var cell in file.Cells) {
      w.Write(cell.CodePoint);
      w.Write(cell.AttributeByte);
    }
    _ = useBlinkBit; // attribute encoding already accounts for blink bit at the cell level.
    return ms.ToArray();
  }
}
