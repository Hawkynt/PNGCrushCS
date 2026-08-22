using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.Codecs;

/// <summary>
/// Decodes Creative YUV (<c>cyuv</c>): 4:1:1 samples coded as differences along each row, where every
/// difference is a four-bit index into one of three sixteen-entry signed tables the frame itself
/// carries.
/// </summary>
/// <remarks>
/// Intra only, and the whole of the coding is one running sum per component: a sample is the sample
/// before it on the same row plus the table entry its index names. There is no transform, no
/// quantiser in the decoder, no run-length coding, no escape and nothing carried between frames. The
/// tables are per frame rather than per stream, so a packet is self-contained.
/// <para/>
/// <b>Two documents describe this format and they agree.</b> Mike Melanson's <i>Simple YUV Coding
/// Formats</i> and Dr. Tim Ferguson's <c>cyuv.txt</c>, written at Monash University and mirrored at
/// <c>multimedia.cx/mirror/cyuv.txt</c> once its own address stopped resolving. The two are not fully
/// independent — Melanson's write-up cites Ferguson's as its source — but they check each other
/// arithmetically: Ferguson states the coded picture's size as <c>width * height * 6 / 8</c> bytes, and
/// Melanson's byte layout reduces to exactly that, six nibbles carrying four luminance samples and one
/// of each chrominance. Both figures are confirmed against the file: every one of 150 packets of
/// <c>samples.ffmpeg.org/V-codecs/CYUV/cyuv.avi</c> is exactly 19,056 bytes, which is the 48-byte
/// table block plus 176 * 144 * 6 / 8.
/// <para/>
/// <b>One thing measurement decided against the documentation.</b> Both documents give the third byte
/// of a group as its high nibble naming the third luminance sample's index and its low nibble the
/// fourth. It is the other way round. Read as written, every fourth luminance sample still comes out
/// right — a running sum does not care which order two differences are added in — and only the sample
/// between them is wrong, which is precisely the shape the measurement showed: 3,996 of 25,344
/// luminance samples differing, every one of them at a column congruent to two modulo four, with both
/// chrominance planes already exact. Swapping the two nibbles brings the frame to zero differences.
/// That is a difference no picture would announce, since the wrong sample is a plausible value
/// between its two correct neighbours.
/// <para/>
/// <b>A seed nibble is the top four bits of its sample</b>, so it widens by shifting rather than by
/// repeating the pattern. Measured the same way: shifting leaves frame 0 with 3,996 differing samples,
/// all of them the one nibble above, where repeating leaves 37,725 of 38,016.
/// <para/>
/// <b>The format has a second, uncompressed shape</b>, and which one a packet is, is read off its own
/// length rather than from any field — neither the stream description nor the packet states it. A
/// packet as long as the picture's own packed 4:2:2 byte count is samples in U, Y, V, Y order with no
/// table block and no prediction, stored bottom row first, the Windows bitmap convention this
/// package's other AVI codecs already carry; the coded shape's rows run top-down. Both spellings are
/// real: <c>samples.ffmpeg.org/V-codecs/CYUV.AVI</c> is 320x240 and carries the uncompressed one in
/// every one of its 14 packets, and both files state <c>cyuv</c> and sixteen bits a pixel in the same
/// header fields, so the length is the only thing that separates them.
/// <para/>
/// <b>Measured against ffmpeg</b> on the coded samples themselves and not through an RGB conversion,
/// since this is a subsampled format and the chroma-siting convention would otherwise be inside the
/// comparison. The coded shape: 150 frames of 176x144, compared plane by plane against
/// <c>ffmpeg -threads 1 -i cyuv.avi -fps_mode passthrough -f rawvideo -pix_fmt yuv411p</c>, every
/// sample of all three planes identical on every frame — max delta 0, nothing drifting across the run.
/// The uncompressed shape: 14 frames of 320x240 against the same command at <c>-pix_fmt uyvy422</c>,
/// identical as well. The RGB picture <see cref="TryDecode"/> hands back converts with BT.601
/// coefficients and repeats each chrominance sample across the four luminance samples it covers; that
/// is a display convenience the measurement above does not run through.
/// <para/>
/// <b>Whether a sum that leaves the byte range wraps or saturates is not settled here</b>, because
/// nothing in either corpus leaves it: swept over all 150 coded frames, no running sum of any of the
/// three components ever goes below zero or above 255, so both readings reproduce every measured frame
/// exactly. Byte arithmetic is what is implemented, which is what this package's other prediction-coded
/// lossless codecs do, and it is recorded here as a choice the files did not decide rather than as a
/// fact they did.
/// <para/>
/// <b>What refuses.</b> A picture whose width is not a whole number of four-sample groups, since that
/// is the unit both the coded rows and the chrominance are built from; a packet whose length is neither
/// of the two shapes above, rather than one of them decoded partway; and a picture size the stream
/// states as nothing. There is no <c>catch</c> here handing back a blank frame or repeating the last
/// one.
/// </remarks>
public sealed class CreativeYuvVideoDecoder : IVideoCodecDecoder<CreativeYuvVideoDecoder> {

