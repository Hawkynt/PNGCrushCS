using System;
using System.IO;

// Every reader offers a FromStream beside its FromSpan, and each one used to carry its own copy of
// the same seven lines: take the remaining length when the stream can seek, buffer the whole thing
// when it cannot, then hand the bytes to the real parse. 875 copies in four accidental spellings —
// the local was called data or buffer or remaining, the MemoryStream ms or memory or buffer — which
// is four chances to correct the wrong one and no way to tell from a diff that the others exist.
// The reading lives here now and the readers delegate, for the same reason the by-bytes entry
// points forward to the by-span ones rather than parsing a second time.

namespace FileFormat;

/// <summary>Reads what is left of a stream into a byte array.</summary>
internal static class StreamBytes {

  /// <summary>Reads from the current position to the end of the stream.</summary>
  /// <param name="stream">The stream to drain; left positioned at its end.</param>
  /// <returns>The bytes that remained.</returns>
  /// <remarks>
  /// A seekable stream states how much is left, so the array is allocated once at exactly that
  /// size. A stream that cannot seek has to be copied until it ends, because nothing else can say
  /// how long it is.
  /// </remarks>
  public static byte[] ReadAll(Stream stream) {
    ArgumentNullException.ThrowIfNull(stream);

    if (!stream.CanSeek) {
      using var buffer = new MemoryStream();
      stream.CopyTo(buffer);
      return buffer.ToArray();
    }

    var data = new byte[checked((int)(stream.Length - stream.Position))];
    stream.ReadExactly(data);
    return data;
  }
}
