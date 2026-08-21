using System;
using System.Collections.Generic;

namespace FileFormat.Ogg;

/// <summary>Walks an Ogg file's pages, in the order they are stored.</summary>
/// <remarks>
/// Storage order and not per-bitstream order, because storage order is the interleaving: a file with
/// sound holds a run of video pages, then a run of audio pages, then more video, arranged so that a
/// player reading forwards has both by the time it needs them. Separating the bitstreams is the
/// caller's job and is done on the serial number.
/// <para/>
/// Pages are expected to abut. RFC 3533 allows a decoder to resynchronise by hunting for the next
/// capture pattern, and this reader deliberately does not: hunting turns a file with a damaged page
/// into a file with a silently missing one, and the four bytes <c>OggS</c> occur inside compressed
/// video often enough that a hunt lands inside a packet and reports its contents as pages. The file
/// is walked from its first byte and a page that does not begin where the last one ended is refused
/// by name.
/// </remarks>
internal static class OggPageScanner {

  /// <summary>
  /// Walks every page of the file from the beginning.
  /// </summary>
  /// <remarks>
  /// A trailing run of bytes too short to be a page ends the walk rather than failing it. That is
  /// what a recording cut off mid-write looks like, and the pages before the cut are perfectly good
  /// — the incomplete one is simply not there to report.
  /// <para/>
  /// Checksums are not verified here, because verifying is the one part of reading a page that costs
  /// its whole body: a page's structure is read out of its first few dozen bytes and its body is
  /// stepped over by arithmetic, so an unverified walk of a two-hour recording touches a thousandth
  /// of it. The caller verifies the pages it actually takes packets out of, which is
  /// <see cref="OggPage.Verify"/>.
  /// </remarks>
  internal static IEnumerable<OggPage> Walk(ReadOnlyMemory<byte> file) {
    var offset = 0;
    while (OggPage.TryRead(file, offset, out var page)) {
      yield return page;
      offset += page.Length;
    }
  }
}
