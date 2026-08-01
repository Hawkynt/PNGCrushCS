using System;
using System.Globalization;
using System.Text;

namespace FileFormat.ScitexCt;

/// <summary>The 2048-byte header every Scitex CT file starts with.</summary>
/// <remarks>
/// Two blocks, not one. The first 1024 bytes are the SCITEX control block: eighty bytes of comment,
/// then the two letters naming what kind of file this is, then reserved space. The picture's own
/// parameters — how many separations, how large it is on paper, and how many pixels that is — begin
/// at 1024, written as fixed-width decimal text.
/// <para/>
/// This used to be an eighty-byte block with the two letters at the front, which is neither block
/// in the right place: a reader looking at offset 80 for the file type found pixels there.
/// </remarks>
internal static class ScitexCtHeader {

  /// <summary>Bytes before the picture starts.</summary>
  public const int StructSize = 2048;

  /// <summary>Where the two letters naming the file type sit.</summary>
  private const int TypeOffset = 80;

  /// <summary>Where the picture's own parameters begin.</summary>
  private const int ParametersOffset = 1024;

  /// <summary>What a continuous-tone picture's type field says.</summary>
  private static readonly byte[] _ContinuousTone = "CT"u8.ToArray();

  /// <summary>Which separations are present, as a bit per plate.</summary>
  private const int RgbMask = 0x07;

  private const int CmykMask = 0x0F;

  /// <summary>Writes the header for a picture of a given size and colour mode.</summary>
  public static void Write(Span<byte> target, int width, int height, ScitexCtColorMode mode, int resolution) {
    target[..StructSize].Fill((byte)' ');
    _ContinuousTone.CopyTo(target[TypeOffset..]);

    var separations = mode switch {
      ScitexCtColorMode.Grayscale => 1,
      ScitexCtColorMode.Rgb => 3,
      _ => 4,
    };

    var at = ParametersOffset;
    target[at] = 1;
    target[at + 1] = (byte)separations;
    target[at + 2] = 0;
    target[at + 3] = (byte)(mode == ScitexCtColorMode.Cmyk ? CmykMask : RgbMask);

    // The picture's size on paper, in the stated units, and then in pixels. Both are text: the
    // format predates any agreement on how to write a number in bytes.
    _Text(target[(at + 4)..], 14, (height / (double)resolution).ToString("F4", CultureInfo.InvariantCulture));
    _Text(target[(at + 18)..], 14, (width / (double)resolution).ToString("F4", CultureInfo.InvariantCulture));
    _Text(target[(at + 32)..], 12, height.ToString(CultureInfo.InvariantCulture));
    _Text(target[(at + 44)..], 12, width.ToString(CultureInfo.InvariantCulture));
  }

  /// <summary>Reads back what <see cref="Write"/> put there.</summary>
  public static (int Width, int Height, ScitexCtColorMode Mode) Read(ReadOnlySpan<byte> data) {
    var at = ParametersOffset;
    var separations = data[at + 1];
    var height = _Number(data.Slice(at + 32, 12));
    var width = _Number(data.Slice(at + 44, 12));

    return (width, height, separations switch {
      1 => ScitexCtColorMode.Grayscale,
      4 => ScitexCtColorMode.Cmyk,
      _ => ScitexCtColorMode.Rgb,
    });
  }

  /// <summary>Whether the two letters at offset eighty say this is a continuous-tone picture.</summary>
  public static bool IsContinuousTone(ReadOnlySpan<byte> data)
    => data.Length >= StructSize && data[TypeOffset] == 'C' && data[TypeOffset + 1] == 'T';

  private static void _Text(Span<byte> target, int width, string value) {
    target[..width].Fill((byte)' ');
    var text = Encoding.ASCII.GetBytes(value);
    if (text.Length > width)
      return;

    // Right-aligned, which is what a fixed-width decimal field means.
    text.CopyTo(target[(width - text.Length)..]);
  }

  private static int _Number(ReadOnlySpan<byte> field) {
    var value = 0;
    foreach (var b in field) {
      if (b is < (byte)'0' or > (byte)'9')
        continue;

      value = value * 10 + (b - '0');
    }

    return value;
  }
}
