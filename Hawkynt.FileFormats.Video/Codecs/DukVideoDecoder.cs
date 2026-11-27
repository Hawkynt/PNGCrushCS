using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using FileFormat.Core;
using FileFormat.Duk;

namespace FileFormat.Codecs;

/// <summary>
/// Decodes the Duck TrueMotion pictures of Star Control II's 3DO cutscenes (<c>UQMV</c>, from a
/// <see cref="DukContainer"/>): 15-bit pictures built from deltas the film's codebook names, in bands
/// of eight or four rows.
/// </summary>
/// <remarks>
/// Written from a specification derived from NihAV's <c>na_game_tool</c> (GPL-3.0, read and not
/// converted), which is the oracle the tests compare against. The picture is held as words of two
/// RGB555 pixels, the left in the high half. Each byte of a picture names one of the codebook's 256
/// vectors, and a vector is up to seven pairs of indices into the film's two eight-entry delta tables,
/// read one pair at a time. A chrominance pair adds its two deltas to the red and blue of both pixels
/// of a word; a luminance pair adds half of each delta to all three components of its pixel, and in
/// the eight-row layout the odd bit of each delta flips the pixel's spare top bit, which carries into
/// the next addition, all of it in wrapping 32-bit arithmetic. In the eight-row layout (method 1)
/// every band is two rows of words, each predicted from the one above it, and each shown four times;
/// in the four-row layout (method 3) a band is one row of words, shown four times. Every picture starts
/// from a black row above it, so every picture stands alone. Handed out as <see cref="PixelFormat.Rgb24"/>,
/// each five-bit component widened to eight by repeating its top bits.
/// </remarks>
public sealed class DukVideoDecoder : IVideoCodecDecoder<DukVideoDecoder> {

  private readonly int _width;
  private readonly int _height;
  private readonly short[] _lumaDeltas = new short[8];
  private readonly short[] _chromaDeltas = new short[8];
  private readonly byte[] _codebook;

  private DukVideoDecoder(int width, int height, byte[] configuration) {
    this._width = width;
    this._height = height;
    for (var i = 0; i < 8; ++i) {
      this._lumaDeltas[i] = BinaryPrimitives.ReadInt16BigEndian(configuration.AsSpan(16 + 2 * i));
      this._chromaDeltas[i] = BinaryPrimitives.ReadInt16BigEndian(configuration.AsSpan(32 + 2 * i));
    }

    this._codebook = configuration[DukContainer.HeaderLength..];
  }

  public static string CodecName => "Star Control II 3DO TrueMotion";

