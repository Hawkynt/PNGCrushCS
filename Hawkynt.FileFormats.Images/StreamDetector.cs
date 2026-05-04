using System;
using System.IO;

namespace Hawkynt.FileFormats.Images;

/// <summary>
/// Stream-based format detection. Works with both seekable and non-seekable streams.
/// </summary>
internal static class StreamDetector {

  /// <summary>Reads up to <paramref name="peekBytes"/> from the stream, runs signature detection,
  /// then restores the stream position (for seekable streams) so the caller can re-read the data.
  /// For non-seekable streams the consumed bytes are lost; callers wanting to preserve the data
  /// should use <see cref="DetectAndRewind"/>.</summary>
  public static ImageFormat Detect(Stream stream, int peekBytes) {
    if (stream == null) throw new ArgumentNullException(nameof(stream));
    if (!stream.CanRead) throw new ArgumentException("Stream must be readable.", nameof(stream));
    if (peekBytes <= 0) return ImageFormat.Unknown;

    var buffer = new byte[peekBytes];
    var read = _ReadFully(stream, buffer, 0, peekBytes);
    if (read == 0) return ImageFormat.Unknown;
    var format = FormatRegistry.DetectFromBytes(buffer.AsSpan(0, read));
    if (stream.CanSeek) stream.Seek(-read, SeekOrigin.Current);
    return format;
  }

  /// <summary>Detects the format AND returns a stream positioned at the start of the original data.
  /// For seekable streams the same stream is returned (rewound). For non-seekable streams a new
  /// <see cref="MemoryStream"/>-backed wrapper is returned that re-emits the consumed prefix
  /// followed by the rest of the source stream.</summary>
  public static (ImageFormat Format, Stream RewoundStream) DetectAndRewind(Stream stream, int peekBytes) {
    if (stream == null) throw new ArgumentNullException(nameof(stream));
    if (!stream.CanRead) throw new ArgumentException("Stream must be readable.", nameof(stream));

    var buffer = new byte[peekBytes];
    var read = _ReadFully(stream, buffer, 0, peekBytes);
    var format = read > 0
      ? FormatRegistry.DetectFromBytes(buffer.AsSpan(0, read))
      : ImageFormat.Unknown;

    if (stream.CanSeek) {
      stream.Seek(-read, SeekOrigin.Current);
      return (format, stream);
    }

    // Non-seekable: stitch the peeked prefix in front of the remaining stream.
    return (format, new ConcatStream(buffer, read, stream));
  }

  /// <summary>Reads up to <paramref name="count"/> bytes, looping past short reads (which streams
  /// are allowed to do per <see cref="Stream.Read(byte[], int, int)"/>'s contract).</summary>
  private static int _ReadFully(Stream stream, byte[] buffer, int offset, int count) {
    var totalRead = 0;
    while (totalRead < count) {
      var n = stream.Read(buffer, offset + totalRead, count - totalRead);
      if (n <= 0) break;
      totalRead += n;
    }
    return totalRead;
  }

  /// <summary>Stream that re-emits a buffered prefix followed by the rest of an inner stream.</summary>
  private sealed class ConcatStream : Stream {
    private readonly byte[] _prefix;
    private readonly int _prefixLength;
    private readonly Stream _inner;
    private int _prefixPos;

    public ConcatStream(byte[] prefix, int prefixLength, Stream inner) {
      this._prefix = prefix;
      this._prefixLength = prefixLength;
      this._inner = inner;
      this._prefixPos = 0;
    }

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position {
      get => throw new NotSupportedException();
      set => throw new NotSupportedException();
    }

    public override int Read(byte[] buffer, int offset, int count) {
      if (this._prefixPos < this._prefixLength) {
        var fromPrefix = Math.Min(count, this._prefixLength - this._prefixPos);
        Buffer.BlockCopy(this._prefix, this._prefixPos, buffer, offset, fromPrefix);
        this._prefixPos += fromPrefix;
        return fromPrefix;
      }
      return this._inner.Read(buffer, offset, count);
    }

    public override void Flush() => this._inner.Flush();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    protected override void Dispose(bool disposing) {
      if (disposing) this._inner.Dispose();
      base.Dispose(disposing);
    }
  }
}
