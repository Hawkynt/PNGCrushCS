using System;
using System.Collections.Generic;

namespace FileFormat.ZeissLsm;

/// <summary>Assembles Zeiss LSM (minimal TIFF) bytes from a <see cref="ZeissLsmFile"/>.</summary>
public static class ZeissLsmWriter {

  public static byte[] ToBytes(ZeissLsmFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var output = new List<byte>();

    // TIFF header: byte order (II), magic (42), IFD offset
    output.Add(0x49); output.Add(0x49); // II
    _WriteUInt16LE(output, ZeissLsmFile.TiffMagicLE);

    const int ifdOffset = 8;
    _WriteUInt32LE(output, ifdOffset);

    // IFD with 5 entries
    const int entryCount = 5;
    _WriteUInt16LE(output, (ushort)entryCount);

    var pixelDataOffset = (uint)(ifdOffset + 2 + entryCount * 12 + 4); // +4 for next IFD pointer
    var pixelDataSize = (uint)file.PixelData.Length;

    // Tag 256: ImageWidth
    _WriteIfdEntry(output, 256, 3, 1, (uint)file.Width);
    // Tag 257: ImageLength
    _WriteIfdEntry(output, 257, 3, 1, (uint)file.Height);
    // Tag 273: StripOffsets
    _WriteIfdEntry(output, 273, 4, 1, pixelDataOffset);
    // Tag 277: SamplesPerPixel
    _WriteIfdEntry(output, 277, 3, 1, (uint)file.Channels);
    // Tag 279: StripByteCounts
    _WriteIfdEntry(output, 279, 4, 1, pixelDataSize);

    // Next IFD pointer (0 = no more IFDs)
    _WriteUInt32LE(output, 0);

    // Pixel data
    output.AddRange(file.PixelData);

    return output.ToArray();
  }

  private static void _WriteIfdEntry(List<byte> output, ushort tag, ushort type, uint count, uint value) {
    _WriteUInt16LE(output, tag);
    _WriteUInt16LE(output, type);
    _WriteUInt32LE(output, count);
    _WriteUInt32LE(output, value);
  }

  private static void _WriteUInt16LE(List<byte> output, ushort value) {
    output.Add((byte)(value & 0xFF));
    output.Add((byte)(value >> 8));
  }

  private static void _WriteUInt32LE(List<byte> output, uint value) {
    output.Add((byte)(value & 0xFF));
    output.Add((byte)((value >> 8) & 0xFF));
    output.Add((byte)((value >> 16) & 0xFF));
    output.Add((byte)(value >> 24));
  }
}
