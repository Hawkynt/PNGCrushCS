using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.Codecs;

/// <summary>
/// The two picture-layout rules Avid's JPEG-coded codecs share: the display crop that keeps the
/// bottom of an over-tall coded picture, and the field weave that turns two coded field pictures
/// into one frame.
/// </summary>
/// <remarks>
/// Both <c>AVRn</c> and <c>AVDJ</c> need them and neither invented them, so they live here rather
/// than once per codec. The behaviour is the reference decoder's, measured rather than assumed —
/// what was measured, and against what, is recorded in <c>codec-notes.md</c> beside each codec.
/// </remarks>
internal static class AvidMotionJpegLayout {

  /// <summary>
  /// Whether a coded picture of this height is one field of a two-field packet rather than a whole
  /// frame.
  /// </summary>
  /// <remarks>
  /// A coded field is half a frame, but an encoder is free to pad its coded height out to a whole
  /// number of macroblock rows and a container is free to state a display height a few rows shorter
  /// than either. So the test is not equality but the reference decoder's own margin: anything under
  /// three quarters of the stated frame height is a field. Measured against ffmpeg 8.1.2, which
  /// weaves at coded field heights of 240, 243 and 248 against a stated 486 and treats a coded 486
  /// as one frame.
  /// </remarks>
  public static bool IsCodedField(int codedHeight, int containerHeight)
    => containerHeight > 0 && (long)codedHeight * 4 < (long)containerHeight * 3;

  /// <summary>
  /// Trims a decoded Avid picture to the geometry its container states, keeping the bottom rows and
  /// the left columns.
  /// </summary>
  /// <remarks>
  /// Which end the padding sits at is the Avid-specific part and does not go without saying: the
  /// coded macroblock padding is above the displayed picture, so the bottom is what survives. A
  /// container stating more than the packet codes is refused rather than padded, because nothing
  /// here has bytes to fill the difference with.
  /// </remarks>
  public static RawImage CropToDisplay(RawImage decoded, int width, int height, int streamIndex, string whatCodesIt) {
    if (width > decoded.Width || height > decoded.Height)
      throw new InvalidDataException(
        $"Video stream {streamIndex} states a picture size of {width}x{height}, larger than the "
        + $"{decoded.Width}x{decoded.Height} its {whatCodesIt} codes.");

    if (width == decoded.Width && height == decoded.Height)
      return decoded;

    var bytesPerPixel = _BytesPerPixel(decoded.Format, "display cropping");
    var sourceStride = checked(decoded.Width * bytesPerPixel);
    var targetStride = checked(width * bytesPerPixel);
    var pixels = new byte[checked(targetStride * height)];
    var firstRow = decoded.Height - height;

    for (var row = 0; row < height; ++row)
      decoded.PixelData.AsSpan((firstRow + row) * sourceStride, targetStride)
        .CopyTo(pixels.AsSpan(row * targetStride, targetStride));

    return new() {
      Width = width,
      Height = height,
      Format = decoded.Format,
      PixelData = pixels,
      ColorInfo = decoded.ColorInfo,
      Palette = decoded.Palette,
      PaletteCount = decoded.PaletteCount,
      AlphaTable = decoded.AlphaTable,
      Metadata = decoded.Metadata,
    };
  }

  /// <summary>
  /// Interleaves two coded fields into one frame, putting the first coded field on the row parity
  /// the stream's field descriptor names.
  /// </summary>
  public static RawImage Weave(RawImage first, RawImage second, bool firstFieldOnOddRows) {
    var bytesPerPixel = _BytesPerPixel(first.Format, "field weaving");
    var stride = checked(first.Width * bytesPerPixel);
    var height = checked(first.Height * 2);
    var pixels = new byte[checked(stride * height)];
    var firstParity = firstFieldOnOddRows ? 1 : 0;
    var secondParity = 1 - firstParity;

    for (var fieldRow = 0; fieldRow < first.Height; ++fieldRow) {
      first.PixelData.AsSpan(fieldRow * stride, stride)
        .CopyTo(pixels.AsSpan((fieldRow * 2 + firstParity) * stride, stride));
      second.PixelData.AsSpan(fieldRow * stride, stride)
        .CopyTo(pixels.AsSpan((fieldRow * 2 + secondParity) * stride, stride));
    }

    return new() {
      Width = first.Width,
      Height = height,
      Format = first.Format,
      PixelData = pixels,
      ColorInfo = first.ColorInfo,
      Metadata = first.Metadata,
    };
  }

  private static int _BytesPerPixel(PixelFormat format, string operation) {
    var bytesPerPixel = RawImage.BytesPerPixel(format);
    return bytesPerPixel > 0
      ? bytesPerPixel
      : throw new NotSupportedException($"Avid {operation} does not support decoded pixel format {format}.");
  }
}
