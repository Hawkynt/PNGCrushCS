using System;

namespace FileFormat.Hireslace;

/// <summary>Assembles C64 Hireslace Editor (.hle) file bytes from a <see cref="HireslaceFile"/>.</summary>
public static class HireslaceWriter {

  public static byte[] ToBytes(HireslaceFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var result = new byte[HireslaceFile.ExpectedFileSize];
    var offset = 0;

    // Load address (2 bytes LE)
    result[offset] = (byte)(file.LoadAddress & 0xFF);
    result[offset + 1] = (byte)(file.LoadAddress >> 8);

    // Frame 1: bitmap + screen
    file.Bitmap1.AsSpan(0, HireslaceFile.BitmapDataSize).CopyTo(result.AsSpan(HireslaceFile.Bitmap1Offset));

    file.Screen1.AsSpan(0, HireslaceFile.ScreenDataSize).CopyTo(result.AsSpan(HireslaceFile.Screen1Offset));

    // Frame 2: bitmap + screen
    file.Bitmap2.AsSpan(0, HireslaceFile.BitmapDataSize).CopyTo(result.AsSpan(HireslaceFile.Bitmap2Offset));

    file.Screen2.AsSpan(0, HireslaceFile.ScreenDataSize).CopyTo(result.AsSpan(HireslaceFile.Screen2Offset));

    return result;
  }
}
