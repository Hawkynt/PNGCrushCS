using System;

namespace FileFormat.Vidcom64;

/// <summary>Assembles Commodore 64 Vidcom 64 file bytes from a Vidcom64File.</summary>
public static class Vidcom64Writer {

  public static byte[] ToBytes(Vidcom64File file) {
    ArgumentNullException.ThrowIfNull(file);

    var result = new byte[Vidcom64File.ExpectedFileSize];
    var offset = 0;

    result[offset] = (byte)(file.LoadAddress & 0xFF);
    result[offset + 1] = (byte)(file.LoadAddress >> 8);
    offset += Vidcom64File.LoadAddressSize;

    file.HeaderData.AsSpan(0, Math.Min(file.HeaderData.Length, Vidcom64File.HeaderDataSize)).CopyTo(result.AsSpan(offset));
    offset += Vidcom64File.HeaderDataSize;

    file.BitmapData.AsSpan(0, Vidcom64File.BitmapDataSize).CopyTo(result.AsSpan(offset));
    offset += Vidcom64File.BitmapDataSize;

    file.ScreenRam.AsSpan(0, Vidcom64File.ScreenRamSize).CopyTo(result.AsSpan(offset));
    offset += Vidcom64File.ScreenRamSize;

    file.ColorRam.AsSpan(0, Vidcom64File.ColorRamSize).CopyTo(result.AsSpan(offset));
    offset += Vidcom64File.ColorRamSize;

    result[offset] = file.BackgroundColor;

    return result;
  }
}