  public static bool Accepts(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);
    return stream.Kind == MediaStreamKind.Video && stream.Codec == DukContainer.VideoCodec;
  }

  public static DukVideoDecoder Create(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);
    if (!Accepts(stream))
      throw new NotSupportedException($"Stream {stream.Index} is not a Star Control II 3DO picture stream.");
    if (stream.CodecPrivateData.Length < DukContainer.HeaderLength + DukContainer.CodebookLength)
      throw new NotSupportedException($"Stream {stream.Index} carries no Star Control II header and codebook.");
    if (stream.Width <= 0 || stream.Width % 8 != 0 || stream.Height <= 0 || stream.Height % 8 != 0)
      throw new NotSupportedException($"Star Control II pictures are whole eight-pixel blocks; stream {stream.Index} states {stream.Width}x{stream.Height}.");
    return new(stream.Width, stream.Height, stream.CodecPrivateData.ToArray());
  }

  public bool TryDecode(CodedPacket packet, out RawImage frame) {
    var words = this.DecodeWords(packet.Data.Span);
    var pixels = new byte[this._width * this._height * 3];
    for (var i = 0; i < words.Length; ++i)
      for (var half = 0; half < 2; ++half) {
        var value = half == 0 ? words[i] >> 16 : words[i] & 0xFFFF;
        var at = (2 * i + half) * 3;
        pixels[at] = _Widen((value >> 10) & 31);
        pixels[at + 1] = _Widen((value >> 5) & 31);
        pixels[at + 2] = _Widen(value & 31);
      }

    frame = new() { Width = this._width, Height = this._height, Format = PixelFormat.Rgb24, PixelData = pixels };
    return true;
  }

  public IEnumerable<RawImage> Flush() => [];

  private static byte _Widen(uint five) => (byte)((five << 3) | (five >> 2));

  /// <summary>
  /// The picture as words of two pixels, top row first, each with its spare bit as decoded: the left
  /// pixel in bits 16 to 31, the right in bits 0 to 15.
  /// </summary>
  internal uint[] DecodeWords(ReadOnlySpan<byte> data) {
    if (data.Length < 16)
      throw new InvalidDataException("A Star Control II picture opens with sixteen bytes of header.");
    var method = data[0];
    var width = BinaryPrimitives.ReadUInt16LittleEndian(data[14..]);
    var height = BinaryPrimitives.ReadUInt16LittleEndian(data[12..]);
    if (method is not (1 or 3))
      throw new NotSupportedException($"This Star Control II picture states method {method}; only 1 and 3 are known.");
    if (width * 2 != this._width || height < this._height)
      throw new InvalidDataException($"This Star Control II picture states {width * 2}x{height}, which its film of {this._width}x{this._height} does not match.");

    var stride = this._width / 2;
    var output = new uint[stride * this._height];
    var reader = new _Indices(this._codebook, data);
    var above = new uint[stride];
    var first = new uint[stride];
    var second = new uint[stride];
    var row = 0;

    void Show(uint[] line, int times) {
      for (var t = 0; t < times; ++t, ++row)
        line.CopyTo(output, row * stride);
    }

    if (method == 1) {
      while (row < this._height) {
        uint firstSum = 0, secondSum = 0, firstFlip = 0, secondFlip = 0;
        for (var x = 0; x < stride; x += 4) {
          firstSum += this._Chroma(reader.Next());
          for (var i = x; i < x + 4; ++i) {
            var pair = reader.Next();
            firstSum += this._Luma(pair);
            firstFlip ^= this._Flip(pair);
            first[i] = (above[i] + firstSum) ^ firstFlip;
          }

          for (var i = x; i < x + 4; ++i) {
            var pair = reader.Next();
            secondSum += this._Luma(pair);
            secondFlip ^= this._Flip(pair);
            second[i] = (first[i] + secondSum) ^ secondFlip;
          }
        }

        Show(first, 4);
        Show(second, 4);
        (above, second) = (second, above);
      }
    } else {
      while (row < this._height) {
        uint sum = 0;
        for (var x = 0; x < stride; ++x) {
          if ((x & 3) == 0)
            sum += this._Chroma(reader.Next());
          sum += this._Luma(reader.Next());
          first[x] = sum + above[x];
        }

        Show(first, 4);
        (above, first) = (first, above);
      }
    }

    return output;
  }

  /// <summary>Red and blue of both pixels of a word, by the two chrominance deltas.</summary>
  private uint _Chroma((int A, int B) pair) {
    int red = this._chromaDeltas[pair.A], blue = this._chromaDeltas[pair.B];
    return unchecked((uint)((red << 26) + (blue << 16) + (red << 10) + blue));
  }

  /// <summary>All three components of each pixel, by half its luminance delta.</summary>
  private uint _Luma((int A, int B) pair) {
    int left = this._lumaDeltas[pair.A] >> 1, right = this._lumaDeltas[pair.B] >> 1;
    return unchecked((uint)(((left * 0x421) << 16) + right * 0x421));
  }

  /// <summary>The spare bit of each pixel, flipped by the odd bit of its luminance delta.</summary>
  private uint _Flip((int A, int B) pair)
    => (uint)(this._lumaDeltas[pair.A] & 1) << 31 | (uint)(this._lumaDeltas[pair.B] & 1) << 15;

  /// <summary>The picture's bytes as codebook vectors, handed out a pair of delta indices at a time.</summary>
  private ref struct _Indices(byte[] codebook, ReadOnlySpan<byte> data) {
    private readonly ReadOnlySpan<byte> _data = data;
    private int _at = 16;
    private int _vector = -1;
    private int _position;

    internal (int A, int B) Next() {
      if (this._vector < 0) {
        if (this._at >= this._data.Length)
          throw new InvalidDataException("This Star Control II picture ends before its last pixel.");
        this._vector = this._data[this._at++];
        this._position = 0;
        if (codebook[16 * this._vector] > 14)
          throw new InvalidDataException($"Codebook vector {this._vector} of this Star Control II film states {codebook[16 * this._vector]} indices; a vector holds at most fourteen.");
      }

      var entry = 16 * this._vector;
      var pair = ((int)codebook[entry + this._position + 1], (int)codebook[entry + this._position + 2]);
      if (pair.Item1 > 7 || pair.Item2 > 7)
        throw new InvalidDataException($"Codebook vector {this._vector} of this Star Control II film names a delta past the eight its tables hold.");
      this._position += 2;
      if (this._position >= codebook[entry])
        this._vector = -1;
      return pair;
    }
  }
}
