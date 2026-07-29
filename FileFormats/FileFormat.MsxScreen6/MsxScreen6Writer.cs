using System;
using System.Buffers.Binary;

namespace FileFormat.MsxScreen6;

/// <summary>Assembles MSX2 Screen 6 image bytes.</summary>
public static class MsxScreen6Writer {

  public static byte[] ToBytes(MsxScreen6File file) {
    var result = new byte[MsxScreen6File.FileSize];
    result[0] = MsxScreen6File.BsaveMagic;

    // Readers work the picture height out of the end address, so it describes the bitmap.
    BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(3), MsxScreen6File.BsaveEndAddress);

    _Copy(file.PixelData, result, MsxScreen6File.BsaveHeaderSize, MsxScreen6File.PixelDataSize);
    _Copy(file.Palette, result, MsxScreen6File.PaletteOffset, MsxScreen6File.PaletteSize);

    return result;
  }

  private static void _Copy(byte[]? source, byte[] destination, int offset, int length) {
    var data = source ?? [];
    data.AsSpan(0, Math.Min(data.Length, length)).CopyTo(destination.AsSpan(offset));
  }
}
