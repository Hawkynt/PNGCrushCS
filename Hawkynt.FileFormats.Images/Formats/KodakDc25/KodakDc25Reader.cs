using System;
using System.IO;
using System.Text;

namespace FileFormat.KodakDc25;

/// <summary>Reads Kodak DC25 raw photographs from bytes, streams, or file paths.</summary>
public static class KodakDc25Reader {

  /// <summary>The row and column the crop starts at.</summary>
  private const int _Margin = 1;

  /// <summary>Channel order of the mosaic: green, magenta, cyan, yellow.</summary>
  private const int _Green = 0;
  private const int _Magenta = 1;
  private const int _Cyan = 2;
  private const int _Yellow = 3;

  /// <summary>
  /// Which of the four filters covers a pixel, by its place in the cropped picture.
  /// </summary>
  /// <remarks>
  /// Magenta and yellow along the even rows, green and cyan along the odd ones. The pattern is
  /// stated for the cropped picture rather than for the stored array, so the odd margin is already
  /// accounted for in it.
  /// </remarks>
  private static int _FilterAt(int x, int y) => (y & 1) == 0
    ? (x & 1) == 0 ? _Magenta : _Yellow
    : (x & 1) == 0 ? _Green : _Cyan;

  /// <summary>What each channel is scaled by before the colours are mixed.</summary>
  private static ReadOnlySpan<float> _ChannelGain => [1.0f, 1.179f, 1.209f, 1.036f];

  /// <summary>
  /// The mix taking green, magenta, cyan and yellow to red, green and blue.
  /// </summary>
  /// <remarks>
  /// Three rows of four. Complementary filters each pass two primaries, so a primary is recovered by
  /// adding the filters that pass it and subtracting those that do not, which is why the negative
  /// terms are as large as they are.
  /// </remarks>
  private static ReadOnlySpan<float> _ToRgb => [
    2.25f, 0.75f, -1.75f, -0.25f,
    -0.25f, 0.75f, 0.75f, -0.25f,
    -0.25f, -1.75f, 0.75f, 2.25f,
  ];

  public static KodakDc25File FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Kodak DC25 photograph not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static KodakDc25File FromStream(Stream stream) {
    ArgumentNullException.ThrowIfNull(stream);
    if (stream.CanSeek) {
      var data = new byte[stream.Length - stream.Position];
      stream.ReadExactly(data);
      return FromBytes(data);
    }

    using var ms = new MemoryStream();
    stream.CopyTo(ms);
    return FromBytes(ms.ToArray());
  }

