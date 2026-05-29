using System;

namespace FileFormat.TruePaint;

/// <summary>Assembles True Paint (.mci) file bytes from a TruePaintFile.</summary>
public static class TruePaintWriter {

  public static byte[] ToBytes(TruePaintFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var result = new byte[TruePaintFile.ExpectedFileSize];

    result[0] = (byte)(file.LoadAddress & 0xFF);
    result[1] = (byte)(file.LoadAddress >> 8);

    var offset = TruePaintFile.LoadAddressSize;

    file.BitmapData1.AsSpan(0, TruePaintFile.BitmapDataSize).CopyTo(result.AsSpan(offset));
    offset += TruePaintFile.BitmapDataSize;

    file.ScreenRam1.AsSpan(0, TruePaintFile.ScreenRamSize).CopyTo(result.AsSpan(offset));
    offset += TruePaintFile.ScreenRamSize;

    file.BitmapData2.AsSpan(0, TruePaintFile.BitmapDataSize).CopyTo(result.AsSpan(offset));
    offset += TruePaintFile.BitmapDataSize;

    file.ScreenRam2.AsSpan(0, TruePaintFile.ScreenRamSize).CopyTo(result.AsSpan(offset));
    offset += TruePaintFile.ScreenRamSize;

    file.ColorRam.AsSpan(0, TruePaintFile.ColorRamSize).CopyTo(result.AsSpan(offset));
    offset += TruePaintFile.ColorRamSize;

    result[offset] = file.BackgroundColor;
    result[offset + 1] = file.BorderColor;

    // Remaining 430 bytes are padding (already zeroed)

    return result;
  }
}
