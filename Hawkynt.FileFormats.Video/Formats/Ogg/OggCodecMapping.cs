using System;
using System.Buffers.Binary;
using System.IO;
using FileFormat.Core;

namespace FileFormat.Ogg;

/// <summary>Which of the codec mappings a logical bitstream follows.</summary>
internal enum OggCodec {

  /// <summary>A bitstream whose first packet begins with none of the magics known here.</summary>
  Unknown,

  Theora,
  Vorbis,
  Opus,
  Flac,
}

/// <summary>
/// What the Ogg mapping for one codec says about its own bitstream: how many of its packets are
/// headers rather than data, what unit its granule positions count in, and what the identification
/// header states about the stream.
/// </summary>
/// <remarks>
/// Ogg on its own carries no codec information whatever. A page states a serial number and a granule
/// position and nothing about what either means; the format is deliberately a framing layer, and each
/// codec brings a document of its own — RFC 5334 for Theora and Vorbis, RFC 7845 for Opus, RFC 9639
/// for FLAC — saying how it sits inside one. That document is the *mapping*, and this class holds the
/// mappings and not the codecs.
/// <para/>
/// The distinction matters because it is the line this library will not cross. Reading a Theora
/// identification header for its picture size, its frame rate and its keyframe granule shift is
/// reading the mapping: those three are exactly what the mapping says a demuxer needs in order to lay
/// the stream out in time, and without them an Ogg reader cannot report a timestamp at all. Reading
/// the setup header's Huffman tables would be decoding, and nothing here does it — the header packets
/// cross to whoever wants them as opaque bytes.
/// <para/>
/// A bitstream whose magic is none of these is still a bitstream. It is reported, its packets are
/// demuxed, and it carries no timing, which is the honest answer: nothing here knows what its granule
/// counts.
/// </remarks>
internal sealed class OggCodecMapping {

  /// <summary>The mapping this bitstream follows.</summary>
  internal required OggCodec Codec { get; init; }

  /// <summary>
  /// The mapping's own name for the codec, spelled as the magic in the file spells it.
  /// </summary>
  /// <remarks>
  /// A string and not a four-character code, for the same reason Matroska's <c>CodecID</c> is one:
  /// there is no code anywhere in an Ogg file to put in a <see cref="CodecTag"/>, and inventing one
  /// would put a number in a stream's description that is in no file.
  /// </remarks>
  internal required string? CodecId { get; init; }

  /// <summary>What the bitstream carries.</summary>
  internal required MediaStreamKind Kind { get; init; }

  /// <summary>
  /// How many packets of this bitstream are headers rather than data, or -1 where the mapping allows
  /// the writer not to state it.
  /// </summary>
  /// <remarks>
  /// FLAC is the only one that may leave it unstated. Its headers are then the metadata blocks, and
  /// the last of them is the one whose own first byte says so — see
  /// <see cref="IsLastFlacMetadataBlock"/>.
  /// </remarks>
  internal required int HeaderPacketCount { get; init; }

  /// <summary>The picture size the identification header states, for a mapping that carries pictures.</summary>
  internal int Width { get; init; }

  internal int Height { get; init; }

  /// <summary>The seconds one unit of this bitstream's granule positions stands for.</summary>
  internal Rational TimeBase { get; init; } = Rational.Unknown;

  /// <summary>The frames a second the identification header states, for a picture mapping.</summary>
  internal Rational FrameRate { get; init; } = Rational.Unknown;

  /// <summary>
  /// How many bits of a Theora granule position hold the count of frames since the last keyframe.
  /// </summary>
  /// <remarks>
  /// Theora's KFGSHIFT, from the identification header. Zero for every other mapping, which counts
  /// its granule positions straight.
  /// </remarks>
  internal int GranuleShift { get; init; }

  /// <summary>
  /// What to subtract from a granule position to reach a presentation position.
  /// </summary>
  /// <remarks>
  /// Opus's pre-skip, which RFC 7845 section 4 defines as output the decoder produces and the player
  /// must not play — so the position of the first playable sample is the granule position less the
  /// pre-skip, and a reader reporting the raw granule would place the whole stream late by it.
  /// </remarks>
  internal long PositionBias { get; init; }

