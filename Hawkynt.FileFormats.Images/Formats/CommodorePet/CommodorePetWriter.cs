using System;
using System.IO;

namespace FileFormat.CommodorePet;

/// <summary>Assembles commodore pet petscii screen dump bytes from pixel data.</summary>
/// <remarks>
/// A saved screen opens with the address it loads to, then a thousand character codes, then a
/// thousand colours. Only the codes used to be written, so nothing that read the file back could
/// know what colour anything was — and the reader, which now takes the colours from the end of the
/// file, would have read the codes as colours.
/// </remarks>
public static class CommodorePetWriter {

  /// <summary>Where a screen loads on the machine, which is what the first two bytes say.</summary>
  private const int _SCREEN_ADDRESS = 0x3000;

  public static byte[] ToBytes(CommodorePetFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var result = new byte[CommodorePetFile.FixedFileSize];
    result[0] = unchecked((byte)_SCREEN_ADDRESS);
    result[1] = (byte)(_SCREEN_ADDRESS >> 8);

    var codes = file.PixelData ?? [];
    codes.AsSpan(0, Math.Min(CommodorePetFile.CellCount, codes.Length))
      .CopyTo(result.AsSpan(CommodorePetFile.LoadAddressSize));

    // The caption sits between the screen and the colours. Leaving it out packed the file 24 bytes
    // tighter than the format is, and the reference decoder takes one length and no other — so what
    // we wrote could be read here and nowhere else.
    var caption = file.Caption ?? [];
    var captionAt = CommodorePetFile.LoadAddressSize + CommodorePetFile.CellCount;
    caption.AsSpan(0, Math.Min(CommodorePetFile.CaptionSize, caption.Length))
      .CopyTo(result.AsSpan(captionAt));

    var colors = file.CellColors ?? [];
    var at = captionAt + CommodorePetFile.CaptionSize;
    if (colors.Length > 0)
      colors.AsSpan(0, Math.Min(CommodorePetFile.CellCount, colors.Length)).CopyTo(result.AsSpan(at));
    else
      result.AsSpan(at).Fill(1);

    return result;
  }

  public static void ToStream(CommodorePetFile file, Stream stream) {
    ArgumentNullException.ThrowIfNull(stream);
    var bytes = ToBytes(file);
    stream.Write(bytes, 0, bytes.Length);
  }

  public static void ToFile(CommodorePetFile file, FileInfo target) {
    ArgumentNullException.ThrowIfNull(target);
    File.WriteAllBytes(target.FullName, ToBytes(file));
  }
}
