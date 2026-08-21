using System;
using System.Buffers.Binary;

namespace FileFormat.Asf;

/// <summary>
/// The sixteen-byte identifiers ASF names every one of its objects with, and the comparison that
/// recognises one.
/// </summary>
/// <remarks>
/// ASF is keyed entirely by GUID rather than by a four-character code, which is what makes the format
/// extensible and also what makes a reader of it a lookup table: an object whose identifier is not in
/// this list is skipped by its stated length and costs nothing, so a file carrying digital rights
/// management, a mutual exclusion or a bandwidth sharing object reads exactly as well as one that
/// does not. Clause numbers below are from the Advanced Systems Format specification, revision
/// 01.20.06.
/// <para/>
/// The bytes are stored the way they lie in the file, which is not the way the identifier is written
/// down. A GUID's first three fields are little-endian and its last two are a byte string, so
/// <c>75B22630-668E-11CF-A6D9-00AA0062CE6C</c> begins <c>30 26 B2 75</c> on disc. Keeping the stored
/// order here means a comparison is a memory compare and no byte-swapping happens per object.
/// </remarks>
internal static class AsfGuid {

  /// <summary>The length of an ASF object's identifier.</summary>
  internal const int SIZE = 16;

  // -------- Top-level objects (clause 3) --------

  /// <summary>Header Object, 75B22630-668E-11CF-A6D9-00AA0062CE6C (clause 3.1).</summary>
  internal static ReadOnlySpan<byte> Header => [0x30, 0x26, 0xB2, 0x75, 0x8E, 0x66, 0xCF, 0x11, 0xA6, 0xD9, 0x00, 0xAA, 0x00, 0x62, 0xCE, 0x6C];

  /// <summary>Data Object, 75B22636-668E-11CF-A6D9-00AA0062CE6C (clause 5.1).</summary>
  internal static ReadOnlySpan<byte> Data => [0x36, 0x26, 0xB2, 0x75, 0x8E, 0x66, 0xCF, 0x11, 0xA6, 0xD9, 0x00, 0xAA, 0x00, 0x62, 0xCE, 0x6C];

  // -------- Header objects (clause 3) --------

  /// <summary>File Properties Object, 8CABDCA1-A947-11CF-8EE4-00C00C205365 (clause 3.2).</summary>
  internal static ReadOnlySpan<byte> FileProperties => [0xA1, 0xDC, 0xAB, 0x8C, 0x47, 0xA9, 0xCF, 0x11, 0x8E, 0xE4, 0x00, 0xC0, 0x0C, 0x20, 0x53, 0x65];

  /// <summary>Stream Properties Object, B7DC0791-A9B7-11CF-8EE6-00C00C205365 (clause 3.3).</summary>
  internal static ReadOnlySpan<byte> StreamProperties => [0x91, 0x07, 0xDC, 0xB7, 0xB7, 0xA9, 0xCF, 0x11, 0x8E, 0xE6, 0x00, 0xC0, 0x0C, 0x20, 0x53, 0x65];

  /// <summary>Header Extension Object, 5FBF03B5-A92E-11CF-8EE3-00C00C205365 (clause 3.4).</summary>
  internal static ReadOnlySpan<byte> HeaderExtension => [0xB5, 0x03, 0xBF, 0x5F, 0x2E, 0xA9, 0xCF, 0x11, 0x8E, 0xE3, 0x00, 0xC0, 0x0C, 0x20, 0x53, 0x65];

  /// <summary>Codec List Object, 86D15240-311D-11D0-A3A4-00A0C90348F6 (clause 3.5).</summary>
  internal static ReadOnlySpan<byte> CodecList => [0x40, 0x52, 0xD1, 0x86, 0x1D, 0x31, 0xD0, 0x11, 0xA3, 0xA4, 0x00, 0xA0, 0xC9, 0x03, 0x48, 0xF6];

  /// <summary>Content Description Object, 75B22633-668E-11CF-A6D9-00AA0062CE6C (clause 3.10).</summary>
  internal static ReadOnlySpan<byte> ContentDescription => [0x33, 0x26, 0xB2, 0x75, 0x8E, 0x66, 0xCF, 0x11, 0xA6, 0xD9, 0x00, 0xAA, 0x00, 0x62, 0xCE, 0x6C];

