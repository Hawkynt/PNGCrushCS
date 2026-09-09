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

    // SCBs (200 of the 256 bytes the block reserves; the other 56 stay zero)
    file.Scbs.AsSpan(0, AppleIIgsReader.ScbSize).CopyTo(result.AsSpan(offset));

    // Palettes (256 x 16-bit LE values = 512 bytes), where the hardware keeps them
    var span = result.AsSpan(AppleIIgsReader.PaletteOffset, AppleIIgsReader.PaletteSize);
    for (var i = 0; i < AppleIIgsReader.PaletteEntryCount; ++i)
      BinaryPrimitives.WriteInt16LittleEndian(span[(i * 2)..], file.Palettes[i]);

    return result;
  }
}
