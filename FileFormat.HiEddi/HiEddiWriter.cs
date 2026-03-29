using System;

namespace FileFormat.HiEddi;

/// <summary>Assembles HiEddi C64 hires file bytes from a HiEddiFile.</summary>
public static class HiEddiWriter {

  public static byte[] ToBytes(HiEddiFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var result = new byte[HiEddiFile.ExpectedFileSize];
    var offset = 0;

    result[offset] = (byte)(file.LoadAddress & 0xFF);
    result[offset + 1] = (byte)(file.LoadAddress >> 8);
    offset += HiEddiFile.LoadAddressSize;

    file.BitmapData.AsSpan(0, HiEddiFile.BitmapDataSize).CopyTo(result.AsSpan(offset));
    offset += HiEddiFile.BitmapDataSize;

    file.ScreenRam.AsSpan(0, HiEddiFile.ScreenRamSize).CopyTo(result.AsSpan(offset));

    return result;
  }
}