  /// <summary>The three sixteen-entry signed tables at the head of a coded packet.</summary>
  private const int _TABLE_BYTES = 48;

  private static readonly CodecTag _Tag = CodecTag.FromCharacters("cyuv");

  private readonly int _width;
  private readonly int _height;
  private readonly int _streamIndex;

  private CreativeYuvVideoDecoder(int width, int height, int streamIndex) {
    this._width = width;
    this._height = height;
    this._streamIndex = streamIndex;
  }

  public static string CodecName => "Creative YUV (CYUV)";

  public static bool Accepts(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);

    return stream.Kind == MediaStreamKind.Video && stream.Codec.EqualsIgnoringCase(_Tag);
  }

  public static CreativeYuvVideoDecoder Create(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);

    if (stream.Width <= 0 || stream.Height <= 0)
      throw new InvalidDataException(
        $"Video stream {stream.Index} states a picture size of {stream.Width}x{stream.Height}, which no frame can "
        + "be decoded into.");

    if ((stream.Width & 3) != 0)
      throw new NotSupportedException(
        $"Video stream {stream.Index} states a width of {stream.Width}. Creative YUV codes four luminance samples "
        + "to one chrominance pair and writes them three bytes at a time, so a width that is not a whole number of "
        + "four-sample groups has no reading.");

    return new(stream.Width, stream.Height, stream.Index);
  }

  /// <summary>
  /// Decodes one packet, which for this codec is always exactly one whole picture in one of the
  /// format's two shapes.
  /// </summary>
  public bool TryDecode(CodedPacket packet, out RawImage frame) {
    var data = packet.Data.Span;
    var coded = _TABLE_BYTES + this._width * this._height * 6 / 8;
    var packed = this._width * this._height * 2;

    if (data.Length == coded) {
      var planes = this.DecodeCodedPlanes(data);
      frame = new() {
        Width = this._width,
        Height = this._height,
        Format = PixelFormat.Rgb24,
        PixelData = this._PlanesToRgb24(planes[0], planes[1], planes[2], this._width >> 2),
      };

      return true;
    }

    if (data.Length == packed) {
      frame = new() {
        Width = this._width,
        Height = this._height,
        Format = PixelFormat.Rgb24,
        PixelData = this._PackedToRgb24(this.DecodePackedSamples(data)),
      };

      return true;
    }

    throw new InvalidDataException(
      $"Video stream {this._streamIndex} carries a Creative YUV packet of {data.Length} byte(s). A "
      + $"{this._width}x{this._height} picture is {coded} byte(s) coded or {packed} uncompressed, and nothing in "
      + "the format states which shape a packet is other than its own length.");
  }

  // ============================================================================================
  // The coded shape
  // ============================================================================================

  /// <summary>
  /// Turns a coded packet into 4:1:1 planes — luminance, then the two chrominance planes at a quarter
  /// the width — from the 48-byte table block and three bytes for every four pixels, rows top-down.
  /// </summary>
  /// <remarks>
  /// A row opens with three bytes that are not a group like the others. They carry the row's own
  /// starting luminance and chrominance — each the top four bits of its sample, so the row's prediction
  /// restarts rather than carrying on from the row above — and then the indices for the second, third
  /// and fourth luminance samples, which is what makes the first four pixels cost the same three bytes
  /// every later four do.
  /// <para/>
  /// The tables are documented as signed and are added here as plain bytes, which is the same
  /// arithmetic: a running sum kept in a byte cannot tell an entry of 200 from one of -56, the two
  /// being congruent modulo 256, and the sum is a byte because the samples are.
  /// <para/>
  /// This is the seam the measurement is taken at. The planes are what ffmpeg's own decode produces
  /// for this codec, so they are compared directly against it, where the colour picture
  /// <see cref="TryDecode"/> composes has a chroma-siting convention in it that is no part of the
  /// decode.
  /// </remarks>
  internal byte[][] DecodeCodedPlanes(ReadOnlySpan<byte> data) {
    var width = this._width;
    var height = this._height;
    var groups = width >> 2;

    var luma = new byte[width * height];
    var cb = new byte[groups * height];
    var cr = new byte[groups * height];

    var lumaDeltas = data[..16];
    var cbDeltas = data.Slice(16, 16);
    var crDeltas = data.Slice(32, 16);
    var rows = data[_TABLE_BYTES..];
    var rowBytes = groups * 3;

    for (var y = 0; y < height; ++y) {
      var row = rows.Slice(y * rowBytes, rowBytes);
      var lumaAt = y * width;
      var chromaAt = y * groups;

      // The row's three opening bytes: its own starting samples, then three luminance indices.
      var first = row[0];
      var second = row[1];
      var third = row[2];

      var u = (byte)((first >> 4) << 4);
      var l = (byte)((first & 0x0F) << 4);
      var v = (byte)((second >> 4) << 4);

      cb[chromaAt] = u;
      cr[chromaAt] = v;
      luma[lumaAt] = l;

      l = (byte)(l + lumaDeltas[second & 0x0F]);
      luma[lumaAt + 1] = l;
      l = (byte)(l + lumaDeltas[third & 0x0F]);
      luma[lumaAt + 2] = l;
      l = (byte)(l + lumaDeltas[third >> 4]);
      luma[lumaAt + 3] = l;

      for (var g = 1; g < groups; ++g) {
        var at = g * 3;
        var a = row[at];
        var b = row[at + 1];
        var c = row[at + 2];

        u = (byte)(u + cbDeltas[a >> 4]);
        cb[chromaAt + g] = u;
        l = (byte)(l + lumaDeltas[a & 0x0F]);
        luma[lumaAt + (g << 2)] = l;

        v = (byte)(v + crDeltas[b >> 4]);
        cr[chromaAt + g] = v;
        l = (byte)(l + lumaDeltas[b & 0x0F]);
        luma[lumaAt + (g << 2) + 1] = l;

        l = (byte)(l + lumaDeltas[c & 0x0F]);
        luma[lumaAt + (g << 2) + 2] = l;
        l = (byte)(l + lumaDeltas[c >> 4]);
        luma[lumaAt + (g << 2) + 3] = l;
      }
    }

    return [luma, cb, cr];
  }

  // ============================================================================================
  // The uncompressed shape
  // ============================================================================================

  /// <summary>
  /// Reads a packet that is the picture itself, packed 4:2:2 in U, Y, V, Y order, and hands back those
  /// same samples turned the right way up.
  /// </summary>
  /// <remarks>
  /// Only the row order changes here — the samples themselves are the packet's own bytes. The picture
  /// is stored bottom row first, which is the Windows bitmap convention rather than anything this codec
  /// states, and is what separates this shape from the coded one whose rows run top-down.
  /// </remarks>
  internal byte[] DecodePackedSamples(ReadOnlySpan<byte> data) {
    var height = this._height;
    var stride = this._width * 2;
    var samples = new byte[stride * height];

    for (var y = 0; y < height; ++y)
      data.Slice((height - 1 - y) * stride, stride).CopyTo(samples.AsSpan(y * stride));

    return samples;
  }

  // ============================================================================================
  // What comes out
  // ============================================================================================

  /// <summary>
  /// Turns the 4:1:1 planes into the packed colour every reader here hands back, repeating each
  /// chrominance sample across the four luminance samples it covers.
  /// </summary>
  private byte[] _PlanesToRgb24(byte[] luma, byte[] cb, byte[] cr, int groups) {
    var width = this._width;
    var height = this._height;
    var rgb = new byte[width * height * 3];

    for (var y = 0; y < height; ++y) {
      var lumaAt = y * width;
      var chromaAt = y * groups;
      var target = y * width * 3;

      for (var x = 0; x < width; ++x) {
        var at = chromaAt + (x >> 2);
        _WritePixel(rgb, target, luma[lumaAt + x], cb[at], cr[at]);
        target += 3;
      }
    }

    return rgb;
  }

  /// <summary>Converts upright packed 4:2:2 samples in U, Y, V, Y order to colour.</summary>
  private byte[] _PackedToRgb24(byte[] samples) {
    var width = this._width;
    var height = this._height;
    var rgb = new byte[width * height * 3];
    var pairs = width >> 1;

    for (var y = 0; y < height; ++y) {
      var source = y * width * 2;
      var target = y * width * 3;

      for (var p = 0; p < pairs; ++p) {
        var at = source + p * 4;
        _WritePixel(rgb, target, samples[at + 1], samples[at], samples[at + 2]);
        _WritePixel(rgb, target + 3, samples[at + 3], samples[at], samples[at + 2]);
        target += 6;
      }
    }

    return rgb;
  }

  private static void _WritePixel(byte[] rgb, int at, byte luma, byte cb, byte cr) {
    var c = luma - 16;
    var d = cb - 128;
    var e = cr - 128;

    rgb[at] = _Clamp((298 * c + 409 * e + 128) >> 8);
    rgb[at + 1] = _Clamp((298 * c - 100 * d - 208 * e + 128) >> 8);
    rgb[at + 2] = _Clamp((298 * c + 516 * d + 128) >> 8);
  }

  private static byte _Clamp(int value) => (byte)(value < 0 ? 0 : value > 255 ? 255 : value);
}
