using System;
using System.Collections.Generic;

namespace FileFormat.Core;

/// <summary>
/// The run-length scheme the Amiga's RGBN and RGB8 pictures use, which the BMHD calls compression 4.
/// </summary>
/// <remarks>
/// Unlike ByteRun1, which packs bytes and knows nothing of pixels, this packs whole colours: each
/// unit carries a colour and how many pixels in a row have it. RGBN spends two bytes on four bits a
/// channel and a three-bit count; RGB8 spends four on eight bits a channel and a seven-bit count.
/// <para/>
/// A count of zero in that field does not mean an empty run — it means the count did not fit, and
/// one more byte follows; a zero there in turn means a sixteen-bit count follows. So the short form
/// costs nothing and a run of any length is still expressible.
/// </remarks>
public static class AmigaRgbRuns {

  /// <summary>What the BMHD's compression byte says for both of these.</summary>
  public const byte CompressionMethod = 4;

  /// <summary>Bitplanes an RGBN picture declares: twelve of colour and one of genlock.</summary>
  public const byte RgbnBitplanes = 13;

  /// <summary>Bitplanes an RGB8 picture declares: twenty-four of colour and one of genlock.</summary>
  public const byte Rgb8Bitplanes = 25;

  /// <summary>Unpacks a body into RGB triplets.</summary>
  /// <param name="body">The BODY chunk's contents.</param>
  /// <param name="width">Pixels across.</param>
  /// <param name="height">Rows.</param>
  /// <param name="deep">Whether this is the eight-bit form rather than the four-bit one.</param>
  public static byte[] Unpack(ReadOnlySpan<byte> body, int width, int height, bool deep) {
    var rgb = new byte[width * height * 3];
    var at = 0;
    var count = 0;
    byte r = 0, g = 0, b = 0;

    for (var i = 0; i < width * height; ++i) {
      if (count == 0) {
        if (deep) {
          if (at > body.Length - 4)
            break;

          r = body[at];
          g = body[at + 1];
          b = body[at + 2];
          count = body[at + 3] & 127;
          at += 4;
        } else {
          if (at > body.Length - 2)
            break;

          // Four bits a channel, widened by repeating them — which is what multiplying by 17 does.
          r = (byte)((body[at] >> 4) * 17);
          g = (byte)((body[at] & 15) * 17);
          b = (byte)((body[at + 1] >> 4) * 17);
          count = body[at + 1] & 7;
          at += 2;
        }

        if (count == 0) {
          if (at >= body.Length)
            break;

          count = body[at++];
          if (count == 0) {
            if (at > body.Length - 2)
              break;

            count = (body[at] << 8) | body[at + 1];
            at += 2;
          }
        }
      }

      rgb[i * 3] = r;
      rgb[i * 3 + 1] = g;
      rgb[i * 3 + 2] = b;
      --count;
    }

    return rgb;
  }

  /// <summary>Packs RGB triplets into a body.</summary>
  /// <remarks>
  /// Runs do not stop at the end of a row: the decoder walks the picture as one sequence, so a flat
  /// band spanning several rows costs one unit rather than one per row.
  /// </remarks>
  public static byte[] Pack(ReadOnlySpan<byte> rgb, int width, int height, bool deep) {
    var body = new List<byte>(width * height * (deep ? 4 : 2));
    var pixels = width * height;
    var i = 0;

    while (i < pixels) {
      var at = i * 3;
      byte r = rgb[at], g = rgb[at + 1], b = rgb[at + 2];
      if (!deep) {
        // Reduced first, so that two colours alike at four bits join into one run.
        r = (byte)((r + 8) / 17 * 17);
        g = (byte)((g + 8) / 17 * 17);
        b = (byte)((b + 8) / 17 * 17);
      }

      var run = 1;
      while (i + run < pixels && run < 65535) {
        var next = (i + run) * 3;
        byte nr = rgb[next], ng = rgb[next + 1], nb = rgb[next + 2];
        if (!deep) {
          nr = (byte)((nr + 8) / 17 * 17);
          ng = (byte)((ng + 8) / 17 * 17);
          nb = (byte)((nb + 8) / 17 * 17);
        }

        if (nr != r || ng != g || nb != b)
          break;

        ++run;
      }

      var inline = deep ? 127 : 7;
      var short_ = run <= inline ? run : 0;

      if (deep) {
        body.Add(r);
        body.Add(g);
        body.Add(b);
        // The top bit is the genlock flag: set means the pixel is opaque.
        body.Add((byte)(0x80 | short_));
      } else {
        body.Add((byte)(((r / 17) << 4) | (g / 17)));
        body.Add((byte)(((b / 17) << 4) | short_));
      }

      if (short_ == 0) {
        if (run <= 255)
          body.Add((byte)run);
        else {
          body.Add(0);
          body.Add((byte)(run >> 8));
          body.Add((byte)run);
        }
      }

      i += run;
    }

    return body.ToArray();
  }
}
