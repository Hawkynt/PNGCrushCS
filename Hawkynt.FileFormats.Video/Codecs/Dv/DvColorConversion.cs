namespace FileFormat.Codecs.Dv;

/// <summary>
/// Turns the decoded planes into the packed eight-bit RGB every reader in this package hands back.
/// </summary>
/// <remarks>
/// A display convention rather than part of the decoding. DV codes ITU-R BT.601 Y′CbCr at studio
/// swing — black at 16, nominal peak white at 235, colour centred on 128 — and reading the samples as
/// though they filled the byte leaves every picture washed out by about seven per cent of its
/// contrast, which is the kind of wrongness that looks like a decode that worked.
/// <para/>
/// Each colour sample is repeated across the luma samples it covers rather than interpolated between
/// its neighbours, which is what the other subsampled decoders here do and what keeps a picture
/// packed here and read back by one of them where it started. It is also why a comparison against
/// another decoder is made on the planes: interpolating instead would change tens of thousands of
/// samples on a hard-edged picture without either decode being wrong.
/// </remarks>
internal static class DvColorConversion {

  internal static byte[] ToRgb24(DvPlanes planes) {
    var rgb = new byte[planes.Width * planes.Height * 3];
    var horizontalShift = planes.Width == planes.ChromaWidth ? 0 : planes.Width / planes.ChromaWidth == 2 ? 1 : 2;
    var verticalShift = planes.Height == planes.ChromaHeight ? 0 : 1;

    for (var y = 0; y < planes.Height; ++y) {
      var lumaRow = y * planes.Width;
      var chromaRow = (y >> verticalShift) * planes.ChromaWidth;
      var target = lumaRow * 3;

      for (var x = 0; x < planes.Width; ++x) {
        var chromaColumn = x >> horizontalShift;
        var scaledLuma = 298 * (planes.Luma[lumaRow + x] - 16);
        var blueDifference = planes.Cb[chromaRow + chromaColumn] - 128;
        var redDifference = planes.Cr[chromaRow + chromaColumn] - 128;

        rgb[target] = _Clamp(scaledLuma + 409 * redDifference + 128);
        rgb[target + 1] = _Clamp(scaledLuma - 100 * blueDifference - 208 * redDifference + 128);
        rgb[target + 2] = _Clamp(scaledLuma + 516 * blueDifference + 128);
        target += 3;
      }
    }

    return rgb;
  }

  private static byte _Clamp(int scaled) {
    var value = scaled >> 8;

    return (byte)(value < 0 ? 0 : value > 255 ? 255 : value);
  }
}
