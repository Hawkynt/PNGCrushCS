using System;
using FileFormat.Mapletown;

namespace FileFormat.MapletownMx1;

/// <summary>Assembles Mapletown Network MX1 picture bytes from a <see cref="MapletownMx1File"/>.</summary>
/// <remarks>
/// One image, never several: what a file holding several adds up to is decided by their sizes, so a
/// picture written as four equal images would be read back as a two-by-two grid of quarters of
/// itself. A single image says what it is.
/// </remarks>
public static class MapletownMx1Writer {

  /// <summary>Levels each channel of a colour can take.</summary>
  public const int Levels = MapletownPicture.Levels;

  /// <summary>Colours a palette can hold.</summary>
  public const int PaletteSize = MapletownPicture.PaletteSize;

  /// <summary>Snaps one channel to the nine levels a colour is written in.</summary>
  public static int Level(int channel) => MapletownPicture.Level(channel);

  /// <summary>What one of those nine levels shows as.</summary>
  public static byte Channel(int level) => MapletownPicture.Channel(level);

  /// <summary>
  /// Reduces a picture to the palette a file can hold: at most 128 colours, each of them a number in
  /// base nine with a digit per channel.
  /// </summary>
  public static (int[] Colors, int[] Indices) Reduce(ReadOnlySpan<byte> rgb, int pixels)
    => MapletownPicture.Reduce(rgb, pixels);

  /// <summary>The colour a palette entry shows as, three bytes.</summary>
  public static (byte Red, byte Green, byte Blue) Expand(int color) => MapletownPicture.Expand(color);

  public static byte[] ToBytes(MapletownMx1File file) {
    var width = file.Width;
    var height = file.Height;
    if (width < 1 || height < 1)
      throw new ArgumentException("A picture needs at least one pixel.", nameof(file));

    // The end of the picture is announced as a length one past its pixel count, and a length is what
    // it is: twenty-one bits at the widest. A larger picture has no way to say it has finished.
    if ((long)width * height + 1 > MapletownEncoder.MaxLength)
      throw new ArgumentException(
        $"A picture is at most {MapletownEncoder.MaxLength - 1} pixels; this one is {(long)width * height}.",
        nameof(file));

    var encoder = new MapletownEncoder();

    // The reader hunts for this line rather than counting bytes to it, and takes the bit stream to
    // start at the character after it.
    encoder.Text($"@@@ Mapletown ({height} lines) @@@\n");

    MapletownPicture.WriteImage(encoder, width, height, file.Pixels ?? []);
    encoder.Text("\n");

    return encoder.ToArray();
  }
}
