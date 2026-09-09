using System;
using System.Buffers.Binary;
using System.IO;
using FileFormat.Iff;
using FileFormat.Ilbm;

namespace FileFormat.IffMultiPalette;

/// <summary>Writes an IFF ILBM with an uncompressed twelve-bit <c>PCHG</c> palette-change chunk.</summary>
public static class IffMultiPaletteWriter {

  private const ushort _Pchg4Bit = 0x0001;
  private const int _PchgHeaderSize = 20;
  private const int _PlaneCount = 4;

  public static byte[] ToBytes(IffMultiPaletteFile file) {
    IffMultiPaletteFile.Validate(file, nameof(file));

    var pchg = _BuildPaletteChanges(file);
    var planar = PlanarConverter.ChunkyToPlanar(file.PixelData, file.Width, file.Height, _PlaneCount);
    var cmap = file.ScanlinePalettes.AsSpan(0, IffMultiPaletteFile.PaletteByteSize);

    var bmhdChunkSize = 8 + BmhdChunk.StructSize;
    var cmapChunkSize = 8 + cmap.Length;
    var pchgChunkSize = 8 + pchg.Length + (pchg.Length & 1);
    var bodyChunkSize = 8 + planar.Length + (planar.Length & 1);
    var formDataSize = checked(4 + bmhdChunkSize + cmapChunkSize + pchgChunkSize + bodyChunkSize);

    using var stream = new MemoryStream(checked(8 + formDataSize));
    _WriteChunkHeader(stream, "FORM", formDataSize);
    stream.Write("ILBM"u8);

    _WriteChunkHeader(stream, "BMHD", BmhdChunk.StructSize);
    Span<byte> bmhdBuffer = stackalloc byte[BmhdChunk.StructSize];
    new BmhdChunk(
      (ushort)file.Width,
      (ushort)file.Height,
      0,
      0,
      (byte)_PlaneCount,
      (byte)IlbmMasking.None,
      (byte)IlbmCompression.None,
      0,
      0,
      1,
      1,
      (short)Math.Min(file.Width, short.MaxValue),
      (short)Math.Min(file.Height, short.MaxValue)
    ).WriteTo(bmhdBuffer);
    stream.Write(bmhdBuffer);

    _WriteChunkHeader(stream, "CMAP", cmap.Length);
    stream.Write(cmap);

    _WriteChunkHeader(stream, "PCHG", pchg.Length);
    stream.Write(pchg);
    if ((pchg.Length & 1) != 0)
      stream.WriteByte(0);

    _WriteChunkHeader(stream, "BODY", planar.Length);
    stream.Write(planar);
    if ((planar.Length & 1) != 0)
      stream.WriteByte(0);

    return stream.ToArray();
  }

  private static byte[] _BuildPaletteChanges(IffMultiPaletteFile file) {
    var lineCount = file.Height - 1;
    var maskSize = (lineCount + 31) / 32 * 4;
    var mask = new byte[maskSize];
    using var changes = new MemoryStream(Math.Max(0, lineCount * 4));

    var minRegister = IffMultiPaletteFile.PaletteEntries;
    var maxRegister = -1;
    Span<byte> changedRegisters = stackalloc byte[IffMultiPaletteFile.PaletteEntries];
    Span<byte> word = stackalloc byte[2];

    for (var line = 0; line < lineCount; ++line) {
      var previousAt = line * IffMultiPaletteFile.PaletteByteSize;
      var currentAt = previousAt + IffMultiPaletteFile.PaletteByteSize;
      var changed = 0;

      for (var register = 0; register < IffMultiPaletteFile.PaletteEntries; ++register) {
        var previous = file.ScanlinePalettes.AsSpan(previousAt + register * 3, 3);
        var current = file.ScanlinePalettes.AsSpan(currentAt + register * 3, 3);
        if (previous.SequenceEqual(current))
          continue;

        changedRegisters[changed++] = (byte)register;
        minRegister = Math.Min(minRegister, register);
        maxRegister = Math.Max(maxRegister, register);
      }

      if (changed == 0)
        continue;
      if (changed > IffMultiPaletteFile.MaxChangesPerLine)
        throw new NotSupportedException(
          $"Scanline {line + 1} changes {changed} palette registers; PCHG 0.6 permits at most {IffMultiPaletteFile.MaxChangesPerLine} Copper-safe changes per non-interlaced line.");

      mask[line >> 3] |= (byte)(1 << (7 - (line & 7)));
      changes.WriteByte((byte)changed);
      changes.WriteByte(0); // ChangeCount32: this writer uses registers 0..15 only.

      for (var i = 0; i < changed; ++i) {
        var register = changedRegisters[i];
        var at = currentAt + register * 3;
        var red = file.ScanlinePalettes[at] / 17;
        var green = file.ScanlinePalettes[at + 1] / 17;
        var blue = file.ScanlinePalettes[at + 2] / 17;
        var packed = (ushort)((register << 12) | (red << 8) | (green << 4) | blue);
        BinaryPrimitives.WriteUInt16BigEndian(word, packed);
        changes.Write(word);
      }
    }

    var changeBytes = changes.ToArray();
    var originalSize = checked(mask.Length + changeBytes.Length);
    var result = new byte[checked(_PchgHeaderSize + originalSize)];
    BinaryPrimitives.WriteUInt16BigEndian(result, 0); // PCHG_COMP_NONE
    BinaryPrimitives.WriteUInt16BigEndian(result.AsSpan(2), _Pchg4Bit);
    BinaryPrimitives.WriteInt16BigEndian(result.AsSpan(4), 1); // CMAP supplies scanline zero.
    BinaryPrimitives.WriteUInt16BigEndian(result.AsSpan(6), (ushort)lineCount);
    BinaryPrimitives.WriteUInt16BigEndian(result.AsSpan(8), (ushort)(maxRegister < 0 ? 0 : minRegister));
    BinaryPrimitives.WriteUInt16BigEndian(result.AsSpan(10), (ushort)Math.Max(maxRegister, 0));
    BinaryPrimitives.WriteUInt16BigEndian(result.AsSpan(12), 0); // no Huffman tree
    BinaryPrimitives.WriteUInt16BigEndian(result.AsSpan(14), 0);
    BinaryPrimitives.WriteUInt32BigEndian(result.AsSpan(16), (uint)originalSize);
    mask.CopyTo(result.AsSpan(_PchgHeaderSize));
    changeBytes.CopyTo(result.AsSpan(_PchgHeaderSize + mask.Length));
    return result;
  }

  private static void _WriteChunkHeader(Stream stream, string chunkId, int size) {
    Span<byte> buffer = stackalloc byte[IffChunkHeader.StructSize];
    new Riff.FourCC(chunkId).WriteTo(buffer);
    BinaryPrimitives.WriteInt32BigEndian(buffer[4..], size);
    stream.Write(buffer);
  }
}
