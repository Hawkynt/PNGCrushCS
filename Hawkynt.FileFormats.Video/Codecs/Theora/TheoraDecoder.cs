using System;
using System.IO;

namespace FileFormat.Codecs.Theora;

/// <summary>
/// Decodes a Theora video stream: the three setup headers, then a picture per packet.
/// </summary>
/// <remarks>
/// Theora specification chapter 7. One of these exists per stream because almost everything about
/// decoding a Theora frame is stateful — the two reference frames a predicted frame is built from,
/// the quantisation matrices the setup header defines, and the block geometry that follows from the
/// frame size.
/// <para/>
/// The decode of one frame runs in the order the specification gives, and the order is not
/// negotiable: the coded block flags decide which macro blocks have modes, the modes decide which
/// have motion vectors, and the coefficients are read in 64 passes over the whole frame — one per
/// zig-zag position — rather than block by block, so nothing about a block is complete until the
/// last pass is done. Only then is DC prediction undone, and only then can anything be
/// reconstructed.
/// <para/>
/// Nothing here catches an exception and hands back a picture. A packet that runs out part way
/// through, a header that states a version this does not read, a reserved field that is set: each
/// refuses by name and says which field was wrong.
/// </remarks>
internal sealed partial class TheoraDecoder {

  /// <summary>The packet type byte of each of the three headers, and of a data packet.</summary>
  private const int _IDENTIFICATION_HEADER = 0x80;

  private const int _COMMENT_HEADER = 0x81;
  private const int _SETUP_HEADER = 0x82;

  /// <summary>The magic every header packet carries after its type byte.</summary>
  private static ReadOnlySpan<byte> _MAGIC => "theora"u8;

  /// <summary>The quantisation index a duplicate frame is nominally decoded at — section 7.11.</summary>
  /// <remarks>
  /// It selects nothing, because a duplicate frame codes no blocks and so dequantises nothing. It
  /// does select the loop filter limit, and the filter runs over no coded edges either.
  /// </remarks>
  private const int _DUPLICATE_FRAME_QUANTISATION_INDEX = 63;

  private TheoraIdentificationHeader? _identification;
  private TheoraSetupHeader? _setup;
  private TheoraQuantisation? _quantisation;
  private TheoraGeometry? _geometry;

  private TheoraFrame? _current;
  private TheoraFrame? _previous;
  private TheoraFrame? _golden;

  /// <summary>Whether any frame has been decoded, so that a reference frame exists.</summary>
  private bool _hasReference;

  // -------- Per-frame state, allocated once the geometry is known --------

  private bool[] _coded = [];
  private bool[] _superBlockPartial = [];
  private bool[] _superBlockFull = [];
  private bool[] _runBuffer = [];
  private byte[] _modes = [];
  private sbyte[] _motionX = [];
  private sbyte[] _motionY = [];
  private byte[] _quantisationIndices = [];
  private short[] _coefficients = [];
  private byte[] _coefficientCounts = [];
  private byte[] _tokenIndices = [];

  private int _frameType;
  private int _quantisationIndexCount;
  private readonly int[] _frameQuantisationIndices = new int[3];

  /// <summary>
  /// What a motion vector component is divided by, per plane, to reach whole pixels.
  /// </summary>
  /// <remarks>
  /// Two for a luma plane, where a component is at half-pixel resolution, and four along an axis a
  /// chroma plane subsamples, where the same displacement of the picture works out at quarter-pixel
  /// resolution. 4:2:2 subsamples horizontally and not vertically, so its two axes differ.
  /// </remarks>
  private readonly int[] _motionDivisorX = new int[3];

  private readonly int[] _motionDivisorY = new int[3];

  /// <summary>What the identification header said, once it has been read.</summary>
  internal TheoraIdentificationHeader Identification
    => this._identification ?? throw new InvalidOperationException("The identification header has not been read.");

  // ============================================================================================
  // Headers
  // ============================================================================================