  /// <summary>Extended Content Description Object, D2D0A440-E307-11D2-97F0-00A0C95EA850 (clause 3.11).</summary>
  internal static ReadOnlySpan<byte> ExtendedContentDescription => [0x40, 0xA4, 0xD0, 0xD2, 0x07, 0xE3, 0xD2, 0x11, 0x97, 0xF0, 0x00, 0xA0, 0xC9, 0x5E, 0xA8, 0x50];

  // -------- Header extension objects (clause 4) --------

  /// <summary>Extended Stream Properties Object, 14E6A5CB-C672-4332-8399-A96952065B5A (clause 4.1).</summary>
  internal static ReadOnlySpan<byte> ExtendedStreamProperties => [0xCB, 0xA5, 0xE6, 0x14, 0x72, 0xC6, 0x32, 0x43, 0x83, 0x99, 0xA9, 0x69, 0x52, 0x06, 0x5B, 0x5A];

  /// <summary>Language List Object, 7C4346A9-EFE0-4BFC-B229-393EDE415C85 (clause 4.6).</summary>
  internal static ReadOnlySpan<byte> LanguageList => [0xA9, 0x46, 0x43, 0x7C, 0xE0, 0xEF, 0xFC, 0x4B, 0xB2, 0x29, 0x39, 0x3E, 0xDE, 0x41, 0x5C, 0x85];

  // -------- Stream type identifiers (clause 10.4) --------

  /// <summary>Audio Media, F8699E40-5B4D-11CF-A8FD-00805F5C442B.</summary>
  internal static ReadOnlySpan<byte> AudioMedia => [0x40, 0x9E, 0x69, 0xF8, 0x4D, 0x5B, 0xCF, 0x11, 0xA8, 0xFD, 0x00, 0x80, 0x5F, 0x5C, 0x44, 0x2B];

  /// <summary>Video Media, BC19EFC0-5B4D-11CF-A8FD-00805F5C442B.</summary>
  internal static ReadOnlySpan<byte> VideoMedia => [0xC0, 0xEF, 0x19, 0xBC, 0x4D, 0x5B, 0xCF, 0x11, 0xA8, 0xFD, 0x00, 0x80, 0x5F, 0x5C, 0x44, 0x2B];

  /// <summary>Command Media, 59DACFC0-59E6-11D0-A3AC-00A0C90348F6.</summary>
  internal static ReadOnlySpan<byte> CommandMedia => [0xC0, 0xCF, 0xDA, 0x59, 0xE6, 0x59, 0xD0, 0x11, 0xA3, 0xAC, 0x00, 0xA0, 0xC9, 0x03, 0x48, 0xF6];

  /// <summary>Binary Media, 3AFB65E2-47EF-40F2-AC2C-70A90D71D343.</summary>
  internal static ReadOnlySpan<byte> BinaryMedia => [0xE2, 0x65, 0xFB, 0x3A, 0xEF, 0x47, 0xF2, 0x40, 0xAC, 0x2C, 0x70, 0xA9, 0x0D, 0x71, 0xD3, 0x43];

  /// <summary>
  /// Whether two identifiers are the same, by the bytes as they lie in the file.
  /// </summary>
  /// <remarks>
  /// A byte comparison rather than a <see cref="Guid"/> one on purpose: constructing a
  /// <see cref="Guid"/> per object to compare it against a constant would allocate nothing but would
  /// still swap six bytes each time, for a header holding a few dozen objects and a comparison that
  /// fails on the first byte in nearly every case.
  /// </remarks>
  internal static bool Equals(ReadOnlySpan<byte> stored, ReadOnlySpan<byte> known)
    => stored.Length >= SIZE && stored[..SIZE].SequenceEqual(known);

  /// <summary>Renders an identifier the way the specification writes it, for refusals.</summary>
  /// <remarks>
  /// A refusal has to name the object it could not make sense of, and sixteen bytes of hexadecimal is
  /// not a name anyone can look up. This is the one place the stored order is unpacked into the
  /// written one.
  /// </remarks>
  internal static string ToText(ReadOnlySpan<byte> stored) {
    if (stored.Length < SIZE)
      return "(truncated)";

    var guid = new Guid(
      BinaryPrimitives.ReadUInt32LittleEndian(stored),
      BinaryPrimitives.ReadUInt16LittleEndian(stored[4..]),
      BinaryPrimitives.ReadUInt16LittleEndian(stored[6..]),
      stored[8], stored[9], stored[10], stored[11], stored[12], stored[13], stored[14], stored[15]);

    return guid.ToString("D").ToUpperInvariant();
  }
}
