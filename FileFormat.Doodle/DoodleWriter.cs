using System;

namespace FileFormat.Doodle;

/// <summary>Assembles Commodore 64 Doodle hires file bytes from a DoodleFile.</summary>
public static class DoodleWriter {

  public static byte[] ToBytes(DoodleFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var result = new byte[DoodleFile.ExpectedFileSize];
    var offset = 0;

    result[offset] = (byte)(file.LoadAddress & 0xFF);
    result[offset + 1] = (byte)(file.LoadAddress >> 8);
    offset += DoodleFile.LoadAddressSize;

    file.BitmapData.AsSpan(0, Math.Min(file.BitmapData.Length, DoodleFile.BitmapDataSize)).CopyTo(result.AsSpan(offset));
    offset += DoodleFile.BitmapDataSize;

    file.ScreenRam.AsSpan(0, Math.Min(file.ScreenRam.Length, DoodleFile.ScreenRamSize)).CopyTo(result.AsSpan(offset));

    // Padding (216 bytes) remains as zeros

    return result;
  }
}
