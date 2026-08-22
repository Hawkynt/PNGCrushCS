using System;

namespace FileFormat.Codecs.Asv1;

/// <summary>
/// Undoes ASV1's byte order before anything else in the packet is read.
/// </summary>
/// <remarks>
/// Michael Niedermayer's <c>ASUS V1/V2 Codecs</c> (asv1.txt, 2003, GNU FDL/GPL) states the packet is
/// "stored with byte-swapped 32bit words (24..31, 16..23, 8..15, 0..7, 56..63, 48..55, 40..47,
/// 32..39, ...)" — every run of four bytes has its own order reversed, so the byte that carries bits
/// 24 to 31 of the logical stream is the first one on disk. Reversing each four-byte group once, up
/// front, turns the rest of the decoder into an ordinary most-significant-bit-first reader and keeps
/// that one oddity in a single place rather than folded into every field read afterwards.
/// <para/>
/// A trailing group of one to three bytes — a packet is not guaranteed to be a whole number of words —
/// is reversed exactly as far as it goes; nothing here has ever needed a fourth byte of one because the
/// bits it would hold are past the last macroblock and never read.
/// </remarks>
internal static class Asv1Bitstream {

  /// <summary>Returns a new buffer with every four-byte word's byte order reversed.</summary>
  internal static byte[] SwapWords(ReadOnlySpan<byte> packet) {
    var result = new byte[packet.Length];
    var wholeWords = packet.Length & ~3;

    for (var i = 0; i < wholeWords; i += 4) {
      result[i] = packet[i + 3];
      result[i + 1] = packet[i + 2];
      result[i + 2] = packet[i + 1];
      result[i + 3] = packet[i];
    }

    var trailing = packet.Length - wholeWords;
    for (var k = 0; k < trailing; ++k)
      result[wholeWords + (trailing - 1 - k)] = packet[wholeWords + k];

    return result;
  }
}
