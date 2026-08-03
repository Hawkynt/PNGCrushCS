using System;

namespace FileFormat.Vidcom64;

/// <summary>Assembles Commodore 64 Vidcom 64 file bytes from a Vidcom64File.</summary>
public static class Vidcom64Writer {

  public static byte[] ToBytes(Vidcom64File file) {
    ArgumentNullException.ThrowIfNull(file);

    // Colour, then screen, then bitmap, with the first two sitting in a kilobyte each — this wrote a
    // 47-byte header and then bitmap, screen, colour, which is the order the reader used to expect
    // and no real file uses.
    var result = new byte[Vidcom64File.ExpectedFileSize];

    result[0] = (byte)(file.LoadAddress & 0xFF);
    result[1] = (byte)(file.LoadAddress >> 8);

    file.ColorRam.AsSpan(0, Vidcom64File.ColorRamSize).CopyTo(result.AsSpan(Vidcom64File.ColorRamOffset));
    file.ScreenRam.AsSpan(0, Vidcom64File.ScreenRamSize).CopyTo(result.AsSpan(Vidcom64File.ScreenRamOffset));
    file.BitmapData.AsSpan(0, Vidcom64File.BitmapDataSize).CopyTo(result.AsSpan(Vidcom64File.BitmapOffset));

    var padding = file.HeaderData ?? [];
    padding.AsSpan(0, Math.Min(padding.Length, Vidcom64File.HeaderDataSize))
      .CopyTo(result.AsSpan(Vidcom64File.ColorRamOffset + Vidcom64File.ColorRamSize));

    return result;
  }
}
