using System;
using System.Globalization;
using System.Text;

namespace FileFormat.PostScript;

/// <summary>
/// Emits the Level-1 program that paints one RGB raster across a box of a stated size.
/// </summary>
/// <remarks>
/// Three writers in this package put a picture into the PostScript language and each needs the same
/// dozen lines: a row buffer, the transform that turns the unit square into the box, the operand
/// list <c>colorimage</c> wants, and the samples themselves as ASCII hex. They are here once because
/// the alternative is three copies drifting apart, and one of them drifting is what let an EPS ship
/// whose PostScript section drew nothing at all.
/// <para/>
/// Level 1 on purpose. <c>colorimage</c> with a procedure source is the oldest way of saying this
/// and every interpreter written since understands it, which is the point of writing it down.
/// </remarks>
internal static class PostScriptRaster {

  /// <summary>How many sample bytes go on one line of hex, before the pair doubles it.</summary>
  private const int _BYTES_PER_LINE = 64;

  private static ReadOnlySpan<byte> _Hex => "0123456789ABCDEF"u8;

  /// <summary>
  /// The program that draws <paramref name="rgb24"/> over a box <paramref name="boxWidth"/> by
  /// <paramref name="boxHeight"/> points with its origin at the bottom left, first row at the top.
  /// </summary>
  /// <param name="rgb24">Packed RGB samples, three bytes to the pixel, first row first.</param>
  /// <param name="width">Pixels across.</param>
  /// <param name="height">Rows.</param>
  /// <param name="boxWidth">How wide the drawn picture is, in points.</param>
  /// <param name="boxHeight">How tall the drawn picture is, in points.</param>
  public static byte[] Draw(ReadOnlySpan<byte> rgb24, int width, int height, double boxWidth, double boxHeight) {
    var lineBytes = checked(width * 3);

    var header = new StringBuilder(256);
    header.Append("/picstr ").Append(lineBytes).Append(" string def\n");
    header.Append("gsave\n");
    header.Append(Number(boxWidth)).Append(' ').Append(Number(boxHeight)).Append(" scale\n");
    header.Append(width).Append(' ').Append(height).Append(" 8\n");

    // PostScript puts the image's own origin at the bottom left of the unit square and a raster's
    // first row at the top, so the matrix flips y. Without it the picture comes out upside down,
    // which is a decode nothing measuring geometry would notice.
    header.Append('[').Append(width).Append(" 0 0 -").Append(height).Append(" 0 ").Append(height).Append("]\n");
    header.Append("{ currentfile picstr readhexstring pop }\n");
    header.Append("false 3 colorimage\n");

    var prefix = Encoding.ASCII.GetBytes(header.ToString());
    var suffix = "\ngrestore\n"u8;

    var samples = checked(width * height * 3);
    var digits = checked(samples * 2);
    var breaks = samples == 0 ? 0 : (samples - 1) / _BYTES_PER_LINE;
    var output = new byte[checked(prefix.Length + digits + breaks + suffix.Length)];
    prefix.CopyTo(output, 0);

    var at = prefix.Length;
    for (var i = 0; i < samples; ++i) {
      if (i != 0 && i % _BYTES_PER_LINE == 0)
        output[at++] = (byte)'\n';

      // A picture whose buffer is short of its stated size is padded with black rather than refused,
      // because the alternative is a writer that throws on a picture its caller thinks is complete.
      var value = i < rgb24.Length ? rgb24[i] : (byte)0;
      output[at++] = _Hex[value >> 4];
      output[at++] = _Hex[value & 15];
    }

    suffix.CopyTo(output.AsSpan(at));
    return output;
  }

  /// <summary>A length in points, as short as it can be written without losing it.</summary>
  public static string Number(double value) => value.ToString("0.##", CultureInfo.InvariantCulture);

  /// <summary>
  /// The same length rounded outwards to whole points, which is the only shape <c>%%BoundingBox</c>
  /// takes.
  /// </summary>
  /// <remarks>
  /// DSC states that comment as four integers, and a cropper handed a fraction there either rejects
  /// the line or truncates it. Rounding up rather than down keeps the stated page from being smaller
  /// than the picture drawn on it; <c>%%HiResBoundingBox</c> beside it carries the exact figure for
  /// anything that can use one.
  /// </remarks>
  public static string Whole(double value) => ((long)Math.Ceiling(value)).ToString(CultureInfo.InvariantCulture);
}
