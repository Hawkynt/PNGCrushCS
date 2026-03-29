using System;

namespace FileFormat.AppleShr;

/// <summary>Assembles Apple IIgs Super Hi-Res file bytes from an AppleShrFile.</summary>
public static class AppleShrWriter {

  public static byte[] ToBytes(AppleShrFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var result = new byte[AppleShrFile.ExpectedFileSize];
    var offset = 0;

    // Pixel data (32000 bytes)
    file.PixelData.AsSpan(0, Math.Min(file.PixelData.Length, AppleShrFile.PixelDataSize)).CopyTo(result.AsSpan(offset));
    offset += AppleShrFile.PixelDataSize;

    // Scanline control bytes (200 bytes)
    file.ScanlineControl.AsSpan(0, Math.Min(file.ScanlineControl.Length, AppleShrFile.ScbSize)).CopyTo(result.AsSpan(offset));
    offset += AppleShrFile.ScbSize;

    // Padding (56 bytes, zeros)
    offset += AppleShrFile.PaddingSize;

    // Palette (512 bytes)
    file.Palette.AsSpan(0, Math.Min(file.Palette.Length, AppleShrFile.PaletteSize)).CopyTo(result.AsSpan(offset));

    return result;
  }
}
