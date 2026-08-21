using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Text;

namespace FileFormat.Ogg.Tests;

/// <summary>One page to be written into a built file.</summary>
/// <remarks>
/// A page rather than a packet, because everything worth testing about Ogg is where the page
/// boundaries fall relative to the packet ones. ffmpeg's muxer will not put a keyframe and the frames
/// after it on one page, will not write a page whose lacing ends in a zero, and will not leave a
/// continuation flag dangling, so each of those was built here by hand.
/// </remarks>
internal sealed class OggTestPage {

  /// <summary>Which logical bitstream the page belongs to.</summary>
  public uint Serial { get; init; } = 1;

  /// <summary>The page's position among the pages of its own bitstream.</summary>
  public uint Sequence { get; init; }

  /// <summary>The position reached once every packet finishing on this page has been consumed.</summary>
  public long Granule { get; init; } = -1;

  public bool BeginOfStream { get; init; }

  public bool EndOfStream { get; init; }

  /// <summary>Says the first segment continues a packet begun on an earlier page.</summary>
  public bool Continued { get; init; }

  /// <summary>The packets that finish on this page, in order.</summary>
  public IReadOnlyList<byte[]> Packets { get; init; } = [];

  /// <summary>
  /// A packet fragment written after the finished ones, with no terminating segment.
  /// </summary>
  /// <remarks>
  /// The head of a packet that continues on the next page. Its length is written as a run of 255s
  /// with no shorter value after it, which is the only thing in the format that says so.
  /// </remarks>
  public byte[]? Tail { get; init; }

  /// <summary>Writes a version byte other than the zero RFC 3533 defines.</summary>
  public byte Version { get; init; }

  /// <summary>Leaves the stored checksum wrong on purpose.</summary>
  public bool BreakChecksum { get; init; }
}

/// <summary>
/// Builds Ogg files a byte at a time, so that a test can put a packet exactly where it wants it.
/// </summary>
/// <remarks>
/// Every layout built here was checked against a file ffmpeg wrote before being written as a test,
/// and the timings asserted against <c>ffprobe -fflags +noparse</c> on those files — the flag matters,
/// because plain ffprobe runs the codec's own parser over an elementary stream and re-splits it into
/// access units, so its packet list stops being the container's.
/// </remarks>
internal static class OggTestContainer {

  /// <summary>The Theora keyframe granule shift ffmpeg's encoder writes, and this builder's default.</summary>
  internal const int THEORA_GRANULE_SHIFT = 6;

  /// <summary>Assembles a file out of pages, in the order given.</summary>
  internal static byte[] Build(params OggTestPage[] pages) {
    var file = new List<byte>();
    foreach (var page in pages)
      file.AddRange(_Page(page));

    return file.ToArray();
  }

