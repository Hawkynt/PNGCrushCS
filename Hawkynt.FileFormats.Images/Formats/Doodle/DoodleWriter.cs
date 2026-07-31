using System;

namespace FileFormat.Doodle;

/// <summary>Assembles Commodore 64 Doodle hires file bytes from a DoodleFile.</summary>
public static class DoodleWriter {

  public static byte[] ToBytes(DoodleFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var result = new byte[DoodleFile.ExpectedFileSize];

    result[0] = (byte)(file.LoadAddress & 0xFF);
    result[1] = (byte)(file.LoadAddress >> 8);

    // The video matrix comes first and the bitmap after its page, not after its thousand bytes.
    file.ScreenRam.AsSpan(0, Math.Min(file.ScreenRam.Length, DoodleFile.ScreenRamSize))
      .CopyTo(result.AsSpan(DoodleFile.ScreenRamOffset));
    file.BitmapData.AsSpan(0, Math.Min(file.BitmapData.Length, DoodleFile.BitmapDataSize))
      .CopyTo(result.AsSpan(DoodleFile.BitmapOffset));

    return result;
  }
}
