namespace FileFormat.Codecs.DnxHd;

/// <summary>
/// Turns the reconstructed component planes into the packed 8-bit RGB every reader here hands back.
/// </summary>
/// <remarks>
/// A display convention, not part of the coding. VC-3 normally codes Y′CbCr and states in Coding
/// Control B (SMPTE ST 2019-1:2016, 7.2.5) which colour volume the samples were meant for, which is
/// honoured where it names one. CID 1256 additionally permits RGB-format macroblocks: an ACF-clear
/// macroblock is already R, G and B, while an ACF-set one is Y′CbCr and 7.3.1.1 requires the BT.709
/// inverse transform during decode. The distinction is therefore retained to this final packing step
/// instead of converting ten-bit samples to another ten-bit representation and rounding twice.
/// <para/>
/// The samples are studio swing, per Table 1: black at <c>16 * 2^(b-8)</c> and nominal peak white at
/// <c>235 * 2^(b-8)</c>, with chroma centred on <c>128 * 2^(b-8)</c>. Reading them as though they
/// filled the range leaves every picture washed out by about seven per cent of its contrast, which
/// looks like a decode that worked.
/// </remarks>
internal static class DnxHdColorConversion {

  /// <summary>
  /// Packs the planes into 8-bit RGB, cropping to the raster the header states.
  /// </summary>
  /// <param name="planes">The reconstructed planes, a whole number of macroblocks in both directions.</param>
  /// <param name="width">The header's samples per line; columns past it are discarded.</param>
  /// <param name="height">The displayed active lines; rows past it are discarded.</param>
  /// <param name="colorVolume">The CLV field of Coding Control B.</param>
  internal static byte[] ToRgb24(DnxHdPlanes planes, int width, int height, int colorVolume) {
    var rgb = new byte[width * height * 3];

    // ACF=1 has its own normative BT.709 transform. CLV chooses the matrix only for ordinary
    // Y′CbCr bitstreams; it must not leak into the alternate representation of an RGB-format frame.
    var (redFromCr, greenFromCb, greenFromCr, blueFromCb) = Matrix(planes.RgbFormat ? 0 : colorVolume);

    var extra = planes.BitDepth - 8;
    var black = 16 << extra;
    var centre = 128 << extra;
    var nominalRange = 219 << extra;
    var shift = 8 + extra;
    var half = 1 << (shift - 1);

    var subsampled = planes.Width != planes.ChromaWidth;
    var lastChromaColumn = planes.ChromaWidth - 1;

    for (var y = 0; y < height; ++y) {
      var firstRow = y * planes.Width;
      var secondRow = y * planes.ChromaWidth;
      var target = y * width * 3;

      for (var x = 0; x < width; ++x) {
        if (planes.RgbFormat && planes.IsDirectRgb(x, y)) {
          rgb[target] = _ScaleDirectRgb(planes.Luma[firstRow + x], black, nominalRange);
          rgb[target + 1] = _ScaleDirectRgb(planes.Cb[secondRow + x], black, nominalRange);
          rgb[target + 2] = _ScaleDirectRgb(planes.Cr[secondRow + x], black, nominalRange);
          target += 3;
          continue;
        }

        // 298/256 is 219/255 inverted: the studio-swing luma range expanded to fill the byte.
        var scaledLuma = 298 * (planes.Luma[firstRow + x] - black);
        var blueDifference = _Chroma(planes.Cb, secondRow, x, subsampled, lastChromaColumn) - centre;
        var redDifference = _Chroma(planes.Cr, secondRow, x, subsampled, lastChromaColumn) - centre;

        rgb[target] = _Clamp(scaledLuma + redFromCr * redDifference + half, shift);
        rgb[target + 1] = _Clamp(scaledLuma - greenFromCb * blueDifference - greenFromCr * redDifference + half, shift);
        rgb[target + 2] = _Clamp(scaledLuma + blueFromCb * blueDifference + half, shift);
        target += 3;
      }
    }

    return rgb;
  }

  private static byte _ScaleDirectRgb(int sample, int black, int nominalRange) {
    var scaled = ((sample - black) * 255 + nominalRange / 2) / nominalRange;
    return (byte)(scaled < 0 ? 0 : scaled > 255 ? 255 : scaled);
  }

  /// <summary>
  /// One chroma sample at a luma column, at the coded depth.
  /// </summary>
  /// <remarks>
  /// At 4:4:4 there is one chroma sample per luma sample and nothing to do. At 4:2:2 the chroma
  /// sample is co-sited with the even luma column, so an even column reads it whole and an odd one
  /// sits halfway between two and takes their average. It is also why a comparison against another
  /// decoder is made on the planes and never on the packed colour: ffmpeg's scaler replicates where
  /// this interpolates, and that disagreement alone runs to tens of thousands of samples on a
  /// hard-edged picture.
  /// </remarks>
  private static int _Chroma(ushort[] plane, int row, int x, bool subsampled, int lastColumn) {
    if (!subsampled)
      return plane[row + x];

    var near = x >> 1;
    if ((x & 1) == 0)
      return plane[row + near];

    var far = near + 1 <= lastColumn ? near + 1 : lastColumn;

    return (plane[row + near] + plane[row + far] + 1) >> 1;
  }

  /// <summary>
  /// The inverse matrix the CLV field names, in 8-bit fixed point.
  /// </summary>
  /// <remarks>
  /// 7.2.5: 00 is BT.709 and 01 is the non-constant-luma mapping of BT.2020, both of which are a
  /// pair of luma coefficients and an inverse that follows from them. 10 is BT.2020's
  /// constant-luma mapping, which is not a matrix at all — the luma there is computed from linear
  /// light — and 11 says the volume is described somewhere outside the bitstream. Both fall back on
  /// BT.709 here, which is what the rasters this format defines are.
  /// <para/>
  /// The four terms are <c>2(1−Kr)</c>, <c>2Kb(1−Kb)/Kg</c>, <c>2Kr(1−Kr)/Kg</c> and <c>2(1−Kb)</c>,
  /// each carrying the 255/224 that expands studio-swing chroma, and each scaled by 256.
  /// </remarks>
  internal static (int RedFromCr, int GreenFromCb, int GreenFromCr, int BlueFromCb) Matrix(int colorVolume)
    => colorVolume == 1
      ? (430, 48, 167, 548)  // ITU-R BT.2020, non-constant luma
      : (459, 55, 136, 541); // ITU-R BT.709

  private static byte _Clamp(int scaled, int shift) {
    var value = scaled >> shift;

    return (byte)(value < 0 ? 0 : value > 255 ? 255 : value);
  }
}