  private static byte[] _Page(OggTestPage page) {
    var lacing = new List<byte>();
    var body = new List<byte>();

    foreach (var packet in page.Packets) {
      var remaining = packet.Length;
      while (remaining >= 255) {
        lacing.Add(255);
        remaining -= 255;
      }

      // The terminating segment, which is what ends a packet — zero included, for a packet whose
      // length divides by 255 exactly.
      lacing.Add((byte)remaining);
      body.AddRange(packet);
    }

    if (page.Tail != null) {
      // No terminating segment: the packet runs on into the next page.
      var remaining = page.Tail.Length;
      while (remaining >= 255) {
        lacing.Add(255);
        remaining -= 255;
      }

      if (remaining > 0)
        throw new ArgumentException("A page's trailing fragment has to be a whole number of 255-byte segments, or the packet would end on it.");

      body.AddRange(page.Tail);
    }

    var flags = 0;
    if (page.Continued)
      flags |= 0x01;
    if (page.BeginOfStream)
      flags |= 0x02;
    if (page.EndOfStream)
      flags |= 0x04;

    var bytes = new byte[27 + lacing.Count + body.Count];
    Encoding.ASCII.GetBytes("OggS").CopyTo(bytes, 0);
    bytes[4] = page.Version;
    bytes[5] = (byte)flags;
    BinaryPrimitives.WriteInt64LittleEndian(bytes.AsSpan(6, 8), page.Granule);
    BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(14, 4), page.Serial);
    BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(18, 4), page.Sequence);
    bytes[26] = (byte)lacing.Count;
    lacing.CopyTo(bytes, 27);
    body.CopyTo(bytes, 27 + lacing.Count);

    var checksum = _Checksum(bytes);
    BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(22, 4), page.BreakChecksum ? checksum ^ 0xFFFFFFFF : checksum);
    return bytes;
  }

  /// <summary>
  /// The Ogg CRC-32, written out again here rather than called through.
  /// </summary>
  /// <remarks>
  /// Deliberately a second implementation. A test that summed its pages with the reader's own routine
  /// would agree with the reader whatever either of them computed, and the value that matters is
  /// libogg's — so this one is written straight from RFC 3533 section 6 and the pages it produces were
  /// compared byte for byte against pages ffmpeg wrote.
  /// </remarks>
  private static uint _Checksum(ReadOnlySpan<byte> page) {
    var register = 0u;
    for (var i = 0; i < page.Length; ++i) {
      // The stored field reads as zero while the sum is taken over it.
      var value = i is >= 22 and < 26 ? (byte)0 : page[i];
      for (var bit = 0; bit < 8; ++bit) {
        var top = (register & 0x80000000) != 0;
        register = (register << 1) | (uint)((value >> (7 - bit)) & 1);
        if (top)
          register ^= 0x04C11DB7;
      }
    }

    for (var i = 0; i < 32; ++i) {
      var top = (register & 0x80000000) != 0;
      register <<= 1;
      if (top)
        register ^= 0x04C11DB7;
    }

    return register;
  }

  // ============================================================================================
  // Mapping headers
  // ============================================================================================

  /// <summary>
  /// A Theora identification header — Theora specification section 6.2, forty-two bytes.
  /// </summary>
  /// <param name="width">The picture width, which is not the frame width for a picture that is not a
  /// whole number of macroblocks across.</param>
  internal static byte[] TheoraIdentification(
    int width = 176, int height = 144, int rateNumerator = 25, int rateDenominator = 1,
    int granuleShift = THEORA_GRANULE_SHIFT, int pixelFormat = 0,
    byte major = 3, byte minor = 2, byte revision = 1) {
    var header = new byte[42];
    header[0] = 0x80;
    Encoding.ASCII.GetBytes("theora").CopyTo(header, 1);
    header[7] = major;
    header[8] = minor;
    header[9] = revision;

    // The frame size in macroblocks, rounded up from the picture; the reader reports the picture.
    BinaryPrimitives.WriteUInt16BigEndian(header.AsSpan(10, 2), (ushort)((width + 15) / 16));
    BinaryPrimitives.WriteUInt16BigEndian(header.AsSpan(12, 2), (ushort)((height + 15) / 16));
    _WriteUInt24BigEndian(header.AsSpan(14, 3), (uint)width);
    _WriteUInt24BigEndian(header.AsSpan(17, 3), (uint)height);
    BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(22, 4), (uint)rateNumerator);
    BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(26, 4), (uint)rateDenominator);
    _WriteUInt24BigEndian(header.AsSpan(30, 3), 1);
    _WriteUInt24BigEndian(header.AsSpan(33, 3), 1);

    // Six bits of quality hint, five of keyframe granule shift, two of pixel format, three reserved.
    var packed = (ushort)((granuleShift << 5) | (pixelFormat << 3));
    BinaryPrimitives.WriteUInt16BigEndian(header.AsSpan(40, 2), packed);
    return header;
  }

  /// <summary>A Theora comment header: the magic, a vendor string and a list of tags.</summary>
  internal static byte[] TheoraComment(string vendor = "test", params string[] tags) {
    var body = new List<byte> { 0x81 };
    body.AddRange(Encoding.ASCII.GetBytes("theora"));
    _AppendComment(body, vendor, tags);
    return body.ToArray();
  }

  /// <summary>A Theora setup header, whose contents no demuxer reads.</summary>
  internal static byte[] TheoraSetup(int length = 64) {
    var packet = new byte[length];
    packet[0] = 0x82;
    Encoding.ASCII.GetBytes("theora").CopyTo(packet, 1);
    for (var i = 7; i < length; ++i)
      packet[i] = (byte)i;

    return packet;
  }

  /// <summary>A Theora data packet: a frame's worth of bytes, of the stated type.</summary>
  /// <remarks>
  /// The high bit of the first byte is clear, which is what makes it data rather than a header, and
  /// the next bit is the frame type — clear for an intra frame, set for an inter one.
  /// </remarks>
  internal static byte[] TheoraFrame(int length = 16, bool keyFrame = true, byte fill = 0xAB) {
    if (length == 0)
      return [];

    var packet = new byte[length];
    packet[0] = keyFrame ? (byte)0x00 : (byte)0x40;
    for (var i = 1; i < length; ++i)
      packet[i] = fill;

    return packet;
  }

  /// <summary>A Vorbis identification header — Vorbis I specification section 4.2.2.</summary>
  internal static byte[] VorbisIdentification(int sampleRate = 44100, byte channels = 2) {
    var header = new byte[30];
    header[0] = 0x01;
    Encoding.ASCII.GetBytes("vorbis").CopyTo(header, 1);
    header[11] = channels;
    BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(12, 4), (uint)sampleRate);
    header[29] = 1;
    return header;
  }

  internal static byte[] VorbisComment(string vendor = "test", params string[] tags) {
    var body = new List<byte> { 0x03 };
    body.AddRange(Encoding.ASCII.GetBytes("vorbis"));
    _AppendComment(body, vendor, tags);
    return body.ToArray();
  }

  internal static byte[] VorbisSetup(int length = 32) {
    var packet = new byte[length];
    packet[0] = 0x05;
    Encoding.ASCII.GetBytes("vorbis").CopyTo(packet, 1);
    return packet;
  }

  /// <summary>An Opus identification header — RFC 7845 section 5.1.</summary>
  internal static byte[] OpusHead(int preSkip = 312, int inputSampleRate = 48000, byte channels = 1) {
    var header = new byte[19];
    Encoding.ASCII.GetBytes("OpusHead").CopyTo(header, 0);
    header[8] = 1;
    header[9] = channels;
    BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(10, 2), (ushort)preSkip);
    BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(12, 4), (uint)inputSampleRate);
    return header;
  }

  internal static byte[] OpusTags(string vendor = "test", params string[] tags) {
    var body = new List<byte>();
    body.AddRange(Encoding.ASCII.GetBytes("OpusTags"));
    _AppendComment(body, vendor, tags);
    return body.ToArray();
  }

  /// <summary>
  /// A FLAC-in-Ogg mapping header — RFC 9639 section 10.1.
  /// </summary>
  /// <param name="followingHeaders">What the header states about how many header packets come after
  /// it; zero means the writer did not know, and the metadata blocks then say so themselves.</param>
  internal static byte[] FlacMapping(int sampleRate = 44100, int followingHeaders = 1) {
    var header = new byte[51];
    header[0] = 0x7F;
    Encoding.ASCII.GetBytes("FLAC").CopyTo(header, 1);
    header[5] = 1;
    header[6] = 0;
    BinaryPrimitives.WriteUInt16BigEndian(header.AsSpan(7, 2), (ushort)followingHeaders);
    Encoding.ASCII.GetBytes("fLaC").CopyTo(header, 9);

    // A STREAMINFO metadata block: type 0, thirty-four bytes, not the last block.
    header[13] = 0x00;
    _WriteUInt24BigEndian(header.AsSpan(14, 3), 34);

    // Twenty bits of sample rate, ten bytes into the block's body.
    var at = 17 + 10;
    header[at] = (byte)(sampleRate >> 12);
    header[at + 1] = (byte)(sampleRate >> 4);
    header[at + 2] = (byte)((sampleRate & 0x0F) << 4);
    return header;
  }

  /// <summary>A FLAC metadata block packet, of the given type and last-ness.</summary>
  internal static byte[] FlacMetadataBlock(byte type = 4, bool last = true, int length = 16) {
    var packet = new byte[4 + length];
    packet[0] = (byte)(type | (last ? 0x80 : 0x00));
    _WriteUInt24BigEndian(packet.AsSpan(1, 3), (uint)length);
    return packet;
  }

  /// <summary>A FLAC audio frame, which begins with the format's sync code.</summary>
  internal static byte[] FlacFrame(int length = 24) {
    var packet = new byte[length];
    packet[0] = 0xFF;
    packet[1] = 0xF8;
    return packet;
  }

  /// <summary>A packet of a mapping nothing recognises.</summary>
  internal static byte[] UnknownPacket(int length = 12, byte fill = 0x5A) {
    var packet = new byte[length];
    for (var i = 0; i < length; ++i)
      packet[i] = fill;

    return packet;
  }

  // ============================================================================================
  // Whole files
  // ============================================================================================

  /// <summary>
  /// The simplest complete Theora file: three header packets, then one frame a page.
  /// </summary>
  /// <remarks>
  /// Laid out the way ffmpeg lays one out — the identification header alone on the begin-of-stream
  /// page, the comment and setup headers on the second, and the frames after them — because that is
  /// the layout every timing here was measured against.
  /// </remarks>
  internal static byte[] Theora(int frames = 3, int keyFrameEvery = 12, int width = 176, int height = 144) {
    var pages = new List<OggTestPage> {
      new() { Serial = 1, Sequence = 0, Granule = 0, BeginOfStream = true, Packets = [TheoraIdentification(width, height)] },
      new() { Serial = 1, Sequence = 1, Granule = 0, Packets = [TheoraComment(), TheoraSetup()] },
    };

    var keyframes = 0;
    for (var frame = 0; frame < frames; ++frame) {
      var isKey = frame % keyFrameEvery == 0;
      if (isKey)
        ++keyframes;

      var since = frame - (keyframes - 1) * keyFrameEvery;

      pages.Add(new() {
        Serial = 1,
        Sequence = (uint)(2 + frame),
        // The count of frames up to and including the last keyframe, shifted up, plus the count
        // since it. Both count from one, which is why the reader takes one off the sum.
        Granule = ((long)((keyframes - 1) * keyFrameEvery + 1) << THEORA_GRANULE_SHIFT) + since,
        EndOfStream = frame == frames - 1,
        Packets = [TheoraFrame(16 + frame, isKey)],
      });
    }

    return Build(pages.ToArray());
  }

  // ============================================================================================
  // Small conversions
  // ============================================================================================

  private static void _AppendComment(List<byte> body, string vendor, string[] tags) {
    var vendorBytes = Encoding.UTF8.GetBytes(vendor);
    body.AddRange(BitConverter.GetBytes(vendorBytes.Length));
    body.AddRange(vendorBytes);
    body.AddRange(BitConverter.GetBytes(tags.Length));
    foreach (var tag in tags) {
      var tagBytes = Encoding.UTF8.GetBytes(tag);
      body.AddRange(BitConverter.GetBytes(tagBytes.Length));
      body.AddRange(tagBytes);
    }

    // The framing bit every Vorbis-comment header ends with.
    body.Add(1);
  }

  private static void _WriteUInt24BigEndian(Span<byte> target, uint value) {
    target[0] = (byte)(value >> 16);
    target[1] = (byte)(value >> 8);
    target[2] = (byte)value;
  }
}
