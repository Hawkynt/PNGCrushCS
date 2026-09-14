from pathlib import Path


def replace_once(text: str, old: str, new: str, label: str) -> str:
    count = text.count(old)
    if count != 1:
        raise RuntimeError(f"{label}: expected one match, found {count}")
    return text.replace(old, new, 1)


root = Path(__file__).resolve().parents[3]
encoder_path = root / "Hawkynt.FileFormats.Video/Codecs/MsMpeg4/MsMpeg4PictureEncoder.cs"
encoder = encoder_path.read_text(encoding="utf-8")

encoder = replace_once(
    encoder,
    "  private const int _MAX_VECTOR_DIFFERENCE = 31;\n\n  /// <summary>Which of three run-level tables version 3 is told to use: the pair versions 1 and 2 fix.</summary>",
    "  private const int _MAX_VECTOR_DIFFERENCE = 31;\n\n"
    "  /// <summary>Extra luma SAD temporal prediction must lose by before paying for an intra macroblock.</summary>\n"
    "  private const int _INTRA_MODE_BIAS_PER_QUANTISER = 64;\n\n"
    "  /// <summary>Which of three run-level tables version 3 is told to use: the pair versions 1 and 2 fix.</summary>",
    "mode-decision constant",
)

encoder = replace_once(
    encoder,
    "  private void _EncodeIntraMacroblock(int address) {",
    "  private void _EncodeIntraMacroblock(int address, bool inPredictedPicture = false) {",
    "intra encoder signature",
)
encoder = replace_once(
    encoder,
    "    this._WriteIntraMacroblockHeader(address, pattern);\n    this._WriteIntraBlocks(address, levels, pattern);",
    "    if (inPredictedPicture)\n"
    "      this._WritePredictedPictureIntraMacroblockHeader(pattern);\n"
    "    else\n"
    "      this._WriteIntraMacroblockHeader(address, pattern);\n\n"
    "    this._WriteIntraBlocks(address, levels, pattern);",
    "intra header dispatch",
)

marker = "  private void _WriteIntraBlocks(int address, scoped Span<int> levels, int pattern) {"
helper = '''  /// <summary>Writes an intra macroblock carried inside a predicted picture.</summary>
  private void _WritePredictedPictureIntraMacroblockHeader(int pattern) {
    var chroma = pattern & 3;
    var luminance = pattern >> 2;

    switch (this._version) {
      case MsMpeg4Version.Version3:
        // Bit six clear means intra in the P-picture table; the lower six bits are the direct CBP.
        this._writer.Write(
          MsMpeg4Data.MacroblockNonIntraCodes[pattern], MsMpeg4Data.MacroblockNonIntraLengths[pattern]);
        this._writer.Write(0, 1); // AC prediction off.
        return;

      case MsMpeg4Version.Version2: {
        var type = chroma | 4;
        this._writer.Write(MsMpeg4Data.V2MacroblockTypeCodes[type], MsMpeg4Data.V2MacroblockTypeLengths[type]);
        this._writer.Write(0, 1); // AC prediction off.
        this._writer.Write(
          MsMpeg4Data.CodedBlockPatternYCodes[luminance], MsMpeg4Data.CodedBlockPatternYLengths[luminance]);
        return;
      }

      default: {
        var type = chroma | 4;
        this._writer.Write(MsMpeg4Data.InterMacroblockCodes[type], MsMpeg4Data.InterMacroblockLengths[type]);
        var written = (pattern ^ 0x3C) >> 2;
        this._writer.Write(
          MsMpeg4Data.CodedBlockPatternYCodes[written], MsMpeg4Data.CodedBlockPatternYLengths[written]);
        return;
      }
    }
  }

'''
encoder = replace_once(encoder, marker, helper + marker, "P-picture intra header helper")

