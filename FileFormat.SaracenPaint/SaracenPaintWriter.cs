using System;

namespace FileFormat.SaracenPaint;

/// <summary>Assembles Saracen Paint C64 hires file bytes from a SaracenPaintFile.</summary>
public static class SaracenPaintWriter {

  public static byte[] ToBytes(SaracenPaintFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var result = new byte[SaracenPaintFile.ExpectedFileSize];
    var offset = 0;

    result[offset] = (byte)(file.LoadAddress & 0xFF);
    result[offset + 1] = (byte)(file.LoadAddress >> 8);
    offset += SaracenPaintFile.LoadAddressSize;

    file.ScreenRam.AsSpan(0, SaracenPaintFile.ScreenRamSize).CopyTo(result.AsSpan(offset));
    offset += SaracenPaintFile.ScreenRamSize;

    file.BitmapData.AsSpan(0, SaracenPaintFile.BitmapDataSize).CopyTo(result.AsSpan(offset));

    return result;
  }
}
