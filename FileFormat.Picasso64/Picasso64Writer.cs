using System;

namespace FileFormat.Picasso64;

/// <summary>Assembles Commodore 64 Picasso 64 file bytes from a Picasso64File.</summary>
public static class Picasso64Writer {

  public static byte[] ToBytes(Picasso64File file) {
    ArgumentNullException.ThrowIfNull(file);

    var result = new byte[Picasso64File.ExpectedFileSize];
    var offset = 0;

    result[offset] = (byte)(file.LoadAddress & 0xFF);
    result[offset + 1] = (byte)(file.LoadAddress >> 8);
    offset += Picasso64File.LoadAddressSize;

    file.BitmapData.AsSpan(0, Picasso64File.BitmapDataSize).CopyTo(result.AsSpan(offset));
    offset += Picasso64File.BitmapDataSize;

    file.ScreenRam.AsSpan(0, Picasso64File.ScreenRamSize).CopyTo(result.AsSpan(offset));
    offset += Picasso64File.ScreenRamSize;

    file.ColorRam.AsSpan(0, Picasso64File.ColorRamSize).CopyTo(result.AsSpan(offset));
    offset += Picasso64File.ColorRamSize;

    result[offset++] = file.BackgroundColor;
    result[offset++] = file.BorderColor;

    file.ExtraData.AsSpan(0, Math.Min(file.ExtraData.Length, Picasso64File.ExtraDataSize)).CopyTo(result.AsSpan(offset));

    return result;
  }
}