  /// <summary>
  /// Reads the three setup headers out of the block of bytes a container hands across.
  /// </summary>
  /// <remarks>
  /// The packets arrive framed in Xiph lacing — a count of packets less one, then the length of all
  /// but the last, then the packets end to end. That is how Matroska stores the private data of a
  /// Theora track and how the Ogg reader in this library packs the header packets it finds, so one
  /// decoder reads a stream out of either container without being told which it came from.
  /// </remarks>
  internal void Configure(ReadOnlyMemory<byte> codecPrivateData) {
    if (codecPrivateData.Length < 1)
      throw new InvalidDataException(
        "A Theora stream carries its identification, comment and setup headers as codec private data, and this stream's is empty.");

    var bytes = codecPrivateData.Span;
    var packets = bytes[0] + 1;
    if (packets < 3)
      throw new InvalidDataException(
        $"A Theora stream has three header packets and this stream's private data states {packets}.");

    var at = 1;
    Span<int> lengths = stackalloc int[packets];
    var total = 0;
    for (var packet = 0; packet < packets - 1; ++packet) {
      var length = 0;
      while (true) {
        if (at >= bytes.Length)
          throw new InvalidDataException("The Xiph lacing of the Theora header packets runs off the end of the private data.");

        var value = bytes[at++];
        length += value;
        if (value < 255)
          break;
      }

      lengths[packet] = length;
      total += length;
    }

    if (at + total > codecPrivateData.Length)
      throw new InvalidDataException(
        $"The Theora header packets state {total} bytes before the last one, and only {codecPrivateData.Length - at} are there.");

    // The last packet's length is not stated, because it is whatever is left.
    lengths[packets - 1] = codecPrivateData.Length - at - total;

    for (var packet = 0; packet < packets; ++packet) {
      this._ReadHeader(codecPrivateData.Slice(at, lengths[packet]));
      at += lengths[packet];
    }

    if (this._identification == null)
      throw new InvalidDataException("The Theora header packets hold no identification header.");

    if (this._setup == null)
      throw new InvalidDataException("The Theora header packets hold no setup header.");
  }

  /// <summary>Reads one header packet, whichever of the three it turns out to be.</summary>
  private void _ReadHeader(ReadOnlyMemory<byte> packet) {
    if (packet.Length < 7)
      throw new InvalidDataException(
        $"A Theora header packet is at least seven bytes — a type and the magic — and this one is {packet.Length}.");

    var type = packet.Span[0];
    if (!packet.Span.Slice(1, 6).SequenceEqual(_MAGIC))
      throw new InvalidDataException(
        $"A Theora header packet of type 0x{type:X2} does not carry the magic 'theora' after its type byte.");

    // The comment header holds a vendor string and a list of tags, none of which decoding uses. The
    // container has already read it for what a caller wants out of it.
    if (type == _COMMENT_HEADER)
      return;

    var reader = new TheoraBitReader(packet[7..]);

    switch (type) {
      case _IDENTIFICATION_HEADER:
        this._identification = TheoraIdentificationHeader.Read(reader);
        this._Prepare();
        break;

      case _SETUP_HEADER:
        if (this._identification == null)
          throw new InvalidDataException("A Theora setup header arrived before the identification header that says how big its frames are.");

        this._setup = TheoraSetupHeader.Read(reader);
        this._quantisation = new(this._setup);
        break;

      default:
        // Reserved packet types are to be ignored rather than refused, but a header packet type this
        // decoder does not know cannot be one of the three it needs, and a stream missing one of
        // those is refused where the count is checked.
        break;
    }
  }

  /// <summary>Works out the block geometry and allocates everything a frame decode needs.</summary>
  private void _Prepare() {
    var geometry = new TheoraGeometry(this.Identification);
    this._geometry = geometry;

    var format = this.Identification.PixelFormat;
    for (var plane = 0; plane < 3; ++plane) {
      this._motionDivisorX[plane] = plane > 0 && format != TheoraPixelFormat.Yuv444 ? 4 : 2;
      this._motionDivisorY[plane] = plane > 0 && format == TheoraPixelFormat.Yuv420 ? 4 : 2;
    }

    this._current = TheoraFrame.Create(geometry);
    this._previous = TheoraFrame.Create(geometry);
    this._golden = TheoraFrame.Create(geometry);

    this._coded = new bool[geometry.BlockCount];
    this._superBlockPartial = new bool[geometry.SuperBlockCount];
    this._superBlockFull = new bool[geometry.SuperBlockCount];
    this._runBuffer = new bool[Math.Max(geometry.BlockCount, geometry.SuperBlockCount)];
    this._modes = new byte[geometry.MacroBlockCount];
    this._motionX = new sbyte[geometry.BlockCount];
    this._motionY = new sbyte[geometry.BlockCount];
    this._quantisationIndices = new byte[geometry.BlockCount];
    this._coefficients = new short[geometry.BlockCount * 64];
    this._coefficientCounts = new byte[geometry.BlockCount];
    this._tokenIndices = new byte[geometry.BlockCount];
  }

  // ============================================================================================
  // Frames
  // ============================================================================================

