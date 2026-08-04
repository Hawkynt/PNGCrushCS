using System;

namespace FileFormat.FunGraphicsMachine;

/// <summary>Assembles Fun Graphics Machine picture bytes from a FunGraphicsMachineFile.</summary>
/// <remarks>
/// A file carrying nothing but the machine's default attribute is written without a screen at all,
/// which is the shorter of the two shapes the reader takes and the only one RECOIL accepts. Writing
/// the longer one regardless made every picture this produced unreadable by anything else.
/// </remarks>
public static class FunGraphicsMachineWriter {

  public static byte[] ToBytes(FunGraphicsMachineFile file) {
    ArgumentNullException.ThrowIfNull(file.BitmapData);

    var screen = file.ScreenRam ?? [];
    var plain = true;
    foreach (var b in screen)
      if (b != FunGraphicsMachineFile.DefaultScreenAttribute) {
        plain = false;
        break;
      }

    if (plain) {
      var bare = new byte[FunGraphicsMachineFile.BitmapOnlyFileSize];
      bare[0] = (byte)(file.LoadAddress & 0xFF);
      bare[1] = (byte)(file.LoadAddress >> 8);
      file.BitmapData.AsSpan(0, Math.Min(file.BitmapData.Length, FunGraphicsMachineFile.BitmapDataSize))
        .CopyTo(bare.AsSpan(FunGraphicsMachineFile.LoadAddressSize));

      return bare;
    }

    var result = new byte[FunGraphicsMachineFile.ExpectedFileSize];
    var offset = 0;

    result[offset] = (byte)(file.LoadAddress & 0xFF);
    result[offset + 1] = (byte)(file.LoadAddress >> 8);
    offset += FunGraphicsMachineFile.LoadAddressSize;

    screen.AsSpan(0, Math.Min(screen.Length, FunGraphicsMachineFile.ScreenRamSize)).CopyTo(result.AsSpan(offset));
    offset += FunGraphicsMachineFile.ScreenRamSize;

    file.BitmapData.AsSpan(0, Math.Min(file.BitmapData.Length, FunGraphicsMachineFile.BitmapDataSize))
      .CopyTo(result.AsSpan(offset));
    // The trailing seven bytes are padding, which is already nought.

    return result;
  }
}
