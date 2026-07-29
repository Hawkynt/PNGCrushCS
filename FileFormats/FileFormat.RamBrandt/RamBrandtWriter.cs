using System;

namespace FileFormat.RamBrandt;

/// <summary>Assembles Ram Brandt file bytes from a <see cref="RamBrandtFile"/>.</summary>
public static class RamBrandtWriter {

  public static byte[] ToBytes(RamBrandtFile file) {
    var result = new byte[RamBrandtFile.ExpectedFileSize];

    _Copy(file.BitmapData, result, 0, RamBrandtFile.BitmapDataSize);
    _Copy(file.Colors, result, RamBrandtFile.ColorsOffset, RamBrandtFile.ColorCount);
    _Copy(file.DisplayList, result, RamBrandtFile.DisplayListOffset, RamBrandtFile.DisplayListSize);

    return result;
  }

  private static void _Copy(byte[]? source, byte[] destination, int offset, int length) {
    var data = source ?? [];
    data.AsSpan(0, Math.Min(data.Length, length)).CopyTo(destination.AsSpan(offset));
  }
}