  /// <summary>
  /// Whether one packet of this bitstream advances the granule position by exactly one unit.
  /// </summary>
  /// <remarks>
  /// True for Theora and for nothing else here, and it is what makes an exact per-packet timestamp
  /// possible. A page states one position, reached once every packet ending on it has been consumed;
  /// where each packet is worth one frame, the packets before the last can be counted backwards from
  /// it exactly. Where a packet is worth a block of sound whose length is stated in the codec's own
  /// setup data, they cannot be, and this reader does not pretend otherwise.
  /// </remarks>
  internal bool OnePositionPerPacket => this.Codec == OggCodec.Theora;

  // ============================================================================================
  // Identification
  // ============================================================================================

  private static ReadOnlySpan<byte> _THEORA_MAGIC => [0x80, (byte)'t', (byte)'h', (byte)'e', (byte)'o', (byte)'r', (byte)'a'];
  private static ReadOnlySpan<byte> _VORBIS_MAGIC => [0x01, (byte)'v', (byte)'o', (byte)'r', (byte)'b', (byte)'i', (byte)'s'];
  private static ReadOnlySpan<byte> _OPUS_MAGIC => "OpusHead"u8;
  private static ReadOnlySpan<byte> _FLAC_MAGIC => [0x7F, (byte)'F', (byte)'L', (byte)'A', (byte)'C'];

  /// <summary>
  /// Works out which mapping a logical bitstream follows, from the first packet of it.
  /// </summary>
  /// <remarks>
  /// The first packet and no other. Every Ogg mapping puts its identification header alone on the
  /// bitstream's first page and begins it with a magic naming the codec, precisely so that this is
  /// answerable without reading anything else — RFC 3533 section 4 requires it of any mapping.
  /// <para/>
  /// That packet is codec-private data rather than a frame. A caller walking packets never sees it;
  /// it goes into <see cref="MediaStreamInfo.CodecPrivateData"/> along with the header packets that
  /// follow it, which is where a decoder expects to find it.
  /// </remarks>
  internal static OggCodecMapping Identify(ReadOnlySpan<byte> firstPacket) {
    if (firstPacket.StartsWith(_THEORA_MAGIC))
      return _Theora(firstPacket);

    if (firstPacket.StartsWith(_VORBIS_MAGIC))
      return _Vorbis(firstPacket);

    if (firstPacket.StartsWith(_OPUS_MAGIC))
      return _Opus(firstPacket);

    if (firstPacket.StartsWith(_FLAC_MAGIC))
      return _Flac(firstPacket);

    return new() {
      Codec = OggCodec.Unknown,
      CodecId = null,
      Kind = MediaStreamKind.Unknown,
      // Nothing is known about a mapping nothing here recognises, including where its data begins.
      // Reporting every packet as data is the reading that loses nothing: a caller copying the
      // bitstream into another container gets all of it, in order, and can find the headers itself.
      HeaderPacketCount = 0,
    };
  }

  /// <summary>
  /// Reads the Theora identification header.
  /// </summary>
  /// <remarks>
  /// Theora specification section 6.2. Forty-two bytes: the magic, three version bytes, the frame
  /// size in macroblocks, the picture region and its offset within the frame, the frame rate as a
  /// ratio, the pixel aspect ratio, the colour space, the nominal bit rate, and a final sixteen bits
  /// packing six of quality hint, five of keyframe granule shift, two of pixel format and three
  /// reserved.
  /// <para/>
  /// The picture region is what is reported as the stream's size, not the frame. Theora codes whole
  /// macroblocks and a picture that is not a multiple of sixteen is coded larger and cropped, so the
  /// two differ for most sizes; ffprobe reports the picture region and so does this.
  /// </remarks>
  private static OggCodecMapping _Theora(ReadOnlySpan<byte> packet) {
    const int IDENTIFICATION_HEADER_SIZE = 42;
    if (packet.Length < IDENTIFICATION_HEADER_SIZE)
      throw new InvalidDataException(
        $"A Theora identification header is {IDENTIFICATION_HEADER_SIZE} bytes and this bitstream's first packet is {packet.Length}.");

    var width = _ReadUInt24BigEndian(packet[14..17]);
    var height = _ReadUInt24BigEndian(packet[17..20]);
    var rateNumerator = BinaryPrimitives.ReadUInt32BigEndian(packet[22..26]);
    var rateDenominator = BinaryPrimitives.ReadUInt32BigEndian(packet[26..30]);
    var packed = BinaryPrimitives.ReadUInt16BigEndian(packet[40..42]);

    return new() {
      Codec = OggCodec.Theora,
      CodecId = "theora",
      Kind = MediaStreamKind.Video,
      // Identification, comment and setup. Theora specification section 6: three, always.
      HeaderPacketCount = 3,
      Width = (int)width,
      Height = (int)height,
      // A granule position counts frames, so one unit is one frame period — the frame rate upside
      // down. ffprobe reports a time base of 1/25 for a 25/1 stream, which is this.
      TimeBase = _Reduce(rateDenominator, rateNumerator),
      FrameRate = _Reduce(rateNumerator, rateDenominator),
      GranuleShift = (packed >> 5) & 0x1F,
    };
  }