  /// <summary>
  /// Decodes one packet into a picture.
  /// </summary>
  /// <returns>The frame just decoded, which the caller must not hold past the next call.</returns>
  internal TheoraFrame Decode(ReadOnlyMemory<byte> packet) {
    if (this._setup == null || this._geometry == null)
      throw new InvalidOperationException("The Theora setup headers have not been read, so no frame can be decoded.");

    var geometry = this._geometry;

    if (packet.Length == 0) {
      // A zero-length packet is an inter frame with nothing coded — the format's way of saying
      // "show that again". It is not an error handled by repeating a frame; it is a frame the
      // stream explicitly states is the same picture, and the reconstruction below arrives at that
      // by the ordinary path, copying every block from the previous reference.
      if (!this._hasReference)
        throw new InvalidDataException("This Theora stream begins with a zero-length packet, which is a duplicate of a frame that does not exist yet.");

      this._frameType = 1;
      this._quantisationIndexCount = 1;
      this._frameQuantisationIndices[0] = _DUPLICATE_FRAME_QUANTISATION_INDEX;
      Array.Clear(this._coded);
      Array.Clear(this._motionX);
      Array.Clear(this._motionY);
      return this._Finish(geometry);
    }

    // A packet whose first bit is set is a header rather than a frame, and one whose type this
    // decoder does not know is reserved. Neither is a picture.
    var first = packet.Span[0];
    if ((first & 0x80) != 0)
      throw new InvalidDataException(
        $"A packet beginning 0x{first:X2} is a Theora header rather than a frame; the three headers belong in the stream's private data.");

    var reader = new TheoraBitReader(packet);
    this._ReadFrameHeader(reader);

    if (this._frameType != 0 && !this._hasReference)
      throw new InvalidDataException(
        "This Theora stream begins with an inter frame, which is a difference from reference frames that have not been decoded. Decoding must begin at an intra frame.");

    this._ReadCodedBlockFlags(reader, geometry);
    this._ReadMacroBlockModes(reader, geometry);
    this._ReadMotionVectors(reader, geometry);
    this._ReadBlockQuantisationIndices(reader, geometry);
    this._ReadCoefficients(reader, geometry);

    // Every read above may have run past the end of the packet, and a read past the end returns
    // zeroes — which are a perfectly valid bitstream. Without this check a truncated packet becomes
    // a picture with no sign that anything went wrong.
    reader.EnsureComplete("the frame's coded data");

    this._UndoDcPrediction(geometry);
    return this._Finish(geometry);
  }

  /// <summary>Reconstructs, filters, and moves the new frame into the reference slots.</summary>
  private TheoraFrame _Finish(TheoraGeometry geometry) {
    this._Reconstruct(geometry);

    TheoraLoopFilter.Apply(
      this._current!, geometry, this._coded, this._setup!.LoopFilterLimits[this._frameQuantisationIndices[0]]);

    // An intra frame becomes the golden frame as well as the previous one; every frame becomes the
    // previous one. The two are treated as distinct even when they hold the same picture, because
    // DC prediction and the coding modes distinguish them.
    if (this._frameType == 0)
      this._golden!.CopyFrom(this._current!);

    (this._current, this._previous) = (this._previous, this._current);
    this._hasReference = true;
    return this._previous!;
  }

  /// <summary>Reads the frame header — section 7.1.</summary>
  private void _ReadFrameHeader(TheoraBitReader reader) {
    if (reader.ReadBit() != 0)
      throw new InvalidDataException("The first bit of a Theora data packet must be zero, and this packet's is not.");

    this._frameType = reader.ReadBit();

    // From one to three quantisation indices. The first is used for every DC coefficient in the
    // frame — DC prediction happens in the quantised domain, so a DC coefficient dequantised at a
    // different index from its neighbour's would predict from a value on another scale — and the AC
    // coefficients of each block may use any of them.
    this._frameQuantisationIndices[0] = (int)reader.ReadBits(6);
    this._quantisationIndexCount = 1;

    if (reader.ReadBit() != 0) {
      this._frameQuantisationIndices[1] = (int)reader.ReadBits(6);
      this._quantisationIndexCount = 2;

      if (reader.ReadBit() != 0) {
        this._frameQuantisationIndices[2] = (int)reader.ReadBits(6);
        this._quantisationIndexCount = 3;
      }
    }

    if (this._frameType != 0)
      return;

    var reserved = reader.ReadBits(3);
    if (reserved != 0)
      throw new NotSupportedException(
        $"An intra frame's three reserved header bits are {reserved} rather than zero, so this stream uses a feature the Theora I specification does not define.");
  }
}
