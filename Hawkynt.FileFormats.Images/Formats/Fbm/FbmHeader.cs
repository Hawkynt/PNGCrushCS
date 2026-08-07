using System;
using System.Globalization;
using System.Text;

namespace FileFormat.Fbm;

/// <summary>The 256-byte header of a CMU Fuzzy Bitmap (FBM) file.</summary>
/// <remarks>
/// Every field is fixed-width decimal ASCII, right-aligned and null-terminated — not a binary
/// integer. This was written as big-endian integers, which is why the one tool that reads these
/// said "invalid number of planes" of what it produced: it was parsing a binary 3 as characters.
/// <para/>
/// The note that used to sit here said the pixel layout could not be established, that probing for
/// it blind had not converged, and that nothing available could produce a reference. A real file
/// settled it. The picture is stored plane by plane and the rows run bottom to top: an 800 by 600
/// sample in three bands is 256 bytes of header and then three planes of 480000, which is exactly
/// its length, and read that way it matches the reference tool on every pixel.
/// <para/>
/// Layout, all fields decimal ASCII:
/// <code>
///   0   8  magic "%bitmap\0"
///   8   8  cols
///  16   8  rows
///  24   8  bands       1 for grey, 3 for colour
///  32   8  bits        bits per band
///  40   8  physbits    bits per band as stored
///  48  12  rowlen      bytes one row of ONE plane takes, padding included
///  60  12  plnlen      bytes one whole plane takes
///  72  12  clrlen      bytes of colour map between the header and the picture
///  84  12  aspect      pixel aspect, written as a decimal fraction
///  96 160  title       null-terminated, zero-padded
/// </code>
/// </remarks>
public readonly record struct FbmHeader(
  int Cols, int Rows, int Bands, int Bits, int PhysBits, int RowLen, int PlnLen, int ClrLen, double Aspect, string Title
) {

  public const int StructSize = 256;

  /// <summary>The 8-byte magic signature including null terminator: "%bitmap\0".</summary>
  public static readonly byte[] MagicBytes = [(byte)'%', (byte)'b', (byte)'i', (byte)'t', (byte)'m', (byte)'a', (byte)'p', 0];

  private const int _ColsAt = 8, _RowsAt = 16, _BandsAt = 24, _BitsAt = 32, _PhysBitsAt = 40;
  private const int _RowLenAt = 48, _PlnLenAt = 60, _ClrLenAt = 72, _AspectAt = 84, _TitleAt = 96;

  /// <summary>Width of the short fields, which hold the counts.</summary>
  private const int _ShortField = 8;

  /// <summary>Width of the long fields, which hold the lengths.</summary>
  private const int _LongField = 12;

  private const int _TitleField = StructSize - _TitleAt;

  public static FbmHeader ReadFrom(ReadOnlySpan<byte> data) => new(
    _Number(data, _ColsAt, _ShortField),
    _Number(data, _RowsAt, _ShortField),
    _Number(data, _BandsAt, _ShortField),
    _Number(data, _BitsAt, _ShortField),
    _Number(data, _PhysBitsAt, _ShortField),
    _Number(data, _RowLenAt, _LongField),
    _Number(data, _PlnLenAt, _LongField),
    _Number(data, _ClrLenAt, _LongField),
    _Fraction(data, _AspectAt, _LongField),
    _Text(data, _TitleAt, _TitleField));

  public void WriteTo(Span<byte> target) {
    MagicBytes.CopyTo(target);
    _Write(target, _ColsAt, _ShortField, this.Cols.ToString(CultureInfo.InvariantCulture));
    _Write(target, _RowsAt, _ShortField, this.Rows.ToString(CultureInfo.InvariantCulture));
    _Write(target, _BandsAt, _ShortField, this.Bands.ToString(CultureInfo.InvariantCulture));
    _Write(target, _BitsAt, _ShortField, this.Bits.ToString(CultureInfo.InvariantCulture));
    _Write(target, _PhysBitsAt, _ShortField, this.PhysBits.ToString(CultureInfo.InvariantCulture));
    _Write(target, _RowLenAt, _LongField, this.RowLen.ToString(CultureInfo.InvariantCulture));
    _Write(target, _PlnLenAt, _LongField, this.PlnLen.ToString(CultureInfo.InvariantCulture));
    _Write(target, _ClrLenAt, _LongField, this.ClrLen.ToString(CultureInfo.InvariantCulture));
    _Write(target, _AspectAt, _LongField, this.Aspect.ToString("F6", CultureInfo.InvariantCulture));

    // The title is left-aligned where the numbers are right-aligned, being text rather than a value.
    var title = Encoding.ASCII.GetBytes(this.Title ?? string.Empty);
    title.AsSpan(0, Math.Min(title.Length, _TitleField - 1)).CopyTo(target[_TitleAt..]);
  }

  /// <summary>Right-aligns a value in its field, leaving the last byte as the terminator.</summary>
  private static void _Write(Span<byte> target, int at, int width, string value) {
    if (value.Length > width - 1)
      value = value[^(width - 1)..];

    var start = at + width - 1 - value.Length;
    for (var i = at; i < start; ++i)
      target[i] = (byte)' ';

    Encoding.ASCII.GetBytes(value).CopyTo(target[start..]);
  }

  private static string _Text(ReadOnlySpan<byte> data, int at, int width) {
    if (at + width > data.Length)
      return string.Empty;

    var field = data.Slice(at, width);
    var end = field.IndexOf((byte)0);

    return Encoding.ASCII.GetString((end < 0 ? field : field[..end]).ToArray()).Trim();
  }

  private static int _Number(ReadOnlySpan<byte> data, int at, int width)
    => int.TryParse(_Text(data, at, width), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : 0;

  private static double _Fraction(ReadOnlySpan<byte> data, int at, int width)
    => double.TryParse(_Text(data, at, width), NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value : 1.0;
}
