using System;
using System.IO;

namespace FileFormat.Codecs.Cinepak;

/// <summary>
/// One of a strip's two vector codebooks: 256 entries, each already converted to the four colours it
/// paints with.
/// </summary>
/// <remarks>
/// A codebook entry is four luminance samples and one chrominance pair, which is four colours. Those
/// four are what both coding types actually draw — a V1 block repeats each over a 2x2 square, a V4
/// block takes one from each of four entries — so the colour conversion happens here, once per entry
/// written, rather than once per pixel painted.
/// <para/>
/// Codebooks outlive the strip that defined them. An inter-coded strip usually updates a handful of
/// entries and leaves the rest, so what is not restated has to still be here from the frame before;
/// that is the greater part of what makes the format small.
/// </remarks>
internal sealed class CinepakCodebook {

  /// <summary>How many vectors a codebook holds, which is as many as a one-byte reference can name.</summary>
  internal const int Size = 256;

  /// <summary>Bytes one entry occupies in a colour chunk: four luminances and two chrominances.</summary>
  internal const int ColourEntryLength = 6;

  /// <summary>Bytes one entry occupies in a grey chunk, which carries no chrominance.</summary>
  internal const int GreyEntryLength = 4;

  /// <summary>Four RGB triplets per entry, laid end to end.</summary>
  private readonly byte[] _colours = new byte[Size * 12];

  /// <summary>The four colours of one entry, as twelve bytes of red, green and blue.</summary>
  internal ReadOnlySpan<byte> this[int entry] => this._colours.AsSpan(entry * 12, 12);

  /// <summary>Forgets every entry, as a strip that does not inherit a codebook must.</summary>
  internal void Clear() => Array.Clear(this._colours);

  /// <summary>
  /// Replaces entries from zero, as many as the chunk holds.
  /// </summary>
  /// <remarks>
  /// From zero and only as many as are given: a chunk of sixty-four entries restates the first
  /// sixty-four and says nothing about the rest, which keep whatever the frame before left in them.
  /// Clearing the remainder would throw away vectors a later block still refers to.
  /// </remarks>
  internal void ReplaceFromStart(ReadOnlySpan<byte> data, bool grey) {
    var stride = grey ? GreyEntryLength : ColourEntryLength;
    var entries = data.Length / stride;
    if (entries > Size)
      throw new InvalidDataException(
        $"A Cinepak codebook chunk carries {entries} entries where a codebook holds {Size}.");

    for (var entry = 0; entry < entries; ++entry)
      this._Store(entry, data.Slice(entry * stride, stride), grey);
  }

  /// <summary>
  /// Replaces the entries a bitmap of update flags names, and leaves the others.
  /// </summary>
  /// <remarks>
  /// The flags and the entries are interleaved rather than tabled: four bytes of flags cover the next
  /// thirty-two entries, the bodies of the ones whose bit is set follow immediately, and the next
  /// four bytes of flags come after those rather than at any fixed place. Reading all the flags first
  /// would need to know how many entries there were, which is the thing the flags are there to say.
  /// <para/>
  /// The chunk ends when its bytes do. A trailing flag word with no entries behind it is how an
  /// encoder says the last few are unchanged, so running out exactly at a word boundary is normal
  /// and not an error.
  /// </remarks>
  internal void Update(ReadOnlySpan<byte> data, bool grey) {
    var stride = grey ? GreyEntryLength : ColourEntryLength;
    var at = 0;
    var entry = 0;

    while (entry < Size && at + 4 <= data.Length) {
      var flags = (uint)((data[at] << 24) | (data[at + 1] << 16) | (data[at + 2] << 8) | data[at + 3]);
      at += 4;

      for (var bit = 0; bit < 32 && entry < Size; ++bit, ++entry) {
        if ((flags & (0x80000000u >> bit)) == 0)
          continue;

        if (at + stride > data.Length)
          throw new InvalidDataException(
            $"A Cinepak codebook update names entry {entry} as changed and the chunk holds "
            + $"{data.Length - at} of the {stride} bytes it takes.");

        this._Store(entry, data.Slice(at, stride), grey);
        at += stride;
      }
    }
  }

  private void _Store(int entry, ReadOnlySpan<byte> body, bool grey) {
    var into = this._colours.AsSpan(entry * 12, 12);
    if (grey)
      CinepakColorConversion.ToGrey(body, into);
    else
      CinepakColorConversion.ToRgb(body[..4], body[4], body[5], into);
  }
}
