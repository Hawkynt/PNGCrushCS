using System;
using FileFormat.Core;

namespace FileFormat.LuraWave;

/// <summary>Writes LuraWave files: lossless, single stripe.</summary>
/// <remarks>
/// The colour mode is chosen from the pixels: 8-bit grey stays <see cref="LuraWaveColourMode.Grey"/>, 16-bit grey
/// becomes <see cref="LuraWaveColourMode.WideGrey"/>, and RGB is coded in <see cref="LuraWaveColourMode.ReversibleColour"/>.
/// A file read in another colour mode is written in whichever of these its decoded pixels call for, so the picture —
/// not the original coding — is what survives. The resolution of a file that was read is carried over.
/// </remarks>
public static class LuraWaveWriter {

  /// <exception cref="ArgumentException">The pixel buffer does not match the dimensions.</exception>
  public static byte[] ToBytes(LuraWaveFile file) {
    if (file.Width <= 0 || file.Height <= 0)
      throw new ArgumentException($"LuraWave needs a non-empty image, not {file.Width}x{file.Height}.", nameof(file));

    var format = file.PixelFormat;
    var bytesPerPixel = format switch {
      PixelFormat.Gray8 => 1,
      PixelFormat.Gray16 => 2,
      _ => 3,
    };
    if (file.PixelData is null || file.PixelData.Length != (long)file.Width * file.Height * bytesPerPixel)
      throw new ArgumentException($"Pixel data must be {(long)file.Width * file.Height * bytesPerPixel} bytes for {file.Width}x{file.Height} {format}.", nameof(file));

    var mode = format switch {
      PixelFormat.Gray8 => LuraWaveColourMode.Grey,
      PixelFormat.Gray16 => LuraWaveColourMode.WideGrey,
      _ => LuraWaveColourMode.ReversibleColour,
    };
    return LuraWaveCodec.Encode(file.PixelData, file.Width, file.Height, mode, file.Header);
  }
}
