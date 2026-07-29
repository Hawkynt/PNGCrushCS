using System;
using System.Buffers.Binary;

namespace FileFormat.AppleIIgs;

/// <summary>Assembles Apple IIGS Super Hi-Res ($C1) file bytes from an <see cref="AppleIIgsFile"/>.</summary>
public static class AppleIIgsWriter {

  public static byte[] ToBytes(AppleIIgsFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var result = new byte[AppleIIgsReader.FileSize];
    var offset = 0;

    // Pixel data (32000 bytes)
    file.PixelData.AsSpan(0, AppleIIgsReader.PixelDataSize).CopyTo(result.AsSpan(offset));
    offset += AppleIIgsReader.PixelDataSize;

    // SCBs (200 bytes)
    file.Scbs.AsSpan(0, AppleIIgsReader.ScbSize).CopyTo(result.AsSpan(offset));
    offset += AppleIIgsReader.ScbSize;

    // Palettes (256 x 16-bit LE values = 512 bytes)
    var span = result.AsSpan(offset, AppleIIgsReader.PaletteSize);
    for (var i = 0; i < AppleIIgsReader.PaletteEntryCount; ++i)
      BinaryPrimitives.WriteInt16LittleEndian(span[(i * 2)..], file.Palettes[i]);

    // Remaining 56 bytes are zero padding (already zeroed by array allocation)

    return result;
  }
}
