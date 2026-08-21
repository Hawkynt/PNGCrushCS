using System;
using System.Collections.Generic;

namespace FileFormat.Ogg;

/// <summary>One packet put back together, and where it sat.</summary>
/// <param name="Data">The packet's bytes — a window onto the file where the packet lay whole on one
/// page, and a joined copy only where it did not.</param>
/// <param name="Index">Its position among the packets of its own bitstream, counted from zero across
/// header packets and data packets alike.</param>
internal readonly record struct OggAssembledPacket(ReadOnlyMemory<byte> Data, int Index);

/// <summary>
/// Puts the packets of one logical bitstream back together out of the fragments its pages hold.
/// </summary>
/// <remarks>
/// The reassembly is the part of Ogg that cannot be skipped. A page is a container-level convenience
/// sized for the medium rather than for the data, and a packet larger than one — any Theora keyframe
/// of a large picture is — is written as the tail of one page and the head of the next, with the
/// second page's continuation flag set. A reader that treated a page's body as a packet would hand a
/// decoder the first 65 025 bytes of a frame and call it a frame.
/// <para/>
/// The boundaries are in the segment table and are a counting scheme rather than lengths: a packet is
/// as many 255-byte segments as it needs plus one shorter one, and the shorter one is what ends it.
/// A packet whose length is an exact multiple of 255 therefore ends with a zero-length segment, which
/// is why the terminating segment is tested for being under 255 rather than for being non-empty.
/// <para/>
/// One of these per logical bitstream, because the fragment carried across a page boundary belongs to
/// one bitstream and the pages of the others sit between the two halves of it.
/// </remarks>
internal sealed class OggPacketAssembler {

  /// <summary>The fragments of a packet begun on an earlier page and not yet finished.</summary>
  private readonly List<ReadOnlyMemory<byte>> _pending = [];

  private int _pendingLength;

  /// <summary>How many packets of this bitstream have been completed so far.</summary>
  private int _count;

  /// <summary>
  /// Splits one page into the packets that finish on it, appending them to a caller's list.
  /// </summary>
  /// <returns>How many packets were appended.</returns>
  /// <remarks>
  /// Appends to a list the caller owns and reuses, rather than returning a fresh one. A page of one
  /// packet is the ordinary case for video and would otherwise cost a list allocation per frame for
  /// the length of the film.
  /// </remarks>
  internal int Split(OggPage page, List<OggAssembledPacket> into) {
    var before = into.Count;
    var lacing = page.Lacing.Span;

    // A continuation flag with nothing pending means the file was opened part way through this
    // packet — the head of it is in a page that is not here. The fragment is dropped rather than
    // handed on: a decoder given the back half of a frame produces noise, and noise is worse than a
    // frame that is missing.
    var joining = this._pending.Count > 0;
    var dropLeadingFragment = page.IsContinued && !joining;

    // The mirror case: a page that does not claim to continue anything while a fragment is pending
    // means the page carrying the rest of it is missing. The fragment can never be completed, so it
    // goes rather than being silently welded to the next packet.
    if (!page.IsContinued && joining) {
      this._pending.Clear();
      this._pendingLength = 0;
      joining = false;
    }

    var offset = 0;
    var segmentStart = 0;
    var first = true;

    for (var i = 0; i < lacing.Length; ++i) {
      offset += lacing[i];
      if (lacing[i] == OggPage.CONTINUATION_LACING)
        continue;

      // A lacing value under 255 ends a packet, zero included: a packet whose length divides by 255
      // is terminated by a zero-length segment and would otherwise run into the next one.
      var fragment = page.Body[segmentStart..offset];
      segmentStart = offset;

      if (first && dropLeadingFragment) {
        first = false;
        continue;
      }

      first = false;

      if (!joining) {
        into.Add(new(fragment, this._count++));
        continue;
      }

      into.Add(new(this._Join(fragment), this._count++));
      joining = false;
    }

    // Whatever is left after the last terminating segment is the head of a packet that continues on
    // the next page of this bitstream.
    if (segmentStart >= page.Body.Length)
      return into.Count - before;

    var tail = page.Body[segmentStart..];
    if (!(first && dropLeadingFragment)) {
      this._pending.Add(tail);
      this._pendingLength += tail.Length;
    }

    return into.Count - before;
  }

  /// <summary>Welds the fragments carried across page boundaries onto the last of them.</summary>
  /// <remarks>
  /// The one place this reader copies a packet's bytes. It has no choice: the halves of a spanning
  /// packet are separated in the file by the next page's twenty-seven byte header and its segment
  /// table, so there is no window onto the file that holds the packet and nothing else.
  /// </remarks>
  private ReadOnlyMemory<byte> _Join(ReadOnlyMemory<byte> tail) {
    var joined = new byte[this._pendingLength + tail.Length];
    var at = 0;
    foreach (var fragment in this._pending) {
      fragment.CopyTo(joined.AsMemory(at));
      at += fragment.Length;
    }

    tail.CopyTo(joined.AsMemory(at));

    this._pending.Clear();
    this._pendingLength = 0;
    return joined;
  }
}
