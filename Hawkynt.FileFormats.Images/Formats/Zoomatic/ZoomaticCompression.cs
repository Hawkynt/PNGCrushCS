using System;
using System.IO;

namespace FileFormat.Zoomatic;

/// <summary>
/// The run-length container a Zoomatic picture is stored in, which runs from the end of the file
/// towards its front.
/// </summary>
/// <remarks>
/// The depacker this format ships with is the ordinary C64 trick of unpacking a file over itself:
/// it starts at the last byte and walks down, writing the screen out from its last byte downwards,
/// so the write pointer never overtakes the read pointer and no second buffer is needed. That is
/// why the stream is stored backwards, and it is the whole of what this class does — the coding
/// inside it is the plain escape-count-value scheme.
/// <para/>
/// The escape is not fixed. It is the last byte of the file, chosen by the encoder as a value the
/// picture makes little use of, and a byte equal to it is always written as a run so that it can
/// never be mistaken for the marker.
/// <para/>
/// Reading the picture as a flat dump of its ten thousand bytes, which is what this did before, is
/// not a lenient reading of the format: it is a different format. A file written that way is
/// nothing any Zoomatic depacker can follow, and <c>recoil2png</c> — the one implementation of this
/// outside the machine itself — rebuilt a picture 129 levels a channel away from what had been
/// asked for.
/// </remarks>
internal static class ZoomaticCompression {

  /// <summary>A count byte of zero asks for a full run.</summary>
  private const int _MAX_RUN = 256;

  /// <summary>Bytes at the front of the file the depacker never reads: the load address.</summary>
  private const int _RESERVED_PREFIX = 2;

  /// <summary>Expands a packed file into the screen it holds.</summary>
  /// <param name="data">The whole file, load address and trailing escape byte included.</param>
  /// <param name="unpackedSize">How many bytes the screen takes.</param>
  public static byte[] Unpack(ReadOnlySpan<byte> data, int unpackedSize) {
    if (data.Length < _RESERVED_PREFIX + 2)
      throw new InvalidDataException($"File too small for Zoomatic format (got {data.Length} bytes).");

    var escape = data[^1];
    var readAt = data.Length - 1;
    var unpacked = new byte[unpackedSize];
    var writeAt = unpackedSize - 1;

    while (writeAt >= 0) {
      var value = _ReadValue(data, ref readAt);
      var count = 1;
      if (value == escape) {
        count = _ReadValue(data, ref readAt);
        if (count == 0)
          count = _MAX_RUN;
        value = _ReadValue(data, ref readAt);
      }

      while (count-- > 0 && writeAt >= 0)
        unpacked[writeAt--] = (byte)value;
    }

    return unpacked;
  }

  /// <summary>The next byte the depacker meets, which is the one before the last it took.</summary>
  private static byte _ReadValue(ReadOnlySpan<byte> data, ref int readAt) {
    if (readAt <= _RESERVED_PREFIX)
      throw new InvalidDataException("Truncated Zoomatic stream: the packed data ends before the picture is complete.");

    return data[--readAt];
  }

  /// <summary>Packs a screen into the file body the depacker expects, escape byte included.</summary>
  /// <param name="unpacked">The screen, in the order the picture reads.</param>
  /// <param name="loadAddress">The address the file states it loads at, which nothing decodes.</param>
  public static byte[] Pack(ReadOnlySpan<byte> unpacked, ushort loadAddress) {
    var escape = _LeastUsedValue(unpacked);

    // The stream as the depacker meets it, which is the screen read from its last byte to its first.
    using var stream = new MemoryStream();
    for (var at = unpacked.Length - 1; at >= 0;) {
      var value = unpacked[at];
      var run = 1;
      while (run < _MAX_RUN && at - run >= 0 && unpacked[at - run] == value)
        ++run;

      // Three bytes either way at a run of three, so only a longer one pays; a byte equal to the
      // escape has no literal form at all and is always written as a run.
      if (run > 3 || value == escape) {
        stream.WriteByte(escape);
        stream.WriteByte((byte)(run & 0xFF));
        stream.WriteByte(value);
      } else {
        for (var i = 0; i < run; ++i)
          stream.WriteByte(value);
      }

      at -= run;
    }

    var body = stream.ToArray();
    var result = new byte[_RESERVED_PREFIX + body.Length + 1];
    result[0] = (byte)(loadAddress & 0xFF);
    result[1] = (byte)(loadAddress >> 8);

    // Laid into the file back to front, so that reading the file backwards yields the stream above.
    for (var i = 0; i < body.Length; ++i)
      result[_RESERVED_PREFIX + i] = body[body.Length - 1 - i];

    result[^1] = escape;
    return result;
  }

  /// <summary>The byte value the picture leans on least, which is the cheapest marker to spend.</summary>
  private static byte _LeastUsedValue(ReadOnlySpan<byte> data) {
    Span<int> frequency = stackalloc int[256];
    foreach (var value in data)
      ++frequency[value];

    var best = 0;
    for (var value = 1; value < 256; ++value)
      if (frequency[value] < frequency[best])
        best = value;

    return (byte)best;
  }
}
