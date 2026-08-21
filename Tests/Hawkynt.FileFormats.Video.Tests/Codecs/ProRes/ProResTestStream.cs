using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using FileFormat.Core;

namespace FileFormat.Codecs.ProRes.Tests;

/// <summary>
/// Writes ProRes frames a codeword at a time, so a test can state exactly which part of the
/// bitstream it is exercising.
/// </summary>
/// <remarks>
/// Built rather than checked in. The arithmetic of this decoder was measured against ffmpeg over
/// every profile, both encoders, progressive and interlaced, with and without alpha; what these
/// streams add is what that comparison cannot reach — a codeword that tells the Golomb-Rice half of
/// a codebook from the exponential-Golomb half, a codebook adaptation exercised in the direction a
/// natural picture rarely goes, and the refusals, which by definition no valid stream contains.
/// <para/>
/// A frame here is the smallest one the format allows: sixteen by sixteen pixels, which is one
/// macroblock, which with a desired slice size of one macroblock is one slice. Everything a test
/// wants to say then fits in three short bit strings — one per colour component — and the expected
/// picture can be written down beside it.
/// </remarks>
internal sealed class ProResTestStream {

  private readonly List<byte> _bytes = [];
  private int _partial;
  private int _partialBits;

  // ============================================================================================
  // The bits of one colour component
  // ============================================================================================

  /// <summary>Appends a codeword written as its bits, the way a codebook would be printed.</summary>
  internal ProResTestStream Code(string code) {
    foreach (var character in code)
      switch (character) {
        case '0': this._Bit(0); break;
        case '1': this._Bit(1); break;
        case ' ': break;
        default: throw new ArgumentException($"'{character}' is not a bit.", nameof(code));
      }

    return this;
  }

  /// <summary>Appends the low <paramref name="count"/> bits of a value, most significant first.</summary>
  internal ProResTestStream Bits(int value, int count) {
    for (var i = count - 1; i >= 0; --i)
      this._Bit((value >> i) & 1);

    return this;
  }

  /// <summary>
  /// Finishes a colour component, padding with zero bits to the next byte.
  /// </summary>
  /// <remarks>
  /// The padding is not decoration: RDD 36:2022, 5.3.2 ends a component with zero bits to a byte
  /// boundary, and the run-and-level loop stops exactly when the bits that remain are fewer than
  /// thirty-two and all zero. A component whose codewords happen to fill its last byte therefore
  /// needs a byte of nothing after it, which <paramref name="spare"/> supplies.
  /// </remarks>
  internal byte[] End(int spare = 1) {
    while (this._partialBits != 0)
      this._Bit(0);

    for (var i = 0; i < spare; ++i)
      this._bytes.Add(0);

    return this._bytes.ToArray();
  }

  private void _Bit(int bit) {
    this._partial = (this._partial << 1) | bit;
    if (++this._partialBits != 8)
      return;

    this._bytes.Add((byte)this._partial);
    this._partial = 0;
    this._partialBits = 0;
  }

  // ============================================================================================
  // The frame around them
  // ============================================================================================

  /// <summary>Everything a test may want to say differently about a frame header.</summary>
  internal sealed class Options {
    internal int Width { get; init; } = 16;
    internal int Height { get; init; } = 16;
    internal int Version { get; init; }
    internal int ChromaFormat { get; init; } = 2;
    internal int InterlaceMode { get; init; }
    internal int AlphaChannelType { get; init; }
    internal int MatrixCoefficients { get; init; } = 2;
    internal int QuantizationIndex { get; init; } = 1;
    internal int Log2SliceSize { get; init; }
    internal byte[]? LumaMatrix { get; init; }
    internal byte[]? ChromaMatrix { get; init; }

    /// <summary>Overrides the frame's stated size, for the tests that want it wrong.</summary>
    internal int? StatedFrameSize { get; init; }

    /// <summary>Overrides the four bytes that identify a compressed frame.</summary>
    internal string Identifier { get; init; } = "icpf";
  }

