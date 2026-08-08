using System;
using System.Collections.Generic;

namespace FileFormat.MsxMig;

/// <summary>Assembles MSX MIG bytes from an <see cref="MsxMigFile"/>.</summary>
/// <remarks>
/// A MIG is a capture of what the video chip was doing, so writing one means saying which mode the
/// chip was in rather than which mode the picture is. Screen 8 is chosen: one byte a pixel with a
/// palette fixed in hardware, so no palette record is needed and nothing depends on a colour table
/// the file would also have to be trusted about.
/// <para/>
/// The records are stored rather than matched. The compression is a bit before every byte saying
/// whether it stands for itself, so a stream of literals costs one bit in nine — and a match finder
/// would save a screen's worth of bytes on a picture nobody has to load off tape any more.
/// </remarks>
public static class MsxMigWriter {

  /// <summary>Rows a screen record holds.</summary>
  public const int Rows = 212;

  /// <summary>Pixels across.</summary>
  public const int Columns = 256;

  /// <summary>Where the bit stream begins, past the signature, the length and five bytes nothing
  /// reads.</summary>
  private const int _STREAM_OFFSET = 15;

  public static byte[] ToBytes(MsxMigFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var records = new List<byte>(_STREAM_OFFSET + Rows * Columns);

    // The mode bits are spread over three registers and the picture's page count completes them;
    // these four are what screen 8, not interlaced, comes to.
    records.AddRange([0, 4, 0, 0x0E, 0xFF, 1, 0x00, 0xFF, 9, 0x00, 0xFF, 25, 0x00, 0xFF]);
    records.AddRange([2, 0, 0, 0, 0, Rows, 0]);
    records.AddRange(ToScreen(file));

    // One byte past the screen, which the record's stated length accounts for.
    records.Add(0);

    var data = new List<byte>(_STREAM_OFFSET + records.Count + records.Count / 8 + 8);
    data.AddRange("MSXMIG"u8.ToArray());
    data.AddRange(new byte[_STREAM_OFFSET - 6]);

    for (var at = 0; at < records.Count; at += 8) {
      // A clear bit before each byte says the byte stands for itself.
      data.Add(0);
      for (var i = at; i < Math.Min(at + 8, records.Count); ++i)
        data.Add(records[i]);
    }

    var bytes = data.ToArray();
    var length = bytes.Length - 6;
    bytes[6] = (byte)length;
    bytes[7] = (byte)(length >> 8);
    bytes[8] = (byte)(length >> 16);
    bytes[9] = (byte)(length >> 24);

    return bytes;
  }

  /// <summary>The screen's bytes: one hardware colour per pixel, sampled to the screen's own size.</summary>
  public static byte[] ToScreen(MsxMigFile file) {
    var palette = Screen8Palette();
    var pixels = file.Pixels ?? [];
    var screen = new byte[Rows * Columns];

    for (var y = 0; y < Rows; ++y) {
      var sourceY = file.Height > 0 ? (int)((long)y * file.Height / Rows) : 0;

      for (var x = 0; x < Columns; ++x) {
        var sourceX = file.Width > 0 ? (int)((long)x * file.Width / Columns) : 0;
        var at = (sourceY * file.Width + sourceX) * 3;
        if (at + 2 >= pixels.Length)
          continue;

        screen[y * Columns + x] = _Nearest(palette, pixels[at], pixels[at + 1], pixels[at + 2]);
      }
    }

    return screen;
  }

  /// <summary>
  /// Screen 8's fixed palette: three bits of red and green but only two of blue, the eye being least
  /// able to tell blues apart. The four blue levels are not evenly spaced either.
  /// </summary>
  public static byte[] Screen8Palette() {
    ReadOnlySpan<byte> blues = [0, 2, 4, 7];
    var palette = new byte[256 * 3];

    for (var c = 0; c < 256; ++c) {
      palette[c * 3] = Core.ChannelScaling.Expand3((c >> 2) & 7);
      palette[c * 3 + 1] = Core.ChannelScaling.Expand3((c >> 5) & 7);
      palette[c * 3 + 2] = Core.ChannelScaling.Expand3(blues[c & 3]);
    }

    return palette;
  }

  private static byte _Nearest(ReadOnlySpan<byte> palette, byte red, byte green, byte blue) {
    byte best = 0;
    var bestCost = int.MaxValue;

    for (var c = 0; c < 256; ++c) {
      var entry = c * 3;
      int dr = red - palette[entry], dg = green - palette[entry + 1], db = blue - palette[entry + 2];
      var cost = dr * dr + dg * dg + db * db;
      if (cost >= bestCost)
        continue;

      bestCost = cost;
      best = (byte)c;
    }

    return best;
  }
}
