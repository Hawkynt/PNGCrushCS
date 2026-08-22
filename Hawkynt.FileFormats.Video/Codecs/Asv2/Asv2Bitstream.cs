using System;

namespace FileFormat.Codecs.Asv2;

/// <summary>
/// Undoes ASV2's bit order before anything else in the packet is read.
/// </summary>
/// <remarks>
/// Michael Niedermayer's <c>ASUS V1/V2 Codecs</c> (asv1.txt, 2003, GNU FDL/GPL) states ASV2's packet
/// "is stored with the bits in each byte reversed so (7..0, 15..8, 23..16, 31..24, 39..32, ...)" —
/// unlike ASV1, whose oddity is which four-byte group a byte belongs to, ASV2's is which end of its own
/// byte a bit is read from: byte order is left alone and each byte's own eight bits run least
/// significant first rather than most significant first. Reversing every byte once, up front, leaves
/// the same ordinary most-significant-bit-first reader ASV1 uses for everything after it — for a
/// variable-length code, where only the order bits arrive in and not their place value matters.
/// <para/>
/// <b>A fixed-width field is not a variable-length code, and needs a second reversal on top.</b> The
/// coefficient group count, the DC field and an escaped level's own raw byte are plain binary numbers,
/// and reading them out of the once-reversed stream the same most-significant-bit-first way a
/// variable-length code is read answers with the bit-reversal of the field's real value — caught on a
/// flat frame, where every DC field has to equal the picture's known flat luminance and one field in six
/// came back as that value's own mirror image instead (1 read where the file states 128) until each
/// fixed-width field's own bits were reversed a second time, against the once-reversed stream, before
/// being assembled into a number. <see cref="ReadReversedBits"/> is that second reversal.
/// </remarks>
internal static class Asv2Bitstream {

  private static readonly byte[] _Reversed = _BuildTable();

  private static byte[] _BuildTable() {
    var table = new byte[256];
    for (var value = 0; value < 256; ++value) {
      byte reversed = 0;
      for (var bit = 0; bit < 8; ++bit)
        if ((value & (1 << bit)) != 0)
          reversed |= (byte)(1 << (7 - bit));

      table[value] = reversed;
    }

    return table;
  }

  /// <summary>Returns a new buffer with every byte's own bit order reversed.</summary>
  internal static byte[] ReverseBits(ReadOnlySpan<byte> packet) {
    var result = new byte[packet.Length];
    for (var i = 0; i < packet.Length; ++i)
      result[i] = _Reversed[packet[i]];

    return result;
  }

  /// <summary>
  /// Reads a fixed-width field — the coefficient group count, a DC field, an escaped level's raw byte —
  /// whose own bits need reversing a second time on top of <see cref="ReverseBits"/>'s byte-wide one.
  /// </summary>
  internal static int ReadReversedBits(ref H263.H263BitReader reader, int count) {
    var value = reader.ReadBits(count);
    var reversed = 0;
    for (var i = 0; i < count; ++i)
      reversed = (reversed << 1) | ((value >> i) & 1);

    return reversed;
  }
}