  /// <summary>Reads the Vorbis identification header — Vorbis I specification section 4.2.2.</summary>
  private static OggCodecMapping _Vorbis(ReadOnlySpan<byte> packet) {
    const int IDENTIFICATION_HEADER_SIZE = 30;
    if (packet.Length < IDENTIFICATION_HEADER_SIZE)
      throw new InvalidDataException(
        $"A Vorbis identification header is {IDENTIFICATION_HEADER_SIZE} bytes and this bitstream's first packet is {packet.Length}.");

    var sampleRate = BinaryPrimitives.ReadUInt32LittleEndian(packet[12..16]);

    return new() {
      Codec = OggCodec.Vorbis,
      CodecId = "vorbis",
      Kind = MediaStreamKind.Audio,
      // Identification, comment and setup — the same three Theora has, and required in that order
      // before any audio packet.
      HeaderPacketCount = 3,
      TimeBase = _Reduce(1, sampleRate),
    };
  }

  /// <summary>Reads the Opus identification header — RFC 7845 section 5.1.</summary>
  private static OggCodecMapping _Opus(ReadOnlySpan<byte> packet) {
    const int IDENTIFICATION_HEADER_SIZE = 19;
    if (packet.Length < IDENTIFICATION_HEADER_SIZE)
      throw new InvalidDataException(
        $"An Opus identification header is at least {IDENTIFICATION_HEADER_SIZE} bytes and this bitstream's first packet is {packet.Length}.");

    return new() {
      Codec = OggCodec.Opus,
      CodecId = "opus",
      Kind = MediaStreamKind.Audio,
      // OpusHead and OpusTags, and no third.
      HeaderPacketCount = 2,
      // Always forty-eight thousand a second, whatever the encoder was fed: RFC 7845 section 4
      // states granule positions in 48 kHz samples regardless, and the input sample rate in the
      // header is a note for the player rather than the granule's unit.
      TimeBase = new(1, 48000),
      PositionBias = BinaryPrimitives.ReadUInt16LittleEndian(packet[10..12]),
    };
  }

  /// <summary>Reads the FLAC-in-Ogg mapping header — RFC 9639 section 10.1.</summary>
  private static OggCodecMapping _Flac(ReadOnlySpan<byte> packet) {
    // The mapping's own nine bytes, then a whole native FLAC stream head: the four bytes 'fLaC' and
    // a STREAMINFO metadata block with its four-byte header.
    const int MAPPING_HEADER_SIZE = 9;
    const int STREAM_INFO_AT = MAPPING_HEADER_SIZE + 4 + 4;
    const int SAMPLE_RATE_AT = STREAM_INFO_AT + 10;
    if (packet.Length < SAMPLE_RATE_AT + 3)
      throw new InvalidDataException(
        $"A FLAC-in-Ogg mapping header carries a STREAMINFO block, and this bitstream's first packet is only {packet.Length} bytes.");

    // Twenty bits, beginning on a byte boundary and ending in the middle of one.
    var sampleRate = ((uint)packet[SAMPLE_RATE_AT] << 12)
                     | ((uint)packet[SAMPLE_RATE_AT + 1] << 4)
                     | ((uint)packet[SAMPLE_RATE_AT + 2] >> 4);

    // The count the mapping states excludes this packet, so the bitstream's header packets are one
    // more than it. A zero means the writer did not know, which the mapping allows.
    var statedFollowing = BinaryPrimitives.ReadUInt16BigEndian(packet[7..9]);

    return new() {
      Codec = OggCodec.Flac,
      CodecId = "flac",
      Kind = MediaStreamKind.Audio,
      HeaderPacketCount = statedFollowing == 0 ? -1 : statedFollowing + 1,
      TimeBase = _Reduce(1, sampleRate),
    };
  }

