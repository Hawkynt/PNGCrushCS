using System;
using System.IO;

namespace FileFormat.Codecs.CineForm;

/// <summary>
/// Decodes one channel's ten subbands and reconstructs its component array.
/// </summary>
/// <remarks>
/// <b>None of this is the free standard's outer framing.</b> SMPTE ST 2073-1:2017 (VC-5) describes a
/// bitstream that opens with a four-byte start marker and whose header carries only the tag-value
/// pairs Table B.2 names. A real file from ffmpeg's own <c>cfhd</c> encoder does neither: it has no
/// start marker at all, and its header and every subband are threaded through with dozens of tag
/// numbers Table B.2 does not define. GoPro's own documentation calls VC-5 a cleaned-up, published
/// subset of the older CineForm engine, and that is exactly the shape of what a real file contains —
/// the tag-value and chunk mechanics VC-5 specifies, carrying a bitstream that predates it.
/// <para/>
/// What makes this readable at all despite that: Section 8.3.1 states a tag-value pair is one segment
/// whatever its tag, defined or not, so every tag this class does not name is skipped as four bytes
/// rather than decoded. What is not skippable — where a channel's lowpass and each of its nine
/// highpass codeblocks begin — was found by building minimal files with ffmpeg's own encoder
/// (<c>ffmpeg -f lavfi -i color=...:size=64x48 -pix_fmt yuv422p10le -c:v cfhd</c>, and the same
/// pattern for <c>gbrp12le</c>) and a solid-colour frame in particular, whose highpass bands are all
/// but empty and so make the codeblock boundaries easy to see by eye, then confirming each boundary by
/// brute-forcing the byte offset a highpass codeblock's entropy decode terminates cleanly on the band
/// end marker at — the strongest evidence this format offers, since a wrong offset or a wrong band
/// size overwhelmingly fails to produce a clean decode at all. Measured facts, and how each was
/// reached:
/// <list type="bullet">
/// <item>A highpass codeblock's entropy-coded data begins immediately after the fixed run of header
/// tags that ends with tag 55 — no marker, no gap — confirmed by that brute-force search landing on
/// exactly that byte for every one of nine subbands across three channels of a 64x48 test file, with
/// the whole 6960-byte packet consumed to the last byte and nothing left over.</item>
/// <item>The lowpass codeblock is different: one unexplained four-byte segment (tag 4, a value that
/// stayed identical across files of entirely different content, so it reads as a marker rather than
/// data) sits between the tag that states <see cref="CineFormTags.LowpassPrecision"/> and the raw
/// coefficients. It is skipped by position rather than understood.</item>
/// <item>Tags 27/28 state the lowpass band's own width and height, and tags 49/50 do the same for each
/// highpass subband — the picture's own dimensions, not the width the entropy coder actually laid the
/// row out to. Whenever that stated width is not already a whole multiple of eight, the coded row is
/// padded out to the next one, that many real coefficients followed by zeros to fill it, on every
/// subband of every channel measured where the stated width fell short of a multiple of eight — a
/// luma channel's own coarsest level as much as a horizontally-subsampled chroma channel's, whenever
/// the picture is not a whole multiple of sixty-four wide. It is not a doubling: a stated width of ten
/// pads to sixteen, not twenty. <see cref="Parse"/> therefore tries the stated width first and, only on
/// a failed decode, retries at the next multiple of eight and keeps just the stated number of columns
/// of each row — what a decoder relying on the entropy coder's own self-termination to catch a wrong
/// guess is supposed to do, rather than assuming the padding by name.</item>
/// <item>Channel order and identity were read off the lowpass band alone, by encoding solid red, green
/// and blue frames and comparing each channel's constant lowpass value — after undoing the prescale
/// shift below — against ffmpeg's own raw decode of the same frame: <c>yuv422p10le</c> codes channel 0
/// as luma, channel 1 as <b>V</b> and channel 2 as <b>U</b> (chroma in that order, not the U-then-V a
/// container's own track order would suggest); <c>gbrp12le</c> and <c>gbrap12le</c> code channel 0 as
/// green, channel 1 as red and channel 2 as blue, matching neither ffmpeg's own <c>gbrp</c> plane order
/// nor any order this decoder guessed at.</item>
/// </list>
/// <para/>
/// <b>The prescale shifts are not stated in the files measured either, and Table E.1's own ten-bit
/// pair is not where the real encoder puts them.</b> Table B.2's own default is (0,0,0) and no file
/// measured carries a tag 109 overriding it. At twelve bits, (0,2,2) — Annex E.1's own value —
/// reconstructs every subband against a forward transform of ffmpeg's own decoded reference to within
/// single digits. At ten bits it does not: (0,0,2), Annex E.1's stated value, leaves level 2's highpass
/// four times too large while level 1 and level 3 already agree, and moving that shift from level 3 to
/// level 2 — (0,2,0) — is what brings the whole channel into agreement; see
/// <see cref="CineFormPrescale"/>'s remarks for the measurement. Annex E.1 calls these shifts something
/// an encoder "can benefit from" rather than something it must state, and the real encoder evidently
/// applies its own choice of them without ever writing tag 109 to say so.
/// </remarks>
internal static class CineFormChannelDecoder {

