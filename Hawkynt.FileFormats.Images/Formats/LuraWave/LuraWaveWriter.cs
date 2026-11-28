using System;
using FileFormat.Core;

namespace FileFormat.LuraWave;

/// <summary>Writes LuraWave files: lossless, single stripe.</summary>
/// <remarks>
/// The colour mode is chosen from the pixels: 8-bit grey stays <see cref="LuraWaveColourMode.Grey"/>, 16-bit grey
/// becomes <see cref="LuraWaveColourMode.WideGrey"/>, RGB is coded in <see cref="LuraWaveColourMode.ReversibleColour"/>
/// and RGBA in <see cref="LuraWaveColourMode.RawFourChannel"/>. A file in mode 5, 20 or 21 that carries
/// <see cref="LuraWaveFile.Channels"/> is written in its own mode from those channels, so nothing is lost. A file read
/// in any other mode is written in whichever of the pixel modes its decoded pixels call for, so the picture — not the
/// original coding — is what survives. The resolution of a file that was read is carried over.
/// </remarks>
public static class LuraWaveWriter {

  /// <exception cref="ArgumentException">The pixel buffer or the channel samples do not match the dimensions.</exception>
  public static byte[] ToBytes(LuraWaveFile file) {
    if (file.Width <= 0 || file.Height <= 0)
      throw new ArgumentException($"LuraWave needs a non-empty image, not {file.Width}x{file.Height}.", nameof(file));

    if (file.Channels is { } channels && file.Mode is LuraWaveColourMode.RawFourChannel or LuraWaveColourMode.WideLab or LuraWaveColourMode.WideFourChannel) {
      if (channels.Length != (long)file.Width * file.Height * file.ChannelCount)
        throw new ArgumentException($"Channel samples must be {(long)file.Width * file.Height * file.ChannelCount} for {file.Width}x{file.Height} in mode {file.Mode}.", nameof(file));

      return LuraWaveCodec.EncodeChannels(channels, file.Width, file.Height, file.Mode, file.Header?.MaxValue ?? 0, file.Header);
    }

    var format = file.PixelFormat;
    var bytesPerPixel = format switch {
      PixelFormat.Gray8 => 1,
      PixelFormat.Gray16 => 2,
      PixelFormat.Rgba32 => 4,
      _ => 3,
    };
    if (file.PixelData is null || file.PixelData.Length != (long)file.Width * file.Height * bytesPerPixel)
      throw new ArgumentException($"Pixel data must be {(long)file.Width * file.Height * bytesPerPixel} bytes for {file.Width}x{file.Height} {format}.", nameof(file));

    var mode = format switch {
      PixelFormat.Gray8 => LuraWaveColourMode.Grey,
      PixelFormat.Gray16 => LuraWaveColourMode.WideGrey,
      PixelFormat.Rgba32 => LuraWaveColourMode.RawFourChannel,
      _ => LuraWaveColourMode.ReversibleColour,
    };
    return LuraWaveCodec.Encode(file.PixelData, file.Width, file.Height, mode, file.Header);
  }
}