  /// <summary>
  /// Whether a FLAC metadata block packet is the last of them.
  /// </summary>
  /// <remarks>
  /// The high bit of a metadata block's own first byte. Only consulted for a bitstream whose mapping
  /// header left the header count unstated, and only because the alternative — telling a metadata
  /// block from an audio frame by its type field — cannot be done: a frame begins 0xFF, which reads
  /// as a last block of type 127, and type 127 is the one value the format forbids. So the count is
  /// kept rather than the classification guessed.
  /// </remarks>
  internal static bool IsLastFlacMetadataBlock(ReadOnlySpan<byte> packet)
    => packet.Length > 0 && (packet[0] & 0x80) != 0;

  // ============================================================================================
  // Timing
  // ============================================================================================

  /// <summary>
  /// Turns a granule position into a position counted in the units this stream's time base measures.
  /// </summary>
  /// <remarks>
  /// This is the whole reason a demuxer has to know which codec it is looking at. A granule position
  /// is not a timestamp and the format says so: it is a stream position whose meaning the mapping
  /// defines, and the mappings define it differently enough that no common reading exists.
  /// <para/>
  /// Vorbis, Opus and FLAC count output samples, so the position is the number itself — less Opus's
  /// pre-skip, which is output the player throws away.
  /// <para/>
  /// Theora packs two numbers into the field: the count of frames up to and including the last
  /// keyframe in the high bits, and the count of frames since it in the low KFGSHIFT bits. Their sum
  /// is a frame count beginning at one, so the frame's index counting from zero is one less. That is
  /// <c>th_granule_frame</c> in the reference implementation, and it is what makes a file whose first
  /// data page carries a granule of 64 begin at presentation timestamp zero rather than one — which
  /// is what ffprobe reports for it.
  /// </remarks>
  internal long? PositionOf(long granule) {
    if (granule < 0 || this.Codec == OggCodec.Unknown)
      return null;

    if (this.Codec != OggCodec.Theora)
      return granule - this.PositionBias;

    // A stream from before Theora 3.2.1 states a shift of zero and counts frames straight; the sum
    // below degenerates to the granule itself, which is that same reading.
    var keyframes = granule >> this.GranuleShift;
    var since = granule - (keyframes << this.GranuleShift);
    return keyframes + since - 1;
  }

  /// <summary>
  /// Whether a data packet of this mapping can be decoded without anything before it.
  /// </summary>
  /// <remarks>
  /// Answered from the packet's own first byte for Theora, which the mapping reserves for exactly
  /// this: the high bit separates a header packet from a data packet and the next bit is the frame
  /// type, clear for an intra frame and set for an inter one (Theora specification section 7.1).
  /// Reading it is not decoding — it is the same byte that had to be read to know the packet was
  /// data at all.
  /// <para/>
  /// The other available reading, that a granule position whose offset field is zero marks a
  /// keyframe, is true and is not enough: only one packet on a page carries a granule position, so a
  /// page holding several frames would leave the rest of them unclassified.
  /// <para/>
  /// Every packet of the audio mappings here is independently decodable once the setup headers have
  /// been read, which is what ffprobe reports for them.
  /// </remarks>
  internal bool IsKeyFrame(ReadOnlySpan<byte> packet) {
    if (this.Codec != OggCodec.Theora)
      return this.Codec != OggCodec.Unknown;

    // A zero-length data packet is Theora's way of saying "show the previous frame again", so it is
    // by definition not a frame anything can start at.
    return packet.Length > 0 && (packet[0] & 0x40) == 0;
  }

  // ============================================================================================
  // Small conversions
  // ============================================================================================

  private static uint _ReadUInt24BigEndian(ReadOnlySpan<byte> data)
    => ((uint)data[0] << 16) | ((uint)data[1] << 8) | data[2];

  /// <summary>Puts a ratio in lowest terms so it reads the way the file meant it.</summary>
  private static Rational _Reduce(long numerator, long denominator) {
    if (numerator == 0 || denominator == 0)
      return Rational.Unknown;

    var a = Math.Abs(numerator);
    var b = Math.Abs(denominator);
    while (b != 0)
      (a, b) = (b, a % b);

    return new(numerator / a, denominator / a);
  }
}