  /// <summary>
  /// One channel's dequantised coefficients, entropy-decoded but not yet run through the inverse
  /// wavelet transform — deliberately kept apart from reconstruction, because which
  /// <see cref="CineFormPrescale"/> table applies is only knowable once every channel's lowpass width
  /// has been compared against the first channel's, and that comparison needs every channel parsed
  /// first.
  /// </summary>
  internal sealed class ParsedChannel {
    internal required int[] Lowpass { get; init; }
    internal required int LowpassWidth { get; init; }
    internal required int LowpassHeight { get; init; }
    internal required int[][][] HighpassByLevel { get; init; } // [0..2 for levels 3,2,1][0..2 for LH,HL,HH]
  }

  /// <summary>Table 1: which wavelet level each position within the three highpass groups belongs to,
  /// coarsest first — but see the remarks above on why this decoder reads a subband's own width and
  /// height from the bitstream rather than deriving them from the level alone.</summary>
  private static readonly int[] _LevelOrder = [3, 3, 3, 2, 2, 2, 1, 1, 1];

  /// <summary>
  /// Parses one channel's ten subbands starting at <paramref name="position"/>, dequantising every
  /// highpass coefficient but not yet reconstructing, and advances <paramref name="position"/> to
  /// immediately after its last codeblock.
  /// </summary>
  internal static ParsedChannel Parse(ReadOnlyMemory<byte> data, ref int position) {

    var span = data.Span;

    var lowpassWidth = 0;
    var lowpassHeight = 0;
    var lowpassPrecision = 16;
    var highpassWidth = 0;
    var highpassHeight = 0;
    var subbandNumber = 0;
    var quantization = 1;

    int[]? lowpass = null;
    var highpassByLevel = new int[3][][]; // [level index 0..2 for levels 3,2,1][subband 0..2 for LH,HL,HH]

    var subbandIndex = 0;
    var n = span.Length;

    while (position + 4 <= n) {
      var b0 = span[position];
      var b1 = span[position + 1];
      var b2 = span[position + 2];
      var b3 = span[position + 3];
      var tag16 = (b0 << 8) | b1;
      var tag = tag16 >= 0x8000 ? tag16 - 0x10000 : tag16;
      var value = (b2 << 8) | b3;
      position += 4;

      if (tag == CineFormTags.ChannelNumber && lowpass != null)
        return new() { Lowpass = lowpass, LowpassWidth = lowpassWidth, LowpassHeight = lowpassHeight, HighpassByLevel = highpassByLevel };

      switch (tag) {
        case CineFormTags.LowpassWidth: lowpassWidth = value; continue;
        case CineFormTags.LowpassHeight: lowpassHeight = value; continue;
        case CineFormTags.HighpassWidth: highpassWidth = value; continue;
        case CineFormTags.HighpassHeight: highpassHeight = value; continue;
        case CineFormTags.SubbandNumber: subbandNumber = value; continue;
        case CineFormTags.Quantization: quantization = value; continue;
        case CineFormTags.LowpassPrecision: {
          lowpassPrecision = value;
          // One unexplained four-byte marker segment sits between this tag and the raw coefficients —
          // see the class remarks on how that was measured.
          if (position + 4 > n)
            throw new InvalidDataException("A CineForm channel's lowpass precision tag is not followed by the marker segment this format's real files always carry.");

          position += 4;
          lowpass = _ReadLowpass(span, ref position, lowpassWidth, lowpassHeight, lowpassPrecision);
          subbandIndex = 0;
          continue;
        }
        case CineFormTags.HighpassDataFollows: {
          if (subbandIndex >= _LevelOrder.Length)
            throw new InvalidDataException("A CineForm channel codes more than nine highpass subbands, which this decoder does not expect.");

          var levelIndex = subbandIndex / 3; // 0 for level3 (coarsest, first in bitstream order), 1 for level2, 2 for level1
          highpassByLevel[levelIndex] ??= new int[3][];
          var bandInLevel = subbandNumber is >= 1 and <= 3 ? subbandNumber - 1 : throw new InvalidDataException(
            $"A CineForm highpass subband states SubbandNumber {subbandNumber}, which is outside the 1 to 3 range every measured file uses within a wavelet level.");

          var coefficients = _ReadHighpass(data, ref position, highpassWidth, highpassHeight);
          CineFormWavelet.Dequantize(coefficients, quantization);
          highpassByLevel[levelIndex][bandInLevel] = coefficients;
          ++subbandIndex;
          continue;
        }
      }
    }

    if (lowpass == null)
      throw new InvalidDataException("A CineForm channel ended before its lowpass band was ever coded.");

    return new() { Lowpass = lowpass, LowpassWidth = lowpassWidth, LowpassHeight = lowpassHeight, HighpassByLevel = highpassByLevel };
  }

