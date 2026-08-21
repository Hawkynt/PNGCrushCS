using System;
using System.IO;

namespace FileFormat.Codecs.HuffYuv;

/// <summary>How a HuffYUV frame predicts each sample from the ones already decoded.</summary>
internal enum HuffYuvPredictor {

  /// <summary>The same component of the pixel to the left.</summary>
  Left = 0,

  /// <summary>Left plus above minus above-left — a plane through the three known corners.</summary>
  Gradient = 1,

  /// <summary>The median of left, above, and left plus above minus above-left.</summary>
  Median = 2,
}

/// <summary>What the samples of a HuffYUV stream are.</summary>
internal enum HuffYuvColourSpace {

  /// <summary>One plane of luminance and nothing else.</summary>
  Grey,

  /// <summary>Luminance and two chrominance planes, subsampled or not.</summary>
  Yuv,

  /// <summary>Three colour planes in the order green, blue, red, as a planar RGB stream has them.</summary>
  PlanarRgb,

  /// <summary>Blue, green, red and alpha interleaved a pixel at a time, bottom row first.</summary>
  PackedBgr,
}

/// <summary>
/// Everything the stream description says about how a HuffYUV frame is laid out.
/// </summary>
/// <remarks>
/// Three header forms, and which one a file uses is decided by two bytes rather than by a version
/// field. A stream with no description at all is the original codec, whose settings are implied by
/// the depth alone. A four-byte description whose last byte is zero is the second form, which states
/// a predictor, a bit depth for the bitstream and whether the frames are interlaced. A four-byte
/// description whose last byte is one is the third, where the second byte stops being a bit count
/// and becomes a sample depth with the chroma subsampling packed into its low nibble, and the third
/// byte gains flags for what the planes are.
/// <para/>
/// The three coexist because the codec grew: <c>HFYU</c> is the original and <c>FFVH</c> its
/// extension, and a file of either code may carry any of the three descriptions. Deciding on the
/// code rather than on the bytes would refuse half of the files each writes.
/// </remarks>
internal sealed class HuffYuvFormat {

  private HuffYuvFormat() { }

  internal int Version { get; private init; }
  internal HuffYuvPredictor Predictor { get; private init; }

  /// <summary>Whether the colour planes are stored as differences from green.</summary>
  internal bool Decorrelate { get; private init; }

  /// <summary>Whether a frame is two fields, so that a row's neighbour above is two rows up.</summary>
  internal bool Interlaced { get; private init; }

  /// <summary>Whether each frame carries its own Huffman tables in front of its picture.</summary>
  internal bool TablesPerFrame { get; private init; }

  internal HuffYuvColourSpace ColourSpace { get; private init; }
  internal int BitsPerSample { get; private init; }
  internal int ChromaHorizontalShift { get; private init; }
  internal int ChromaVerticalShift { get; private init; }
  internal bool HasAlpha { get; private init; }

  /// <summary>How many bits a pixel of the packed forms occupies: 12, 16, 24 or 32.</summary>
  internal int BitstreamBitsPerPixel { get; private init; }

  /// <summary>How many Huffman tables the stream carries, which is one per plane it codes.</summary>
  internal int TableCount => this.ColourSpace switch {
    HuffYuvColourSpace.Grey => 1,
    HuffYuvColourSpace.PackedBgr => 3,
    _ => this.HasAlpha ? 4 : 3,
  };

  /// <summary>
  /// Reads the description a container carried across, or works out what a stream without one is.
  /// </summary>
  /// <param name="extra">The bytes after the <c>BITMAPINFOHEADER</c>, which may be empty.</param>
  /// <param name="bitsPerPixel">The depth the header states, used where the description does not.</param>
  internal static HuffYuvFormat Parse(ReadOnlySpan<byte> extra, int bitsPerPixel, int streamIndex) {
    if (extra.Length < 4)
      return _Original(bitsPerPixel, streamIndex);

    return extra[3] == 1 ? _Planar(extra, streamIndex) : _Packed(extra, bitsPerPixel, streamIndex);
  }

  /// <summary>
  /// The original codec, which states nothing and is read off the depth alone.
  /// </summary>
  /// <remarks>
  /// Its predictor is neither of the two the later forms name. Version 1 files predict from the
  /// left, but with the first row of the picture handled as the rest are rather than specially, and
  /// nothing here has a file of that vintage to check against — so it is refused by name instead of
  /// being read as the nearest thing that does exist.
  /// </remarks>
  private static HuffYuvFormat _Original(int bitsPerPixel, int streamIndex)
    => throw new NotSupportedException(
      $"Video stream {streamIndex} is coded by the original HuffYUV at {bitsPerPixel} bits per pixel and carries no stream description. That version's frames say nothing about how they were predicted and nothing here can be checked against a file of it, so it is refused rather than read as the later version it resembles.");

