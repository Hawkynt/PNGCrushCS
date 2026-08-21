using System;

namespace FileFormat.Codecs.Ffv1;

/// <summary>
/// The check FFV1 protects its configuration record and, when asked, each slice with
/// (RFC 9043 §4.9.3).
/// </summary>
/// <remarks>
/// The ordinary 32-bit polynomial, but in its unreflected form, with no starting value and no final
/// inversion — so it is not the CRC-32 a zip file uses and a library routine will give the wrong
/// answer. The four bytes at the end of what it covers are chosen so that running it over the whole
/// thing, those four bytes included, leaves nothing; a decoder therefore checks by computing and
/// comparing against zero rather than against a stored number.
/// <para/>
/// It is the only thing in the format that can say a stream is damaged rather than merely
/// undecodable, which is most of why archives use FFV1 at all.
/// </remarks>
internal static class Ffv1Crc {

  private const uint _POLYNOMIAL = 0x04C11DB7;

  private static readonly uint[] _Table = _BuildTable();

  private static uint[] _BuildTable() {
    var table = new uint[256];
    for (var i = 0; i < 256; ++i) {
      var value = (uint)i << 24;
      for (var bit = 0; bit < 8; ++bit)
        value = (value & 0x80000000) != 0 ? (value << 1) ^ _POLYNOMIAL : value << 1;

      table[i] = value;
    }

    return table;
  }

  /// <summary>The remainder over a run of bytes, which is zero for one that carries its own parity.</summary>
  internal static uint Of(ReadOnlySpan<byte> data) {
    var crc = 0u;
    foreach (var b in data)
      crc = (crc << 8) ^ _Table[((crc >> 24) ^ b) & 0xFF];

    return crc;
  }
}