  private static int[] _ReadLowpass(ReadOnlySpan<byte> data, ref int position, int width, int height, int precision) {
    if (precision != 16)
      throw new NotSupportedException($"A CineForm lowpass band states {precision} bits per coefficient; only 16, the value every measured file carries, is read.");

    if (width <= 0 || height <= 0)
      throw new InvalidDataException($"A CineForm lowpass band states {width}x{height} coefficients, which is not a picture.");

    var byteLength = (long)width * height * 2;
    if (position + byteLength > data.Length)
      throw new InvalidDataException(
        $"A CineForm lowpass band of {width}x{height} coefficients needs {byteLength} bytes at position {position}, and the packet holds {data.Length}.");

    var count = width * height;
    var result = new int[count];
    for (var i = 0; i < count; ++i) {
      var at = position + i * 2;
      result[i] = (data[at] << 8) | data[at + 1];
    }

    position = (position + (int)byteLength + 3) & ~3;
    return result;
  }

  private static int[] _ReadHighpass(ReadOnlyMemory<byte> data, ref int position, int statedWidth, int statedHeight) {
    if (statedWidth <= 0 || statedHeight <= 0 || (long)statedWidth * statedHeight > data.Length * 8L)
      throw new InvalidDataException(
        $"A CineForm highpass subband states {statedWidth}x{statedHeight} coefficients, which cannot fit in a packet of {data.Length} bytes.");

    var reader = new CineFormBitReader(data, position);
    if (_TryDecodeBand(reader, statedWidth, statedHeight, out var coefficients)) {
      position = (reader.ByteBoundaryPosition + 3) & ~3;
      return coefficients;
    }

    // A row that is not already a whole multiple of eight coefficients is coded padded out to the
    // next one, the padding always decoding to zero. Measured across every subband of every
    // geometry built for this decoder — not only the horizontally-subsampled chroma channels, where
    // the padding happens to coincide with doubling because their un-padded width is itself already
    // half of a multiple of eight, but a luma channel's own coarsest level whenever the picture's
    // width is not a whole multiple of sixty-four. Table 8.4's own restriction that a highpass
    // codeblock be padded to a segment boundary governs the codeblock's end and says nothing about
    // its rows; this padding is a fact about the row layout the entropy coder itself imposes, not one
    // this specification states.
    var paddedWidth = (statedWidth + 7) / 8 * 8;
    var retryReader = new CineFormBitReader(data, position);
    if (!_TryDecodeBand(retryReader, paddedWidth, statedHeight, out var padded))
      throw new InvalidDataException(
        $"A CineForm highpass subband of {statedWidth}x{statedHeight} coefficients does not entropy-decode cleanly to its band end marker, at the stated width or at {paddedWidth}, the next multiple of eight.");

    var stripped = new int[statedWidth * statedHeight];
    for (var y = 0; y < statedHeight; ++y)
      Array.Copy(padded, y * paddedWidth, stripped, y * statedWidth, statedWidth);

    position = (retryReader.ByteBoundaryPosition + 3) & ~3;
    return stripped;
  }

