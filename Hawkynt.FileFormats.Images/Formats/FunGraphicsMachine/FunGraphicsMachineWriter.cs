using System;

namespace FileFormat.FunGraphicsMachine;

/// <summary>Assembles Commodore 64 Fun Graphics Machine file bytes from a FunGraphicsMachineFile.</summary>
public static class FunGraphicsMachineWriter {

  public static byte[] ToBytes(FunGraphicsMachineFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var result = new byte[FunGraphicsMachineFile.ExpectedFileSize];
    var offset = 0;

    result[offset] = (byte)(file.LoadAddress & 0xFF);
    result[offset + 1] = (byte)(file.LoadAddress >> 8);
    offset += FunGraphicsMachineFile.LoadAddressSize;

    file.ScreenRam.AsSpan(0, Math.Min(file.ScreenRam.Length, FunGraphicsMachineFile.ScreenRamSize)).CopyTo(result.AsSpan(offset));
    offset += FunGraphicsMachineFile.ScreenRamSize;

    file.BitmapData.AsSpan(0, Math.Min(file.BitmapData.Length, FunGraphicsMachineFile.BitmapDataSize)).CopyTo(result.AsSpan(offset));

    // Trailing 7 bytes padding remain as zeros

    return result;
  }
}
