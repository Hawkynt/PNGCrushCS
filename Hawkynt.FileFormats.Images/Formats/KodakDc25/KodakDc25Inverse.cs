using System;
using FileFormat.Core;

namespace FileFormat.KodakDc25;

/// <summary>Projects an RGB target onto the DC25's four-colour complementary sensor mosaic.</summary>
internal static class KodakDc25Inverse {

  private const int _Green = 0, _Magenta = 1, _Cyan = 2, _Yellow = 3;

  // Moore-Penrose inverse of the reader's 3x4 matrix after its per-filter gains are folded in.
  // Multiplying linear RGB*255 by these rows produces the minimum-energy four-channel sensor value
  // whose forward matrix maps back to that RGB before clipping.
  private static ReadOnlySpan<double> _RgbToSensor => [
    0.37907883, 0.40026724, 0.21075030,
    0.18738013, 0.67831447, -0.00911815,
   -0.01730452, 0.66192493, 0.17431787,
    0.21324437, 0.38584243, 0.37572365,
  ];

  public static byte[] FromRgb(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);

    // The wide camera mode is the canonical writer target. Its stored crop is 501x242; the reader
    // then restores the camera's non-square photosite aspect. Any arbitrary input is therefore
    // sampled into the sensor crop rather than pretending the raw format can encode arbitrary size.
    var source = image.SampleTo(KodakDc25File.WideWidth, KodakDc25File.CroppedHeight);
    var sensor = new byte[KodakDc25File.WideSensorWidth * KodakDc25File.SensorHeight];

    for (var y = 0; y < KodakDc25File.CroppedHeight; ++y)
    for (var x = 0; x < KodakDc25File.WideWidth; ++x) {
      var at = (y * KodakDc25File.WideWidth + x) * 3;
      var r = _FromSrgb(source.PixelData[at]);
      var g = _FromSrgb(source.PixelData[at + 1]);
      var b = _FromSrgb(source.PixelData[at + 2]);
      var filter = _FilterAt(x, y);
      var c = filter * 3;
      var value = (_RgbToSensor[c] * r + _RgbToSensor[c + 1] * g + _RgbToSensor[c + 2] * b) * 255.0;
      sensor[(y + 1) * KodakDc25File.WideSensorWidth + x + 1] = _Clamp(value);
    }

    _ReplicateMargins(sensor);
    return sensor;
  }

  private static int _FilterAt(int x, int y) => (y & 1) == 0
    ? (x & 1) == 0 ? _Magenta : _Yellow
    : (x & 1) == 0 ? _Green : _Cyan;

  private static double _FromSrgb(byte value) {
    var encoded = value / 255.0;
    return encoded <= 0.04045
      ? encoded / 12.92
      : Math.Pow((encoded + 0.055) / 1.055, 2.4);
  }

  private static byte _Clamp(double value)
    => (byte)Math.Clamp((int)Math.Round(value), 0, 255);

  private static void _ReplicateMargins(byte[] sensor) {
    var stride = KodakDc25File.WideSensorWidth;
    var lastCropColumn = KodakDc25File.WideWidth;

    // Left and unused right margin: continue the nearest measured photosite rather than introduce a
    // black edge that would contaminate the reader's 3x3 interpolation near the crop boundary.
    for (var y = 1; y <= KodakDc25File.CroppedHeight; ++y) {
      var row = y * stride;
      sensor[row] = sensor[row + 1];
      for (var x = lastCropColumn + 2; x < stride; ++x)
        sensor[row + x] = sensor[row + lastCropColumn + 1];
    }

    // The stored array has one excluded row above and below the 242-line crop.
    sensor.AsSpan(stride, stride).CopyTo(sensor.AsSpan(0, stride));
    sensor.AsSpan(KodakDc25File.CroppedHeight * stride, stride)
      .CopyTo(sensor.AsSpan((KodakDc25File.SensorHeight - 1) * stride, stride));
  }
}
