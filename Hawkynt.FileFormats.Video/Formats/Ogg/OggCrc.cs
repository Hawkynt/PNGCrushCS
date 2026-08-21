using System;

namespace FileFormat.Ogg;

/// <summary>
/// The checksum an Ogg page carries over itself.
/// </summary>
/// <remarks>
/// A CRC-32 that is not the CRC-32 anything else uses. It shares the generator polynomial 0x04C11DB7
/// with Ethernet and PNG and differs from both in every other respect: the message is not reflected,
/// the register is not reflected, it starts at zero rather than at all ones, and the result is not
/// inverted. Feeding an Ogg page to a stock CRC-32 gives a number that is wrong for every page, which
/// is a pleasant kind of bug to have — it fails immediately rather than on one file in a thousand.
/// <para/>
/// The awkward part is what the checksum is computed over: the whole page, header and body together,
/// with the checksum field itself read as four zero bytes. So a page cannot be summed in one pass over
/// the bytes as they lie in the file — the four bytes at offset 22 have to be substituted. This is
/// done here by summing the three runs in order rather than by copying the page aside, so verifying a
/// film's worth of pages allocates nothing.
/// <para/>
/// RFC 3533 section 6, and the specification's own reference table.
/// </remarks>
internal static class OggCrc {

  /// <summary>The generator polynomial, written the way an unreflected implementation shifts it.</summary>
  private const uint _POLYNOMIAL = 0x04C11DB7;

  /// <summary>Where the checksum field sits in a page header.</summary>
  internal const int CHECKSUM_AT = 22;

  /// <summary>How many bytes the checksum field occupies.</summary>
  internal const int CHECKSUM_SIZE = 4;

  private static readonly uint[] _table = _BuildTable();

  private static uint[] _BuildTable() {
    var table = new uint[256];
    for (var i = 0; i < 256; ++i) {
      var register = (uint)i << 24;
      for (var bit = 0; bit < 8; ++bit)
        register = (register & 0x80000000) != 0 ? (register << 1) ^ _POLYNOMIAL : register << 1;

      table[i] = register;
    }

    return table;
  }

  /// <summary>Runs the checksum over a run of bytes, continuing from a register.</summary>
  private static uint _Update(uint register, ReadOnlySpan<byte> data) {
    foreach (var value in data)
      register = (register << 8) ^ _table[(byte)((register >> 24) ^ value)];

    return register;
  }

  /// <summary>
  /// The checksum a page should carry, computed over the page with its checksum field zeroed.
  /// </summary>
  /// <param name="page">The whole page: its header, its segment table and its body.</param>
  internal static uint Compute(ReadOnlySpan<byte> page) {
    // The three runs in order — up to the field, four zeroes standing in for the field, and the rest.
    // Substituting rather than copying the page aside, because a two-hour recording is a million
    // pages and a copy of each is a copy of the film.
    var register = _Update(0, page[..CHECKSUM_AT]);
    for (var i = 0; i < CHECKSUM_SIZE; ++i)
      register = (register << 8) ^ _table[(byte)(register >> 24)];

    return _Update(register, page[(CHECKSUM_AT + CHECKSUM_SIZE)..]);
  }
}