encoder = replace_once(
    encoder,
    "    var (vectorX, vectorY) = this._Search(address, predictedX, predictedY);",
    "    var (vectorX, vectorY, interDistortion) = this._Search(address, predictedX, predictedY);",
    "motion search result",
)
encoder = replace_once(
    encoder,
    "      vectorX = predictedX;\n      vectorY = predictedY;\n    }\n\n    Span<int> levels = stackalloc int[6 * 64];",
    "      vectorX = predictedX;\n"
    "      vectorY = predictedY;\n"
    "      interDistortion = this._Distortion(address, vectorX, vectorY);\n"
    "    }\n\n"
    "    if (this._ShouldEncodeIntra(address, interDistortion)) {\n"
    "      this._writer.Write(0, 1);\n"
    "      this._EncodeIntraMacroblock(address, inPredictedPicture: true);\n"
    "      return;\n"
    "    }\n\n"
    "    Span<int> levels = stackalloc int[6 * 64];",
    "P-picture mode decision",
)

encoder = replace_once(
    encoder,
    "  private (int X, int Y) _Search(int address, int predictedX, int predictedY) {",
    "  private (int X, int Y, int Distortion) _Search(int address, int predictedX, int predictedY) {",
    "search signature",
)
encoder = replace_once(
    encoder,
    "    return (bestX, bestY);\n  }\n\n  /// <summary>The absolute difference between a macroblock's luminance and what a vector predicts.</summary>",
    "    return (bestX, bestY, best);\n"
    "  }\n\n"
    "  /// <summary>Whether spatial coding beats the best legal temporal predictor by enough to pay for intra syntax.</summary>\n"
    "  private bool _ShouldEncodeIntra(int address, int interDistortion)\n"
    "    => interDistortion > this._IntraDistortion(address) + _INTRA_MODE_BIAS_PER_QUANTISER * this._quantiser;\n\n"
    "  /// <summary>Sum of absolute deviations from each 8x8 luminance block's mean.</summary>\n"
    "  private int _IntraDistortion(int address) {\n"
    "    Span<int> samples = stackalloc int[64];\n"
    "    var total = 0;\n\n"
    "    for (var index = 0; index < 4; ++index) {\n"
    "      this._Read(this._source, address, index, samples);\n\n"
    "      var sum = 0;\n"
    "      for (var i = 0; i < samples.Length; ++i)\n"
    "        sum += samples[i];\n\n"
    "      var mean = (sum + 32) >> 6;\n"
    "      for (var i = 0; i < samples.Length; ++i)\n"
    "        total += Math.Abs(samples[i] - mean);\n"
    "    }\n\n"
    "    return total;\n"
    "  }\n\n"
    "  /// <summary>The absolute difference between a macroblock's luminance and what a vector predicts.</summary>",
    "search distortion return and intra metric",
)
encoder_path.write_text(encoder, encoding="utf-8", newline="\n")

public_path = root / "Hawkynt.FileFormats.Video/Codecs/MsMpeg4VideoEncoder.cs"
public = public_path.read_text(encoding="utf-8")
old = '''/// <b>What it writes.</b> An intra picture every <see cref="_KEY_FRAME_INTERVAL"/> frames and a
/// predicted picture between them, one slice a picture, one motion vector a macroblock, the
/// alternating current prediction always off and — for version 3 — the middle run-level tables, the
/// first DC table and the first motion vector table. Every one of those is a choice the format leaves
/// open and none of them changes whether the result decodes; leaving them fixed is what makes the same
/// input produce the same bytes.
/// <para/>
/// <b>What it does not write.</b> No intra macroblock inside a predicted picture. The format has one
/// and this encoder never chooses it: a predicted picture that cannot predict is answered by the next
/// intra picture instead, which is a coarser answer and a much smaller decision surface. Nor does it
/// alternate the interpolation's rounding, which version 3 alone could.
'''
new = '''/// <b>What it writes.</b> An intra picture every <see cref="_KEY_FRAME_INTERVAL"/> frames and a
/// predicted picture between them, one slice a picture and one half-sample motion vector per inter
/// macroblock. A P-picture macroblock switches to the format's intra form when the best legal temporal
/// prediction is materially worse than coding its local spatial variation. Alternating-current
/// prediction remains off and version 3 uses the middle run-level tables, first DC table and first
/// motion-vector table so identical input still produces identical bytes.
/// <para/>
/// <b>Picture types and references.</b> Microsoft's versions 1 to 3 have I and P picture syntax only;
/// they have no B picture and therefore no future/backward reference queue to write. Version 3 can
/// alternate half-sample interpolation rounding; this encoder deliberately keeps that optional mode off.
'''
public = replace_once(public, old, new, "public encoder capability documentation")
public_path.write_text(public, encoding="utf-8", newline="\n")