  public static KodakDc25File FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length <= KodakDc25File.SensorOffset
        || data[0] != 'M' || data[1] != 'M' || data[2] != 0x00 || data[3] != 0x2A)
      throw new InvalidDataException("Not a Kodak DC25 photograph: it is not a big-endian TIFF.");

    if (!Encoding.ASCII.GetString(data[..KodakDc25File.SensorOffset]).Contains(KodakDc25File.Model, StringComparison.Ordinal))
      throw new InvalidDataException("Not a Kodak DC25 photograph: it does not name that camera.");

    // Which of the two arrays this is comes out of the length, and the length has to be exactly one
    // of them. That is the whole of the evidence that the fixed offset is the right one.
    var (sensorWidth, width) = (data.Length - KodakDc25File.SensorOffset) switch {
      KodakDc25File.WideSensorWidth * KodakDc25File.SensorHeight => (KodakDc25File.WideSensorWidth, KodakDc25File.WideWidth),
      KodakDc25File.NarrowSensorWidth * KodakDc25File.SensorHeight => (KodakDc25File.NarrowSensorWidth, KodakDc25File.NarrowWidth),
      _ => throw new InvalidDataException(
        $"A Kodak DC25 photograph is {data.Length} bytes, which is neither of the two sensor arrays laid after {KodakDc25File.SensorOffset}."),
    };

    var height = KodakDc25File.CroppedHeight;
    var sensor = data[KodakDc25File.SensorOffset..];

    // Each pixel carries one of four filters, so the other three are taken as the mean of the
    // nearest neighbours holding them.
    var planes = new float[4][];
    for (var c = 0; c < 4; ++c)
      planes[c] = new float[width * height];

    var counts = new int[4];
    var sums = new float[4];

    for (var y = 0; y < height; ++y)
      for (var x = 0; x < width; ++x) {
        Array.Clear(counts);
        Array.Clear(sums);

        // The two by two cell repeats, so every filter is present within one step in each direction.
        for (var dy = -1; dy <= 1; ++dy) {
          var sy = y + dy;
          if (sy < 0 || sy >= height)
            continue;

          for (var dx = -1; dx <= 1; ++dx) {
            var sx = x + dx;
            if (sx < 0 || sx >= width)
              continue;

            var filter = _FilterAt(sx, sy);
            sums[filter] += sensor[(sy + _Margin) * sensorWidth + sx + _Margin];
            ++counts[filter];
          }
        }

        var at = y * width + x;
        for (var c = 0; c < 4; ++c)
          planes[c][at] = counts[c] > 0 ? sums[c] / counts[c] : 0f;
      }

    var rgb = new byte[width * height * 3];
    for (var i = 0; i < width * height; ++i) {
      var g = planes[_Green][i] * _ChannelGain[_Green];
      var m = planes[_Magenta][i] * _ChannelGain[_Magenta];
      var c = planes[_Cyan][i] * _ChannelGain[_Cyan];
      var y = planes[_Yellow][i] * _ChannelGain[_Yellow];

      for (var channel = 0; channel < 3; ++channel) {
        var mixed = _ToRgb[channel * 4] * g + _ToRgb[channel * 4 + 1] * m
                    + _ToRgb[channel * 4 + 2] * c + _ToRgb[channel * 4 + 3] * y;
        rgb[i * 3 + channel] = _ToSrgb(mixed / 255.0f);
      }
    }

    // The photosites are not square, so the array as stored is the right pixels at the wrong shape:
    // the wide one comes out very nearly two to one where the picture is four to three. Stretching
    // along whichever axis is short is what gets to the shape the camera states.
    //
    // The wide array's ratio is taken from the size the camera itself names for the rendered
    // picture, 493 by 373; the narrow one has no such statement and is taken to four by three.
    var aspect = sensorWidth == KodakDc25File.WideSensorWidth
      ? KodakDc25File.RenderedWidth * (double)height / (KodakDc25File.RenderedHeight * (double)width)
      : 4.0 * height / (3.0 * width);

    if (aspect < 1) {
      var taller = _StretchDown(rgb, width, height, (int)(height / aspect + 0.5));
      return new() { Width = width, Height = taller.Length / 3 / width, PixelData = taller };
    }

    var wider = _StretchAcross(rgb, width, height, (int)(width * aspect + 0.5));

    return new() {
      Width = wider.Length / 3 / height,
      Height = height,
      PixelData = wider,
    };
  }

  /// <summary>The picture resampled to a taller grid, a row at a time.</summary>
  private static byte[] _StretchDown(byte[] rgb, int width, int height, int newHeight) {
    var result = new byte[width * newHeight * 3];

    for (var y = 0; y < newHeight; ++y) {
      var source = (double)y * (height - 1) / (newHeight - 1);
      var top = (int)source;
      var bottom = Math.Min(top + 1, height - 1);
      var weight = (float)(source - top);

      for (var x = 0; x < width * 3; ++x)
        result[y * width * 3 + x] = (byte)(rgb[top * width * 3 + x] * (1 - weight) + rgb[bottom * width * 3 + x] * weight + 0.5f);
    }

    return result;
  }

  /// <summary>The picture resampled to a wider grid, a column at a time.</summary>
  private static byte[] _StretchAcross(byte[] rgb, int width, int height, int newWidth) {
    var result = new byte[newWidth * height * 3];

    for (var x = 0; x < newWidth; ++x) {
      var source = (double)x * (width - 1) / (newWidth - 1);
      var left = (int)source;
      var right = Math.Min(left + 1, width - 1);
      var weight = (float)(source - left);

      for (var y = 0; y < height; ++y)
        for (var c = 0; c < 3; ++c)
          result[(y * newWidth + x) * 3 + c] =
            (byte)(rgb[(y * width + left) * 3 + c] * (1 - weight) + rgb[(y * width + right) * 3 + c] * weight + 0.5f);
    }

    return result;
  }

  public static KodakDc25File FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }

  private static byte _ToSrgb(float linear) {
    linear = Math.Clamp(linear, 0f, 1f);
    var srgb = linear <= 0.0031308f
      ? linear * 12.92f
      : 1.055f * MathF.Pow(linear, 1.0f / 2.4f) - 0.055f;

    return (byte)Math.Clamp((int)(srgb * 255.0f + 0.5f), 0, 255);
  }
}
