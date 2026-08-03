using FileFormat.Core;

namespace FileFormat.MegaPaint;

/// <summary>
/// The four-byte header of a MegaPaint file: the last column and the last row, both big-endian.
/// </summary>
/// <remarks>
/// This was read as eight bytes holding a width and a height. Both were wrong, and the sample settles
/// each by arithmetic. A picture RECOIL and XnView both draw 480 by 1728 states 479 and 1727, so the
/// numbers are the last column and row rather than the counts; and 480 bits a row is 60 bytes, which
/// over 1728 rows is 103680 — four less than the file, not eight.
/// </remarks>
[GenerateSerializer, Endian(Endianness.Big)]
internal readonly partial record struct MegaPaintHeader(
  ushort LastColumn,
  ushort LastRow
) {
  public const int StructSize = 4;

  public int Width => this.LastColumn + 1;
  public int Height => this.LastRow + 1;
}
