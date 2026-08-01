using System;

namespace FileFormat.Botticelli;

/// <summary>Assembles Botticelli (.p4i) file bytes.</summary>
/// <remarks>
/// The high-resolution screen, which is the one of the three that a picture from elsewhere fits:
/// two colours a cell out of the chip's full 121 rather than four out of a fixed sixteen, at the
/// full 320 across. Multicolour would buy a third and fourth colour per cell at half the horizontal
/// resolution, but two of those four are shared by the whole screen, so it is the poorer trade for
/// anything but a picture drawn for it.
/// </remarks>
public static class BotticelliWriter {

  public static byte[] ToBytes(BotticelliFile file) {
    ArgumentNullException.ThrowIfNull(file.Data);

    var result = new byte[file.Mode == BotticelliMode.Logo
      ? BotticelliFile.LogoFileSize
      : BotticelliFile.ScreenFileSize];

    file.Data.AsSpan(0, Math.Min(file.Data.Length, result.Length)).CopyTo(result);
    return result;
  }
}
