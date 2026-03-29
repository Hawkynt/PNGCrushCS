using System;

namespace FileFormat.InterPaintHi;

/// <summary>Assembles Commodore 64 InterPaint Hires file bytes from an InterPaintHiFile.</summary>
public static class InterPaintHiWriter {

  public static byte[] ToBytes(InterPaintHiFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var result = new byte[InterPaintHiFile.ExpectedFileSize];
    var offset = 0;

    result[offset] = (byte)(file.LoadAddress & 0xFF);
    result[offset + 1] = (byte)(file.LoadAddress >> 8);
    offset += InterPaintHiFile.LoadAddressSize;

    file.BitmapData.AsSpan(0, InterPaintHiFile.BitmapDataSize).CopyTo(result.AsSpan(offset));
    offset += InterPaintHiFile.BitmapDataSize;

    file.ScreenRam.AsSpan(0, InterPaintHiFile.ScreenRamSize).CopyTo(result.AsSpan(offset));

    return result;
  }
}
