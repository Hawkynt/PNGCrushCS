using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.Pcd;

/// <summary>Assembles a Kodak Photo CD: the pyramid of sizes, each at the place the format fixes.</summary>
/// <remarks>
/// A Photo CD is not one picture but the same one at three sizes, and a reader picks whichever it
/// wants — so all three are written. Anything else produces a file that opens at a size the caller
/// did not ask for, or not at all.
/// </remarks>
public static class PcdWriter {

  public static byte[] ToBytes(PcdFile file) {
    ArgumentNullException.ThrowIfNull(file);

    return Assemble(file.Width, file.Height, file.PixelData, photoYcc: true);
  }

  /// <summary>Lays the pyramid out and fills every size in it.</summary>
  /// <param name="photoYcc">
  /// Whether the planes are to hold Photo YCC, which is what a <c>.pcd</c> means by them. A
  /// <c>.pcds</c> holds the channels themselves, so it says no.
  /// </param>
  internal static byte[] Assemble(int width, int height, byte[]? pixelData, bool photoYcc) {
    var source = new RawImage {
      Width = width,
      Height = height,
      Format = PixelFormat.Rgb24,
      PixelData = pixelData ?? new byte[width * height * 3],
    };

    var last = PcdFile.Resolutions[^1];
    var result = new byte[last.Offset + PcdFile.PlaneBytes(last.Width, last.Height)];
    PcdFile.Magic.CopyTo(result.AsSpan(PcdFile.PreambleSize));

    foreach (var (planeWidth, planeHeight, offset) in PcdFile.Resolutions)
      _Encode(source.SampleTo(planeWidth, planeHeight), planeWidth, planeHeight, result.AsSpan(offset), photoYcc);

    return result;
  }

  /// <summary>Writes one resolution: two rows of luminance, then a row of each chrominance.</summary>
  private static void _Encode(RawImage image, int width, int height, Span<byte> target, bool photoYcc) {
    var half = width / 2;
    var groupBytes = width * 2 + half * 2;
    var rgb = image.PixelData;

    for (var y = 0; y < height; ++y) {
      var group = y / 2;
      var luminanceRow = group * groupBytes + (y & 1) * width;

      for (var x = 0; x < width; ++x) {
        var (luminance, _, _) = _FromRgb(rgb, (y * width + x) * 3, photoYcc);
        target[luminanceRow + x] = luminance;
      }
    }

    // One chrominance sample covers two pixels each way, so it is the mean of the four it stands for.
    for (var y = 0; y < height; y += 2)
    for (var x = 0; x < width; x += 2) {
      int blue = 0, red = 0, count = 0;

      for (var dy = 0; dy < 2 && y + dy < height; ++dy)
      for (var dx = 0; dx < 2 && x + dx < width; ++dx) {
        var (_, sampleBlue, sampleRed) = _FromRgb(rgb, ((y + dy) * width + x + dx) * 3, photoYcc);
        blue += sampleBlue;
        red += sampleRed;
        ++count;
      }

      var group = y / 2;
      var blueRow = group * groupBytes + width * 2;
      target[blueRow + (x >> 1)] = (byte)(blue / count);
      target[blueRow + half + (x >> 1)] = (byte)(red / count);
    }
  }

  /// <summary>The inverse of the Photo CD colour transform.</summary>
  /// <remarks>
  /// The reading side fits an extended range into a byte rather than clipping it, so the writing
  /// side has to undo that fit before anything else — otherwise a picture comes back about
  /// three-quarters as bright as it went in, which looks like a plausible picture and is not the
  /// one that was written.
  /// </remarks>
  private static (byte Luminance, byte Blue, byte Red) _FromRgb(ReadOnlySpan<byte> rgb, int at, bool photoYcc) {
    if (at + 2 >= rgb.Length)
      return photoYcc ? ((byte)0, (byte)156, (byte)137) : ((byte)0, (byte)0, (byte)0);

    // Without the transform there is nothing to invert: the three channels go into the three planes
    // as they stand, which is what the reading side takes back out of them.
    if (!photoYcc)
      return (rgb[at], rgb[at + 1], rgb[at + 2]);

    const double toExtended = PcdReader.ExtendedRange / 255.0;
    double red = rgb[at] * toExtended, green = rgb[at + 1] * toExtended, blue = rgb[at + 2] * toExtended;

    var level = 0.299 * red + 0.587 * green + 0.114 * blue;

    return (
      _Clamp(level / 1.3584),
      _Clamp((blue - level) / 2.2179 + 156),
      _Clamp((red - level) / 1.8215 + 137));
  }

  private static byte _Clamp(double value) => value <= 0 ? (byte)0 : value >= 255 ? (byte)255 : (byte)(value + 0.5);

  public static void ToFile(PcdFile file, FileInfo target) {
    ArgumentNullException.ThrowIfNull(target);
    File.WriteAllBytes(target.FullName, ToBytes(file));
  }
}
