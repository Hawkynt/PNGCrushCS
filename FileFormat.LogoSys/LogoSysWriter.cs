using System;

namespace FileFormat.LogoSys;

/// <summary>Assembles Windows 95/98 boot logo (logo.sys) bytes from a <see cref="LogoSysFile"/>.</summary>
public static class LogoSysWriter {

  public static byte[] ToBytes(LogoSysFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var result = new byte[LogoSysFile.FileSize];

    var paletteLen = Math.Min(file.Palette.Length, LogoSysFile.PaletteSize);
    file.Palette.AsSpan(0, paletteLen).CopyTo(result);

    var pixelLen = Math.Min(file.PixelData.Length, LogoSysFile.PixelDataSize);
    file.PixelData.AsSpan(0, pixelLen).CopyTo(result.AsSpan(LogoSysFile.PaletteSize));

    return result;
  }
}