  private static bool _TryDecodeBand(CineFormBitReader reader, int width, int height, out int[] coefficients) {
    var count = width * height;
    coefficients = new int[count];
    var index = 0;

    while (index < count) {
      if (!CineFormCodebook.TryDecodeRun(reader, out var runCount, out var value))
        return false;

      if (value == CineFormCodebook.BandEndMarkerValue)
        return false; // the band ended before all coefficients were accounted for

      if (value == 0) {
        if (index + runCount > count)
          return false;

        index += runCount; // coefficients[] is already zero-initialised
        continue;
      }

      var sign = reader.Peek(1);
      reader.Advance(1);
      coefficients[index++] = sign != 0 ? -value : value;
    }

    if (!CineFormCodebook.TryDecodeRun(reader, out var endCount, out var endValue))
      return false;

    return endValue == CineFormCodebook.BandEndMarkerValue && endCount == 0;
  }

  /// <summary>
  /// Applies the inverse spatial wavelet transform through all three levels, with the prescale shifts
  /// Section 11.3 states between them, to reconstruct one channel's component array.
  /// </summary>
  internal static int[] Reconstruct(ParsedChannel channel, ReadOnlySpan<int> prescaleShift, out int outputWidth, out int outputHeight) {
    var current = channel.Lowpass;
    var currentWidth = channel.LowpassWidth;
    var currentHeight = channel.LowpassHeight;
    var highpassByLevel = channel.HighpassByLevel;

    for (var levelIndex = 0; levelIndex < 3; ++levelIndex) {
      var bands = highpassByLevel[levelIndex]
        ?? throw new InvalidDataException("A CineForm channel is missing one of its three wavelet levels of highpass subbands.");

      var lh = bands[0] ?? throw new InvalidDataException("A CineForm channel's LH highpass subband was never coded.");
      var hl = bands[1] ?? throw new InvalidDataException("A CineForm channel's HL highpass subband was never coded.");
      var hh = bands[2] ?? throw new InvalidDataException("A CineForm channel's HH highpass subband was never coded.");

      current = CineFormWavelet.InverseSpatial(current, lh, hl, hh, currentWidth, currentHeight, out currentWidth, out currentHeight);

      // 11.3 step 2/4/6: PrescaleShift[2] after level 3's inverse, [1] after level 2's, [0] after level 1's.
      var shift = prescaleShift[2 - levelIndex];
      if (shift != 0)
        for (var i = 0; i < current.Length; ++i)
          current[i] <<= shift;
    }

    outputWidth = currentWidth;
    outputHeight = currentHeight;
    return current;
  }
}
