using System;
using FileFormat.Ccitt;

namespace FileFormat.WinFax;

/// <summary>Assembles WinFAX fax image file bytes.</summary>
public static class WinFaxWriter {

  /// <summary>Vertical resolution written for a page, in dots per inch.</summary>
  /// <remarks>The samples carry 200 for a fine page and 100 for a coarse one; fine is written.</remarks>
  private const byte _FINE_RESOLUTION = 200;

  public static byte[] ToBytes(WinFaxFile file) {
    // The page was written as flat bitmap bytes behind a header that put the size in the wrong
    // fields, so nothing else could open it — a fax file states its page coded, which is the whole
    // reason the format exists.
    var coded = CcittG3Encoder.Encode(file.PixelData ?? [], file.Width, file.Height);
    var result = new byte[WinFaxFile.HeaderSize + coded.Length];

    WinFaxFile.Signature.CopyTo(result);
    result[WinFaxFile.ResolutionOffset] = _FINE_RESOLUTION;
    result[WinFaxFile.WidthOffset] = (byte)file.Width;
    result[WinFaxFile.WidthOffset + 1] = (byte)(file.Width >> 8);
    result[WinFaxFile.HeightOffset] = (byte)file.Height;
    result[WinFaxFile.HeightOffset + 1] = (byte)(file.Height >> 8);

    coded.CopyTo(result, WinFaxFile.HeaderSize);

    return result;
  }
}
