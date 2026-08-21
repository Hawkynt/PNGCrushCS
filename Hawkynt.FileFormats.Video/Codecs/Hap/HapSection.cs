using System;
using System.IO;

namespace FileFormat.Codecs.Hap;

/// <summary>
/// One section of a Hap frame: a type byte and a size, in a header that is four bytes or eight
/// depending on what it needs to hold.
/// </summary>
/// <remarks>
/// Every Hap frame is built from these. A header is four bytes — a 24-bit little-endian size and a
/// type byte — unless that size does not fit, in which case the first three bytes are all zero to say
/// so and the size moves to a 32-bit little-endian field after the type byte, making the header eight
/// bytes long. Nothing about the type byte's meaning changes; only how far the size had to reach.
/// </remarks>
internal readonly struct HapSection(byte type, int dataOffset, int dataLength) {

  public byte Type { get; } = type;

  /// <summary>Where this section's payload starts, relative to the buffer it was read from.</summary>
  public int DataOffset { get; } = dataOffset;

  public int DataLength { get; } = dataLength;

  /// <summary>The offset one past this section's payload — where the next sibling section begins.</summary>
  public int EndOffset => this.DataOffset + this.DataLength;

  /// <summary>
  /// Reads one section header at <paramref name="offset"/> of <paramref name="data"/>.
  /// </summary>
  public static HapSection ReadAt(ReadOnlySpan<byte> data, int offset, string what) {
    if (offset + 4 > data.Length)
      throw new InvalidDataException($"{what} ends before a four-byte section header fits at offset {offset}.");

    var b0 = data[offset];
    var b1 = data[offset + 1];
    var b2 = data[offset + 2];
    var type = data[offset + 3];

    if (b0 == 0 && b1 == 0 && b2 == 0) {
      if (offset + 8 > data.Length)
        throw new InvalidDataException($"{what} states an eight-byte section header at offset {offset} but ends before it fits.");

      var size = data[offset + 4] | (data[offset + 5] << 8) | (data[offset + 6] << 16) | (data[offset + 7] << 24);
      if (size < 0 || offset + 8 + size > data.Length)
        throw new InvalidDataException($"{what} states a section of {size} bytes at offset {offset} that runs past the end of the data.");

      return new(type, offset + 8, size);
    }

    var size24 = b0 | (b1 << 8) | (b2 << 16);
    if (offset + 4 + size24 > data.Length)
      throw new InvalidDataException($"{what} states a section of {size24} bytes at offset {offset} that runs past the end of the data.");

    return new(type, offset + 4, size24);
  }
}