test_path = root / "Tests/Hawkynt.FileFormats.Video.Tests/Codecs/MsMpeg4/MsMpeg4PredictedIntraMacroblockTests.cs"
test_path.write_text(r'''using System;
using System.Linq;
using FileFormat.Codecs.Mpeg4;
using FileFormat.Core;
using Hawkynt.FileFormats.Video;

namespace FileFormat.Codecs.MsMpeg4.Tests;

[TestFixture]
public sealed class MsMpeg4PredictedIntraMacroblockTests {

  [TestCase("MPG4", MsMpeg4Version.Version1)]
  [TestCase("MP42", MsMpeg4Version.Version2)]
  [TestCase("MP43", MsMpeg4Version.Version3)]
  [Category("Unit")]
  public void SceneChangeUsesTheIntraMacroblockFormInsideAPredictedPicture(string tag, MsMpeg4Version version) {
    var stream = new MediaStreamInfo {
      Index = 0,
      Kind = MediaStreamKind.Video,
      Codec = CodecTag.FromCharacters(tag),
      Width = 16,
      Height = 16,
      FrameRate = new(25, 1),
      TimeBase = new(1, 25),
    };

    var encoder = MsMpeg4VideoEncoder.Create(stream);
    Assert.That(encoder.TryEncode(_Solid(16, 16, 24), 0, out var first), Is.True);
    Assert.That(encoder.TryEncode(_Solid(16, 16, 224), 1, out var second), Is.True);

    Assert.Multiple(() => {
      Assert.That(first.IsKeyFrame, Is.True);
      Assert.That(second.IsKeyFrame, Is.False, "an intra macroblock does not turn its P picture into a key frame");
    });

    var reader = new Mpeg4BitReader(second.Data);
    var header = MsMpeg4PictureHeader.Parse(ref reader, version, macroblockHeight: 1, previousSliceHeight: 1);
    Assert.That(header.CodingType, Is.EqualTo(MsMpeg4PictureHeader.PredictiveCoded));
    Assert.That(reader.ReadBit(), Is.Zero, "the scene-change macroblock must be coded rather than skipped");

    switch (version) {
      case MsMpeg4Version.Version1:
        Assert.That(MsMpeg4Tables.InterMacroblock.Read(ref reader) & 4, Is.Not.Zero,
          "version 1 carries the intra flag in bit 2 of H.263 MCBPC");
        break;

      case MsMpeg4Version.Version2:
        Assert.That(MsMpeg4Tables.V2MacroblockType.Read(ref reader) & 4, Is.Not.Zero,
          "version 2 carries the intra flag in bit 2 of its macroblock type");
        break;

      default:
        Assert.That(MsMpeg4Tables.MacroblockNonIntra.Read(ref reader) & 0x40, Is.Zero,
          "version 3 carries an intra macroblock in the bit-6-clear half of its P-picture table");
        break;
    }

    var decoder = MsMpeg4VideoDecoder.Create(encoder.DescribeStream());
    Assert.That(decoder.TryDecode(first, out _), Is.True);
    Assert.That(decoder.TryDecode(second, out var decoded), Is.True);
    Assert.That(decoded.PixelData.Average(value => (double)value), Is.GreaterThan(180d));
  }

  private static RawImage _Solid(int width, int height, byte value) {
    var pixels = new byte[width * height * 3];
    Array.Fill(pixels, value);
    return new() { Width = width, Height = height, Format = PixelFormat.Rgb24, PixelData = pixels };
  }
}
''', encoding="utf-8", newline="\n")
