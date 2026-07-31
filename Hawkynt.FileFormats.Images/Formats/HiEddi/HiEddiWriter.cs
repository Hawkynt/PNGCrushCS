using System;

namespace FileFormat.HiEddi;

/// <summary>Assembles HiEddi C64 hires file bytes from a HiEddiFile.</summary>
public static class HiEddiWriter {

  public static byte[] ToBytes(HiEddiFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var result = new byte[HiEddiFile.ExpectedFileSize];

    result[0] = (byte)(file.LoadAddress & 0xFF);
    result[1] = (byte)(file.LoadAddress >> 8);

    // The matrix follows the bitmap's eight whole pages, not its eight thousand used bytes.
    file.BitmapData.AsSpan(0, Math.Min(file.BitmapData.Length, HiEddiFile.BitmapDataSize))
      .CopyTo(result.AsSpan(HiEddiFile.BitmapOffset));
    file.ScreenRam.AsSpan(0, Math.Min(file.ScreenRam.Length, HiEddiFile.ScreenRamSize))
      .CopyTo(result.AsSpan(HiEddiFile.ScreenRamOffset));

    return result;
  }
}
