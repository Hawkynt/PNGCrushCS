using System;

namespace FileFormat.AmicaPaint;

/// <summary>Assembles Commodore 64 Amica Paint (.ami) file bytes from an AmicaPaintFile.</summary>
public static class AmicaPaintWriter {

  public static byte[] ToBytes(AmicaPaintFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var result = new byte[AmicaPaintFile.ExpectedFileSize];
    var offset = 0;

    result[offset] = (byte)(file.LoadAddress & 0xFF);
    result[offset + 1] = (byte)(file.LoadAddress >> 8);
    offset += AmicaPaintFile.LoadAddressSize;

    file.BitmapData.AsSpan(0, Math.Min(file.BitmapData.Length, AmicaPaintFile.BitmapDataSize)).CopyTo(result.AsSpan(offset));
    offset += AmicaPaintFile.BitmapDataSize;

    file.ScreenRam.AsSpan(0, Math.Min(file.ScreenRam.Length, AmicaPaintFile.ScreenRamSize)).CopyTo(result.AsSpan(offset));
    offset += AmicaPaintFile.ScreenRamSize;

    file.ColorRam.AsSpan(0, Math.Min(file.ColorRam.Length, AmicaPaintFile.ColorRamSize)).CopyTo(result.AsSpan(offset));
    offset += AmicaPaintFile.ColorRamSize;

    result[offset] = file.BackgroundColor;

    return result;
  }
}