  /// <summary>The second form: a predictor, a bitstream depth and the interlacing.</summary>
  private static HuffYuvFormat _Packed(ReadOnlySpan<byte> extra, int bitsPerPixel, int streamIndex) {
    var method = extra[0];
    var bitstreamBpp = extra[1] != 0 ? extra[1] : bitsPerPixel & ~7;

    var colourSpace = bitstreamBpp switch {
      12 or 16 => HuffYuvColourSpace.Yuv,
      24 or 32 => HuffYuvColourSpace.PackedBgr,
      _ => throw new NotSupportedException(
        $"Video stream {streamIndex} states {bitstreamBpp} bits a pixel in its bitstream, which is not one of the four HuffYUV codes samples at: 12 and 16 for 4:2:0 and 4:2:2, 24 and 32 for colour a pixel at a time."),
    };

    return new() {
      Version = 2,
      Predictor = _PredictorOf(method & 63, streamIndex),
      Decorrelate = (method & 64) != 0,
      Interlaced = _InterlacedFrom(extra[2]),
      TablesPerFrame = (extra[2] & 0x40) != 0,
      ColourSpace = colourSpace,
      BitsPerSample = 8,
      BitstreamBitsPerPixel = bitstreamBpp,
      ChromaHorizontalShift = bitstreamBpp == 12 ? 1 : 1,
      ChromaVerticalShift = bitstreamBpp == 12 ? 1 : 0,
      HasAlpha = bitstreamBpp == 32,
    };
  }

  /// <summary>
  /// The third form, where the planes are coded one after another rather than interleaved.
  /// </summary>
  /// <remarks>
  /// The second byte carries two things at once: the sample depth less one in its high nibble, and
  /// the chroma subsampling in its low one — the horizontal shift in the bottom two bits and the
  /// vertical shift in the two above them. That is how 4:4:4 comes out as <c>0x70</c>, 4:1:1 as
  /// <c>0x72</c>, 4:4:0 as <c>0x74</c>, 4:2:0 as <c>0x75</c> and 4:1:0 as <c>0x7A</c>, all at eight
  /// bits; it was read off streams ffmpeg was asked to write in each of those samplings.
  /// <para/>
  /// The third byte says what the planes are: one bit for chrominance present, one for the three
  /// planes being green, blue and red rather than luminance and chrominance, and one for an alpha
  /// plane after them.
  /// </remarks>
  private static HuffYuvFormat _Planar(ReadOnlySpan<byte> extra, int streamIndex) {
    var method = extra[0];
    var flags = extra[2];
    var chroma = (flags & 1) != 0;
    var rgb = (flags & 2) != 0;

    var bits = (extra[1] >> 4) + 1;
    if (bits != 8)
      throw new NotSupportedException(
        $"Video stream {streamIndex} carries {bits}-bit samples. Only eight-bit HuffYUV is read here; the deeper samplings code two bytes a sample and nothing here has been measured against one.");

    return new() {
      Version = 3,
      Predictor = _PredictorOf(method & 63, streamIndex),
      Decorrelate = (method & 64) != 0,
      Interlaced = _InterlacedFrom(flags),
      TablesPerFrame = (flags & 0x40) != 0,
      ColourSpace = rgb ? HuffYuvColourSpace.PlanarRgb : chroma ? HuffYuvColourSpace.Yuv : HuffYuvColourSpace.Grey,
      BitsPerSample = bits,
      ChromaHorizontalShift = extra[1] & 3,
      ChromaVerticalShift = (extra[1] >> 2) & 3,
      HasAlpha = (flags & 4) != 0,
      BitstreamBitsPerPixel = 0,
    };
  }

  /// <summary>
  /// Whether frames are two fields, from the two bits that say so.
  /// </summary>
  /// <remarks>
  /// Two bits and three meanings: one says interlaced, two says progressive, and zero says the
  /// writer stated nothing. The original codec decided by height — anything taller than 288 rows was
  /// a pair of fields — and a writer that says nothing is asking for that guess to be made. It is
  /// refused instead, because the guess is wrong for every progressive picture taller than a PAL
  /// field and being wrong about it puts every other row of a frame in the wrong place.
  /// </remarks>
  private static bool _InterlacedFrom(byte flags) => ((flags >> 4) & 3) == 1;

  private static HuffYuvPredictor _PredictorOf(int method, int streamIndex) => method switch {
    0 => HuffYuvPredictor.Left,
    1 => HuffYuvPredictor.Gradient,
    2 => HuffYuvPredictor.Median,
    _ => throw new NotSupportedException(
      $"Video stream {streamIndex} names prediction method {method}, which is not one of the three HuffYUV codes with: left, gradient and median."),
  };

  /// <summary>Refuses a description whose interlacing the writer left for a reader to guess.</summary>
  internal static void RefuseUnstatedInterlacing(ReadOnlySpan<byte> extra, int height, int streamIndex) {
    if (extra.Length < 4 || ((extra[2] >> 4) & 3) != 0)
      return;

    throw new NotSupportedException(
      $"Video stream {streamIndex} states neither that its frames are interlaced nor that they are not. The original codec guessed from the height — {height} rows here — and the guess puts every other row of a progressive picture in the wrong place whenever it is wrong, so it is refused rather than made.");
  }
}
