using System;
using FileFormat.Core;

namespace FileFormat.MsxScreen10;

/// <summary>Assembles MSX2+ Screen 10 picture bytes.</summary>
public static class MsxScreen10Writer {

  public static byte[] ToBytes(MsxScreen10File file) {
    var result = new byte[MsxScreen10File.FileSize];
    MsxGraphics.WriteBsaveHeader(result, MsxScreen10File.BsaveEndAddress);

    _Copy(file.PixelData, result, MsxScreen10File.PixelDataOffset, MsxScreen10File.PixelDataSize);
    _Copy(file.Palette, result, MsxScreen10File.PaletteOffset, MsxScreen10File.PaletteSize);

    return result;
  }

  private static void _Copy(byte[]? source, byte[] destination, int offset, int length) {
    var data = source ?? [];
    data.AsSpan(0, Math.Min(data.Length, length)).CopyTo(destination.AsSpan(offset));
  }
}
