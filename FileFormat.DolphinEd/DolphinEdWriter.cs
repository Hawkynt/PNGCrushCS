using System;

namespace FileFormat.DolphinEd;

/// <summary>Assembles Dolphin Ed C64 multicolor file bytes from a DolphinEdFile.</summary>
public static class DolphinEdWriter {

  public static byte[] ToBytes(DolphinEdFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var result = new byte[DolphinEdFile.ExpectedFileSize];
    var offset = 0;

    result[offset] = (byte)(file.LoadAddress & 0xFF);
    result[offset + 1] = (byte)(file.LoadAddress >> 8);
    offset += DolphinEdFile.LoadAddressSize;

    file.BitmapData.AsSpan(0, DolphinEdFile.BitmapDataSize).CopyTo(result.AsSpan(offset));
    offset += DolphinEdFile.BitmapDataSize;

    file.VideoMatrix.AsSpan(0, DolphinEdFile.VideoMatrixSize).CopyTo(result.AsSpan(offset));
    offset += DolphinEdFile.VideoMatrixSize;

    file.ColorRam.AsSpan(0, DolphinEdFile.ColorRamSize).CopyTo(result.AsSpan(offset));
    offset += DolphinEdFile.ColorRamSize;

    result[offset] = file.BackgroundColor;

    return result;
  }
}
