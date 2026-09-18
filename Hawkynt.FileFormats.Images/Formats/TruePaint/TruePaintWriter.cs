using System;

namespace FileFormat.TruePaint;

/// <summary>Assembles True Paint (.mci) file bytes from a TruePaintFile.</summary>
public static class TruePaintWriter {

  public static byte[] ToBytes(TruePaintFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var result = new byte[TruePaintFile.ExpectedFileSize];

    result[0] = (byte)(file.LoadAddress & 0xFF);
    result[1] = (byte)(file.LoadAddress >> 8);

    // Each section goes where the machine loads it, and the bytes between them are the gaps those
    // addresses leave — the video matrix's last 24 bytes and the two the bitmaps are padded up to.
    file.ScreenRam1.AsSpan(0, TruePaintFile.ScreenRamSize).CopyTo(result.AsSpan(TruePaintFile.ScreenRam1Offset));
    file.BitmapData1.AsSpan(0, TruePaintFile.BitmapDataSize).CopyTo(result.AsSpan(TruePaintFile.BitmapData1Offset));
    file.BitmapData2.AsSpan(0, TruePaintFile.BitmapDataSize).CopyTo(result.AsSpan(TruePaintFile.BitmapData2Offset));
    file.ScreenRam2.AsSpan(0, TruePaintFile.ScreenRamSize).CopyTo(result.AsSpan(TruePaintFile.ScreenRam2Offset));
    file.ColorRam.AsSpan(0, TruePaintFile.ColorRamSize).CopyTo(result.AsSpan(TruePaintFile.ColorRamOffset));

    result[TruePaintFile.BackgroundOffset] = file.BackgroundColor;
    result[TruePaintFile.BorderOffset] = file.BorderColor;

    return result;
  }
}
