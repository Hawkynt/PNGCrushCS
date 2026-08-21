using System;
using System.Buffers.Binary;
using System.Collections.Generic;

namespace FileFormat.RealMedia;

/// <summary>One chunk of a RealMedia file: its four-character name, where it is and how long it is.</summary>
/// <param name="Name">The four bytes naming the chunk, as one big-endian number.</param>
/// <param name="Offset">Where the chunk begins, counted from the file's start.</param>
/// <param name="Length">The chunk's whole length including its ten-byte prefix, as it states it.</param>
/// <param name="Version">The chunk's object version, which says how its body is laid out.</param>
internal readonly record struct RealMediaChunk(uint Name, int Offset, int Length, int Version) {

  /// <summary>Where the chunk's body begins: past the name, the length and the version.</summary>
  internal int BodyOffset => this.Offset + RealMediaChunkScanner.PREFIX;

  /// <summary>How many bytes of body the chunk states it has.</summary>
  internal int BodyLength => this.Length - RealMediaChunkScanner.PREFIX;
}

/// <summary>
/// Walks the chunks a RealMedia file is built from.
/// </summary>
/// <remarks>
/// The whole file is a flat run of chunks, each opening with four characters naming it, a length
/// counting itself, and a two-byte object version. Because every chunk states its own length, a
/// chunk this reader has never heard of costs nothing to step over — which is why a file carrying
/// digital rights management, a logical-stream description or any of the chunks RealNetworks added
/// over the format's life is walked exactly as fast as one carrying none of them.
/// <para/>
/// The object version is read but not acted on here. It is the chunk's business what its versions
/// mean, and the ones this reader knows about check their own; a chunk whose version is one it does
/// not know is left alone rather than guessed at.
/// </remarks>
internal static class RealMediaChunkScanner {

  /// <summary>The bytes every chunk spends before its body: four naming it, four of length, two of version.</summary>
  internal const int PREFIX = 10;

  /// <summary>The file header, which is also the format's signature.</summary>
  internal const uint FILE_HEADER = 0x2E524D46; // ".RMF"

  /// <summary>File properties: the rates, the packet count, the duration and where the data begins.</summary>
  internal const uint PROPERTIES = 0x50524F50; // "PROP"

  /// <summary>Media properties, one per stream, carrying that stream's codec-specific description.</summary>
  internal const uint MEDIA_PROPERTIES = 0x4D445052; // "MDPR"

  /// <summary>Content description: title, author, copyright and comment.</summary>
  internal const uint CONTENT = 0x434F4E54; // "CONT"

  /// <summary>The chunk holding the packets.</summary>
  internal const uint DATA = 0x44415441; // "DATA"

  /// <summary>An index over one stream, letting a player seek without walking the packets.</summary>
  internal const uint INDEX = 0x494E4458; // "INDX"

  /// <summary>Reads the four-character name at an offset.</summary>
  internal static uint NameAt(ReadOnlySpan<byte> data, int offset)
    => BinaryPrimitives.ReadUInt32BigEndian(data[offset..]);

  /// <summary>Renders a chunk's name the way it reads in the file, for a message.</summary>
  internal static string NameOf(uint name) {
    Span<char> letters = stackalloc char[4];
    for (var i = 0; i < 4; ++i) {
      var value = (byte)(name >> ((3 - i) * 8));
      if (value is < 0x20 or > 0x7E)
        return $"0x{name:X8}";

      letters[i] = (char)value;
    }

    return new(letters);
  }

  /// <summary>
  /// Walks the file's chunks from the front, stopping where the chain stops making sense.
  /// </summary>
  /// <remarks>
  /// Stopping rather than throwing, because a RealMedia file that was cut short mid-recording is
  /// ordinary rather than exceptional — the format was built for streaming, and much of what is on
  /// the sample servers is a capture that ended when somebody closed the player. What was written
  /// before the cut is perfectly good and is read; what comes after the last complete chunk is not
  /// guessed at.
  /// <para/>
  /// A chunk whose stated length cannot be true is reported as running to the end of the file, and
  /// the walk stops after it. That is not a guess but the only reading left: a writer fills in the
  /// data chunk's length when it closes the file, so a recording that was never closed carries either
  /// a zero there or the length it was going to grow to. Both mean the same thing — everything from
  /// here on is this chunk — and a reader that refused them would refuse the packets that were
  /// written, which are the whole of what such a file has.
  /// </remarks>
  internal static IEnumerable<RealMediaChunk> Walk(ReadOnlyMemory<byte> file) {
    var data = file;
    var offset = 0;

    while (offset + PREFIX <= data.Length) {
      var span = data.Span;
      var name = NameAt(span, offset);
      var stated = (int)BinaryPrimitives.ReadUInt32BigEndian(span[(offset + 4)..]);
      var version = BinaryPrimitives.ReadUInt16BigEndian(span[(offset + 8)..]);

      var usable = stated >= PREFIX && stated <= data.Length - offset;
      var length = usable ? stated : data.Length - offset;

      yield return new(name, offset, length, version);

      if (!usable)
        yield break;

      offset += length;
    }
  }
}
