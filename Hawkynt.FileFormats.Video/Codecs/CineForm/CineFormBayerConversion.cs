using System;
using System.Buffers.Binary;
using System.IO;
using FileFormat.Core;

namespace FileFormat.Codecs.CineForm;

/// <summary>Converts CineForm's four decorrelated Bayer channels back to a raw RGGB sensor mosaic.</summary>
/// <remarks>
/// CineForm does not wavelet-transform the sensor mosaic directly. Each two-by-two Bayer cell becomes
/// four half-resolution channels: average green, red-minus-green, blue-minus-green and green
/// difference. FFmpeg's public decoder and GoPro's dual MIT/Apache-2.0 SDK agree on the inverse below;
/// keeping it separate from display colour conversion is intentional because the result is still RAW
/// sensor data and must not be silently demosaiced.
/// </remarks>
internal static class CineFormBayerConversion {
  private const int _MIDPOINT = 1 << 11;
  private const int _MAX_SAMPLE = (1 << 12) - 1;

  internal static RawImage ToRggb12(CineFormPictureDecoder.Result picture) {
    ArgumentNullException.ThrowIfNull(picture);
    if (!picture.IsBayer || picture.Channels.Length != 4 || picture.Precision != 12)
      throw new ArgumentException("The CineForm picture is not a four-channel twelve-bit Bayer RAW image.", nameof(picture));
    if ((picture.ImageWidth & 1) != 0 || (picture.ImageHeight & 1) != 0)
      throw new InvalidDataException($"A CineForm Bayer picture must have even dimensions; {picture.ImageWidth}x{picture.ImageHeight} was decoded.");

    var cellWidth = picture.ImageWidth >> 1;
    var cellHeight = picture.ImageHeight >> 1;
    foreach (var channel in picture.Channels)
      if (channel.Width < cellWidth || channel.Height < cellHeight)
        throw new InvalidDataException(
          $"A CineForm Bayer component is {channel.Width}x{channel.Height}, smaller than the {cellWidth}x{cellHeight} cells required by the picture header.");

    var output = new byte[checked(picture.ImageWidth * picture.ImageHeight * 2)];
    var gPlane = picture.Channels[0];
    var rgPlane = picture.Channels[1];
    var bgPlane = picture.Channels[2];
    var gdPlane = picture.Channels[3];

    for (var y = 0; y < cellHeight; ++y) {
      var row0 = (y << 1) * picture.ImageWidth;
      var row1 = row0 + picture.ImageWidth;
      for (var x = 0; x < cellWidth; ++x) {
        var g = gPlane.Samples[y * gPlane.Width + x];
        var rg = rgPlane.Samples[y * rgPlane.Width + x] - _MIDPOINT;
        var bg = bgPlane.Samples[y * bgPlane.Width + x] - _MIDPOINT;
        var gd = gdPlane.Samples[y * gdPlane.Width + x] - _MIDPOINT;

        var r = _Clamp12((rg << 1) + g);
        var g1 = _Clamp12(g + gd);
        var g2 = _Clamp12(g - gd);
        var b = _Clamp12((bg << 1) + g);

        var column = x << 1;
        _Write(output, row0 + column, r);
        _Write(output, row0 + column + 1, g1);
        _Write(output, row1 + column, g2);
        _Write(output, row1 + column + 1, b);
      }
    }

    return new() {
      Width = picture.ImageWidth,
      Height = picture.ImageHeight,
      Format = PixelFormat.Cfa16,
      PixelData = output,
      CfaInfo = new(RawCfaPattern.Rggb, 12),
    };
  }

  private static int _Clamp12(int value) => value < 0 ? 0 : value > _MAX_SAMPLE ? _MAX_SAMPLE : value;

  private static void _Write(Span<byte> output, int pixelIndex, int value)
    => BinaryPrimitives.WriteUInt16LittleEndian(output[(pixelIndex * 2)..], checked((ushort)value));
}
