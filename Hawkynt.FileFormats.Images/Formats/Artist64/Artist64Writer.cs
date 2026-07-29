using System;

namespace FileFormat.Artist64;

/// <summary>Assembles Commodore 64 Artist 64 file bytes from an Artist64File.</summary>
public static class Artist64Writer {

  public static byte[] ToBytes(Artist64File file) {
    ArgumentNullException.ThrowIfNull(file);

    var result = new byte[Artist64File.ExpectedFileSize];
    var offset = 0;

    result[offset] = (byte)(file.LoadAddress & 0xFF);
    result[offset + 1] = (byte)(file.LoadAddress >> 8);
    offset += Artist64File.LoadAddressSize;

    file.BitmapData.AsSpan(0, Math.Min(file.BitmapData.Length, Artist64File.BitmapDataSize)).CopyTo(result.AsSpan(offset));
    offset += Artist64File.BitmapDataSize;

    file.VideoMatrix.AsSpan(0, Math.Min(file.VideoMatrix.Length, Artist64File.VideoMatrixSize)).CopyTo(result.AsSpan(offset));
    offset += Artist64File.VideoMatrixSize;

    file.ColorRam.AsSpan(0, Math.Min(file.ColorRam.Length, Artist64File.ColorRamSize)).CopyTo(result.AsSpan(offset));

    // Trailing 240 bytes padding remain as zeros

    return result;
  }
}
