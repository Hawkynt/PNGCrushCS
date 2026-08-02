using System;
using FileFormat.Ccitt;

namespace FileFormat.BrooktroutFax;

/// <summary>Assembles Brooktrout 301 fax image file bytes.</summary>
/// <remarks>
/// What was written before put the width over the signature and the height over the resolution, and
/// handed the page back as flat bitmap bytes — a fax states its page coded, which is the reason the
/// format exists. Nothing else could open the result.
/// </remarks>
public static class BrooktroutFaxWriter {

  /// <summary>Horizontal resolution written for a page, in dots per inch.</summary>
  private const int _HORIZONTAL_RESOLUTION = 200;

  /// <summary>Vertical resolution written for a page.</summary>
  private const int _VERTICAL_RESOLUTION = 100;

  public static byte[] ToBytes(BrooktroutFaxFile file) {
    var coded = CcittG3Encoder.Encode(file.PixelData ?? [], file.Width, file.Height);
    var result = new byte[BrooktroutFaxFile.HeaderSize + coded.Length];

    BrooktroutFaxFile.Signature.CopyTo(result);
    result[4] = (byte)_HORIZONTAL_RESOLUTION;
    result[5] = (byte)(_HORIZONTAL_RESOLUTION >> 8);
    result[6] = (byte)_VERTICAL_RESOLUTION;
    result[7] = (byte)(_VERTICAL_RESOLUTION >> 8);
    result[BrooktroutFaxFile.WidthOffset] = (byte)file.Width;
    result[BrooktroutFaxFile.WidthOffset + 1] = (byte)(file.Width >> 8);
    result[BrooktroutFaxFile.HeightOffset] = (byte)file.Height;
    result[BrooktroutFaxFile.HeightOffset + 1] = (byte)(file.Height >> 8);

    coded.CopyTo(result, BrooktroutFaxFile.HeaderSize);

    return result;
  }
}