  /// <summary>
  /// Assembles one frame holding one picture of one slice.
  /// </summary>
  /// <param name="options">What the frame header says about itself.</param>
  /// <param name="components">The coded data of the colour components, and alpha where there is one.</param>
  internal static byte[] Frame(Options options, params byte[][] components) {
    ArgumentNullException.ThrowIfNull(options);
    ArgumentNullException.ThrowIfNull(components);

    var hasAlpha = options.AlphaChannelType != 0;
    var slice = _Slice(options, hasAlpha, components);
    var picture = _Picture(options, slice);
    var header = _FrameHeader(options);

    var frame = new List<byte>();
    frame.AddRange(new byte[4]);
    foreach (var character in options.Identifier)
      frame.Add((byte)character);

    frame.AddRange(header);
    frame.AddRange(picture);

    var size = options.StatedFrameSize ?? frame.Count;
    var bytes = frame.ToArray();
    BinaryPrimitives.WriteUInt32BigEndian(bytes, (uint)size);

    return bytes;
  }

  /// <summary>RDD 36:2022, 5.1.1 — twenty bytes, then the quantisation weight matrices.</summary>
  private static byte[] _FrameHeader(Options options) {
    var matrices = new List<byte>();
    var flags = 0;
    if (options.LumaMatrix != null) {
      flags |= 2;
      matrices.AddRange(options.LumaMatrix);
    }

    if (options.ChromaMatrix != null) {
      flags |= 1;
      matrices.AddRange(options.ChromaMatrix);
    }

    var header = new byte[20 + matrices.Count];
    BinaryPrimitives.WriteUInt16BigEndian(header, (ushort)header.Length);
    header[3] = (byte)options.Version;
    header[4] = (byte)'t';
    header[5] = (byte)'e';
    header[6] = (byte)'s';
    header[7] = (byte)'t';
    BinaryPrimitives.WriteUInt16BigEndian(header.AsSpan(8), (ushort)options.Width);
    BinaryPrimitives.WriteUInt16BigEndian(header.AsSpan(10), (ushort)options.Height);
    header[12] = (byte)((options.ChromaFormat << 6) | (options.InterlaceMode << 2));
    header[16] = (byte)options.MatrixCoefficients;
    header[17] = (byte)options.AlphaChannelType;
    header[19] = (byte)flags;
    matrices.CopyTo(header, 20);

    return header;
  }

  /// <summary>RDD 36:2022, 5.2.1 and 5.2.2 — the picture header and its one-entry slice table.</summary>
  private static byte[] _Picture(Options options, byte[] slice) {
    var picture = new byte[10 + slice.Length];
    picture[0] = 8 << 3;
    BinaryPrimitives.WriteUInt32BigEndian(picture.AsSpan(1), (uint)picture.Length);
    BinaryPrimitives.WriteUInt16BigEndian(picture.AsSpan(5), 1);
    picture[7] = (byte)(options.Log2SliceSize << 4);
    BinaryPrimitives.WriteUInt16BigEndian(picture.AsSpan(8), (ushort)slice.Length);
    slice.CopyTo(picture, 10);

    return picture;
  }

  /// <summary>RDD 36:2022, 5.3.1 — the slice header, then the components one after another.</summary>
  private static byte[] _Slice(Options options, bool hasAlpha, byte[][] components) {
    var headerSize = hasAlpha ? 8 : 6;
    var slice = new List<byte>(new byte[headerSize]);
    foreach (var component in components)
      slice.AddRange(component);

    var bytes = slice.ToArray();
    bytes[0] = (byte)(headerSize << 3);
    bytes[1] = (byte)options.QuantizationIndex;
    BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(2), (ushort)components[0].Length);
    BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(4), (ushort)components[1].Length);
    if (hasAlpha)
      BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(6), (ushort)components[2].Length);

    return bytes;
  }

  /// <summary>A stream description naming ProRes at the size the frames will state.</summary>
  internal static MediaStreamInfo Stream(int width = 16, int height = 16, string codec = "apcn") => new() {
    Index = 0,
    Kind = MediaStreamKind.Video,
    Codec = CodecTag.FromCharacters(codec),
    Width = width,
    Height = height,
  };
}
