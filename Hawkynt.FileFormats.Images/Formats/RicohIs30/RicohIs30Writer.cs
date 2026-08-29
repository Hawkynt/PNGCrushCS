using System;
using System.Globalization;
using System.Text;

namespace FileFormat.RicohIs30;

/// <summary>Writes Ricoh IS30 scans in the verified uncompressed four-grey layout.</summary>
public static class RicohIs30Writer {

  public static byte[] ToBytes(RicohIs30File file) {
    if (file.BitsPerPixel is not (1 or 2))
      throw new ArgumentException($"Ricoh IS30 supports one or two bits per pixel, not {file.BitsPerPixel}.", nameof(file));
    if (file.Width < 1 || file.Height is < 1 or > 65535)
      throw new ArgumentException($"Invalid Ricoh IS30 dimensions {file.Width}x{file.Height}.", nameof(file));

    var pixelsPerByte = 8 / file.BitsPerPixel;
    if (file.Width % pixelsPerByte != 0)
      throw new ArgumentException($"Ricoh IS30 width must be a multiple of {pixelsPerByte} pixels for {file.BitsPerPixel}bpp.", nameof(file));

    var bytesPerRow = file.Width / pixelsPerByte;
    if (bytesPerRow > 9999)
      throw new ArgumentException($"Ricoh IS30 stores row length in four decimal characters; {bytesPerRow} is too large.", nameof(file));

    var expected = checked(bytesPerRow * file.Height);
    if (file.PixelData == null || file.PixelData.Length < expected)
      throw new ArgumentException($"Ricoh IS30 needs {expected} packed bytes.", nameof(file));

    var output = new byte[checked(RicohIs30File.HeaderSize + expected)];
    RicohIs30File.Signature.CopyTo(output);
    output[RicohIs30File.DepthSelectorOffset] = file.BitsPerPixel == 1 ? (byte)1 : (byte)2;
    _WriteDecimal(output, RicohIs30File.ResolutionOffset, RicohIs30File.ResolutionLength, Math.Clamp(file.Resolution, 0, 999));
    _WriteDecimal(output, RicohIs30File.BytesPerRowOffset, RicohIs30File.BytesPerRowLength, bytesPerRow);
    _WriteDecimal(output, RicohIs30File.HeightOffset, RicohIs30File.HeightLength, file.Height);
    output[RicohIs30File.MarkerOffset] = RicohIs30File.MarkerValue;
    file.PixelData.AsSpan(0, expected).CopyTo(output.AsSpan(RicohIs30File.HeaderSize));
    return output;
  }

  private static void _WriteDecimal(byte[] output, int offset, int length, int value) {
    var text = value.ToString($"D{length}", CultureInfo.InvariantCulture);
    Encoding.ASCII.GetBytes(text.AsSpan(), output.AsSpan(offset, length));
  }
}
