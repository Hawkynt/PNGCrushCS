using System;
using System.IO;
using System.IO.Compression;

namespace FileFormat.Codecs.Zmbv;

/// <summary>
/// One zlib decompressor held open across packets, so the sliding window an interframe's blocks are
/// copied through reaches back into pictures this same instance already produced.
/// </summary>
/// <remarks>
/// This is the trap the format sets for a decoder built one packet at a time. ZMBV's own words for it
/// are "you must reset zlib for intraframes" — said once, about intraframes only, which means every
/// other frame is read by the same inflater without resetting it. A decoder that opened a fresh zlib
/// stream for each packet would decode the first frame, since a lone intraframe carries a complete
/// zlib stream of its own, and then diverge on the first interframe, whose compressed bytes are not a
/// stream at all on their own — they are a continuation, meaningful only against the dictionary the
/// frames before it built.
/// <para/>
/// So there is exactly one <see cref="ZLibStream"/> for as long as intraframes keep it, and what
/// changes between packets is only which bytes <see cref="_Source"/> hands out next. <see cref="Reset"/>
/// throws the old one away and starts another, which is what an intraframe's own fresh zlib header
/// demands; <see cref="Continue"/> keeps it and simply points <see cref="_Source"/> at the next
/// packet's bytes, which is what lets an interframe's compressed data mean anything at all.
/// </remarks>
internal sealed class ZmbvInflater : IDisposable {

  /// <summary>
  /// A stream that hands out exactly the bytes of whichever packet it was last pointed at, and
  /// nothing beyond them — never bytes belonging to a packet not yet handed over.
  /// </summary>
  /// <remarks>
  /// That boundary is what keeps <see cref="ZLibStream"/> honest across the call. Its own internal
  /// buffering could otherwise read ahead into whatever this stream had left to give, and there is
  /// nothing left to give beyond the packet currently in hand — the next one has not arrived yet.
  /// Reaching the end of the current packet's bytes returns zero, precisely as an ordinary stream
  /// signals it has nothing more <i>right now</i>; the format's own sync-flush framing is what
  /// guarantees a decompression never needs to ask for more than one packet holds to finish producing
  /// that packet's frame.
  /// </remarks>
  private sealed class _PacketSource : Stream {
    private ReadOnlyMemory<byte> _buffer;
    private int _position;

    public void SetBuffer(ReadOnlyMemory<byte> buffer) {
      this._buffer = buffer;
      this._position = 0;
    }

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
    public override void Flush() { }
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    public override int Read(byte[] buffer, int offset, int count) => this.Read(buffer.AsSpan(offset, count));

    public override int Read(Span<byte> buffer) {
      var remaining = this._buffer.Length - this._position;
      if (remaining <= 0)
        return 0;

      var take = Math.Min(buffer.Length, remaining);
      this._buffer.Span.Slice(this._position, take).CopyTo(buffer);
      this._position += take;
      return take;
    }
  }

  private readonly _PacketSource _source = new();
  private ZLibStream? _zlib;

  /// <summary>Starts a fresh zlib stream over a packet's compressed bytes, as an intraframe demands.</summary>
  public void Reset(ReadOnlyMemory<byte> compressed) {
    this._zlib?.Dispose();
    this._source.SetBuffer(compressed);
    this._zlib = new(this._source, CompressionMode.Decompress, leaveOpen: true);
  }

  /// <summary>
  /// Points the same zlib stream this instance has held since the last <see cref="Reset"/> at the
  /// next packet's compressed bytes, carrying its dictionary across the boundary between them.
  /// </summary>
  public void Continue(ReadOnlyMemory<byte> compressed) {
    if (this._zlib == null)
      throw new InvalidOperationException(
        "A ZMBV interframe was offered before any intraframe opened a zlib stream for it to continue.");

    this._source.SetBuffer(compressed);
  }

  /// <summary>
  /// Fills <paramref name="destination"/> from the current zlib stream, refusing rather than padding
  /// when the packet in hand runs out first.
  /// </summary>
  public void ReadExactly(Span<byte> destination) {
    if (this._zlib == null)
      throw new InvalidOperationException("No zlib stream is open — call Reset before the first read.");

    try {
      this._zlib.ReadExactly(destination);
    } catch (EndOfStreamException ex) {
      throw new InvalidDataException(
        $"A ZMBV packet's compressed data ran out {destination.Length} byte(s) short of what its frame needs.", ex);
    }
  }

  public void Dispose() {
    this._zlib?.Dispose();
    this._source.Dispose();
  }
}
