using System;
using System.Buffers.Binary;
using System.IO;

namespace FileFormat.Ogg;

/// <summary>
/// One Ogg page: the unit the format is built out of, and the only thing in an Ogg file that is not
/// somebody else's bytes.
/// </summary>
/// <remarks>
/// A page is a header of twenty-seven bytes, a segment table of up to two hundred and fifty-five
/// one-byte lacing values, and a body that is the sum of them. It belongs to exactly one logical
/// bitstream, named by the serial number in its header; pages of several bitstreams are interleaved
/// in one file and a reader separates them by that number and by nothing else.
/// <para/>
/// The lacing is where the packet boundaries are, and it is a counting scheme rather than a length
/// field. A packet is written as as many 255-byte segments as it needs followed by one shorter one;
/// the shorter one is what ends it. So a packet of 700 bytes is 255, 255, 190, and a packet of exactly
/// 510 bytes is 255, 255, 0 — the terminating zero is not padding and a reader that dropped it would
/// join that packet to the next. A page whose last lacing value is 255 ends in the middle of a packet,
/// which continues on the next page of the same bitstream.
/// <para/>
/// RFC 3533 section 6.
/// </remarks>
internal readonly struct OggPage {

  /// <summary>The four bytes every page begins with.</summary>
  internal static ReadOnlySpan<byte> CapturePattern => "OggS"u8;

  /// <summary>The size of a page header before its segment table.</summary>
  internal const int HEADER_SIZE = 27;

  /// <summary>The only version RFC 3533 defines.</summary>
  internal const int VERSION = 0;

  /// <summary>The lacing value that means "this packet continues in the next segment".</summary>
  internal const int CONTINUATION_LACING = 255;

  /// <summary>Where the segment count sits in the header.</summary>
  private const int _SEGMENT_COUNT_AT = 26;

  /// <summary>This page holds the tail of a packet that began on an earlier one.</summary>
  internal const int FLAG_CONTINUED = 0x01;

  /// <summary>This page is the first of its logical bitstream.</summary>
  internal const int FLAG_BEGIN_OF_STREAM = 0x02;

  /// <summary>This page is the last of its logical bitstream.</summary>
  internal const int FLAG_END_OF_STREAM = 0x04;

  /// <summary>Where this page begins in the file.</summary>
  internal required int Offset { get; init; }

  /// <summary>How many bytes the whole page occupies: header, segment table and body.</summary>
  internal required int Length { get; init; }

  /// <summary>The header type flags — continued, first, last.</summary>
  internal required byte Flags { get; init; }

  /// <summary>
  /// The position in the logical bitstream reached once every packet that ends on this page has been
  /// consumed, in whatever unit the codec's Ogg mapping counts in, or -1 for a page that ends no
  /// packet at all.
  /// </summary>
  /// <remarks>
  /// Not a timestamp. The field is 64 bits and the format says nothing whatever about what is in
  /// them: Vorbis counts samples, Theora packs a keyframe number and an offset into two bit fields,
  /// and a mapping added tomorrow may count something else again. Turning one into a moment is
  /// <see cref="OggCodecMapping"/>'s business and is done differently for each.
  /// </remarks>
  internal required long GranulePosition { get; init; }

  /// <summary>Which logical bitstream this page belongs to.</summary>
  internal required uint SerialNumber { get; init; }

  /// <summary>This page's position among the pages of its own bitstream, counted from zero.</summary>
  internal required uint SequenceNumber { get; init; }

  /// <summary>The checksum the writer stored in the page.</summary>
  internal required uint Checksum { get; init; }

  /// <summary>The segment table: one lacing value per segment, in order.</summary>
  internal required ReadOnlyMemory<byte> Lacing { get; init; }

  /// <summary>The page's payload, which the lacing divides into packets and packet fragments.</summary>
  internal required ReadOnlyMemory<byte> Body { get; init; }

  internal bool IsContinued => (this.Flags & FLAG_CONTINUED) != 0;
  internal bool IsBeginOfStream => (this.Flags & FLAG_BEGIN_OF_STREAM) != 0;
  internal bool IsEndOfStream => (this.Flags & FLAG_END_OF_STREAM) != 0;

  /// <summary>
  /// Reads the page beginning at an offset, or says why the bytes there are not one.
  /// </summary>
  /// <param name="file">The whole file.</param>
  /// <param name="offset">Where the capture pattern is expected.</param>
  /// <param name="page">The page, when one was read.</param>
  /// <returns><c>false</c> when there are not enough bytes left for a whole page.</returns>
  /// <exception cref="InvalidDataException">The bytes are there but are not a page.</exception>
  internal static bool TryRead(ReadOnlyMemory<byte> file, int offset, out OggPage page) {
    page = default;

    var data = file.Span;
    if (offset + HEADER_SIZE > data.Length)
      return false;

    var header = data.Slice(offset, HEADER_SIZE);
    if (!header[..4].SequenceEqual(CapturePattern))
      throw new InvalidDataException(
        $"The bytes at offset {offset} are not an Ogg page: a page begins with 'OggS' and these begin with {_Describe(header[..4])}.");

    // Refused by name rather than read hopefully. A version other than zero means a page laid out to
    // rules this reader does not have, and reading it to these rules would produce packet boundaries
    // that are somebody else's bytes at plausible-looking lengths.
    var version = header[4];
    if (version != VERSION)
      throw new NotSupportedException(
        $"The page at offset {offset} states stream structure version {version}, where RFC 3533 defines version {VERSION} and this reader takes no other.");

    var segments = header[_SEGMENT_COUNT_AT];
    var lacingAt = offset + HEADER_SIZE;
    if (lacingAt + segments > data.Length)
      return false;

    var lacing = data.Slice(lacingAt, segments);
    var bodyLength = 0;
    foreach (var value in lacing)
      bodyLength += value;

    var bodyAt = lacingAt + segments;
    if (bodyAt + bodyLength > data.Length)
      return false;

    page = new() {
      Offset = offset,
      Length = HEADER_SIZE + segments + bodyLength,
      Flags = header[5],
      GranulePosition = BinaryPrimitives.ReadInt64LittleEndian(header[6..14]),
      SerialNumber = BinaryPrimitives.ReadUInt32LittleEndian(header[14..18]),
      SequenceNumber = BinaryPrimitives.ReadUInt32LittleEndian(header[18..22]),
      Checksum = BinaryPrimitives.ReadUInt32LittleEndian(header[22..26]),
      Lacing = file.Slice(lacingAt, segments),
      Body = file.Slice(bodyAt, bodyLength),
    };

    return true;
  }

  /// <summary>Checks the page against the checksum it carries, and refuses it by name if they differ.</summary>
  /// <remarks>
  /// Verified rather than trusted, because the failure a corrupt page causes without this check is
  /// the one that cannot be traced: a damaged lacing value moves every packet boundary after it, and
  /// what comes out is packets of plausible lengths holding the wrong bytes. A player may guess its
  /// way past that; a library handing packets to a caller has nothing to guess with.
  /// </remarks>
  internal void Verify(ReadOnlyMemory<byte> file) {
    var computed = OggCrc.Compute(file.Span.Slice(this.Offset, this.Length));
    if (computed == this.Checksum)
      return;

    throw new InvalidDataException(
      $"The page at offset {this.Offset} — bitstream 0x{this.SerialNumber:X8}, sequence {this.SequenceNumber} — "
      + $"carries checksum 0x{this.Checksum:X8} where its {this.Length} bytes sum to 0x{computed:X8}.");
  }

  private static string _Describe(ReadOnlySpan<byte> bytes) {
    Span<char> text = stackalloc char[bytes.Length];
    for (var i = 0; i < bytes.Length; ++i)
      text[i] = bytes[i] is >= 0x20 and <= 0x7E ? (char)bytes[i] : '.';

    return $"'{new string(text)}'";
  }
}
