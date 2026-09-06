using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FileFormat.Codecs.Mpeg4;
using FileFormat.Core;
using Hawkynt.FileFormats.Video;

namespace FileFormat.Codecs.MsMpeg4.Tests;

/// <summary>
/// What the codec takes, what it refuses, and whether what it writes is what it reads.
/// </summary>
/// <remarks>
/// The arithmetic was checked against ffmpeg rather than here, over four hundred and eighty encoded
/// streams and five real-world files, and what that measured is written down in
/// <see cref="MsMpeg4VideoDecoder"/>'s own remarks. What is left for a test that runs anywhere is the
/// part that needs no reference decoder: the refusals, which say what is not implemented rather than
/// failing at it, and the round trip, which is the strongest statement this library can make about
/// itself — the encoder and the decoder agree on every bit of every codeword, or a picture comes back
/// wrong.
/// </remarks>
[TestFixture]
public sealed class MsMpeg4VideoCodecTests {

  /// <summary>How far a round trip's samples may sit from the source, as a mean squared error.</summary>
  /// <remarks>
  /// The coding is lossy, the encoder quantises at eight and the test picture is deliberately the
  /// worst case for it — hard edges at full saturation, which is where a transform codec spends its
  /// error. So this bound says "a picture came back", not "the picture came back exactly"; what says
  /// the picture is the right one is the assertion beside it, that every frame is closer to its own
  /// source than to any other frame's.
  /// </remarks>
  private const double _WORST_MEAN_SQUARED_ERROR = 300d;

  private static readonly string[] _Version1Tags = ["MPG4", "MP41", "DIV1"];
  private static readonly string[] _Version2Tags = ["MP42", "DIV2"];

  private static readonly string[] _Version3Tags =
    ["MP43", "DIV3", "DIV4", "DIV5", "DIV6", "DVX3", "AP41", "AP42", "COL0", "COL1", "MPG3"];

  // ============================================================================================
  // What reaches the codec
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void EveryCodeOfAllThreeVersionsIsAccepted() {
    foreach (var tag in _Version1Tags.Concat(_Version2Tags).Concat(_Version3Tags))
      Assert.That(MsMpeg4VideoDecoder.Accepts(_Stream(tag)), Is.True, tag);
  }

  [TestCase("MPG4", 1)]
  [TestCase("MP41", 1)]
  [TestCase("DIV1", 1)]
  [TestCase("MP42", 2)]
  [TestCase("DIV2", 2)]
  [TestCase("MP43", 3)]
  [TestCase("DIV3", 3)]
  [TestCase("COL1", 3)]
  [TestCase("MPG3", 3)]
  [Category("Unit")]
  public void TheFourCharacterCodeDecidesWhichOfTheThreeBitstreamsIsRead(string tag, int version)
    => Assert.That((int)MsMpeg4VideoDecoder.Create(_Stream(tag)).Version, Is.EqualTo(version));

  [TestCase("WMV1")]
  [TestCase("WMV2")]
  [Category("Unit")]
  public void WindowsMediaVideoIsRefusedByNameRatherThanDecodedAsVersionThree(string tag) {
    Assert.That(MsMpeg4VideoDecoder.Accepts(_Stream(tag)), Is.True, "the registry has to reach the refusal");
    var refusal = Assert.Throws<NotSupportedException>(() => MsMpeg4VideoDecoder.Create(_Stream(tag)));
    Assert.That(refusal!.Message, Does.Contain("Windows Media Video"));
  }

  [Test]
  [Category("Unit")]
  public void AStreamWithNoSizeIsRefusedBecauseTheBitstreamCarriesNone() {
    var refusal = Assert.Throws<NotSupportedException>(
      () => MsMpeg4VideoDecoder.Create(new() { Index = 0, Kind = MediaStreamKind.Video, Codec = CodecTag.FromCharacters("MP43") }));

    Assert.That(refusal!.Message, Does.Contain("picture size"));
  }

  [Test]
  [Category("Unit")]
  public void ASoundStreamIsNotAccepted()
    => Assert.That(
      MsMpeg4VideoDecoder.Accepts(new() { Index = 1, Kind = MediaStreamKind.Audio, Codec = CodecTag.FromCharacters("MP43") }),
      Is.False);

  // ============================================================================================
  // The picture header
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void APictureTypeTheFormatDoesNotHaveIsRefused() {
    // Two bits, and only two of the four values are pictures: there are no bidirectionally coded
    // pictures and no fourth kind anywhere in the family.
    var refusal = Assert.Throws<InvalidDataException>(
      () => _ParseHeader([0b1000_1000], MsMpeg4Version.Version2, 9, 9));

    Assert.That(refusal!.Message, Does.Contain("picture type"));
  }

  [Test]
  [Category("Unit")]
  public void AQuantiserOfZeroIsRefused() {
    var refusal = Assert.Throws<InvalidDataException>(
      () => _ParseHeader([0b0000_0000, 0b0000_0000], MsMpeg4Version.Version2, 9, 9));

    Assert.That(refusal!.Message, Does.Contain("quantiser of zero"));
  }

  [Test]
  [Category("Unit")]
  public void AVersionOnePictureWithoutTheStartCodeIsRefused() {
    var refusal = Assert.Throws<InvalidDataException>(
      () => _ParseHeader([0, 0, 1, 1, 0, 0, 0], MsMpeg4Version.Version1, 9, 9));

    Assert.That(refusal!.Message, Does.Contain("start code"));
  }

  [Test]
  [Category("Unit")]
  public void AnIntraPictureStatingFewerThanOneSliceIsRefused() {
    // Type nought, quantiser eight, slice field below the bias of twenty-two the field carries.
    var refusal = Assert.Throws<InvalidDataException>(
      () => _ParseHeader([0b0001_0000, 0b0000_0000], MsMpeg4Version.Version2, 9, 9));

    Assert.That(refusal!.Message, Does.Contain("slice"));
  }

  [Test]
  [Category("Unit")]
  public void APredictedPictureKeepsTheSliceHeightTheIntraPictureBeforeItStated() {
    // Type one, quantiser eight, and the skip flag: a predicted picture states no slice field at all.
    var header = _ParseHeader([0b0101_0000, 0b0000_0000], MsMpeg4Version.Version2, 18, 6);

    Assert.Multiple(() => {
      Assert.That(header.CodingType, Is.EqualTo(MsMpeg4PictureHeader.PredictiveCoded));
      Assert.That(header.Quantiser, Is.EqualTo(8));
      Assert.That(header.SliceHeight, Is.EqualTo(6));
    });
  }

  // ============================================================================================
  // The round trip
  // ============================================================================================

  [TestCase("MPG4")]
  [TestCase("MP42")]
  [TestCase("MP43")]
  [Category("Unit")]
  public void WhatTheEncoderWritesIsWhatTheDecoderReads(string tag) {
    const int width = 96;
    const int height = 80;
    const int frames = 15;

    var described = _Stream(tag, width, height);
    var encoder = MsMpeg4VideoEncoder.Create(described);
    var sources = new List<byte[]>();
    var packets = new List<CodedPacket>();

    for (var i = 0; i < frames; ++i) {
      var picture = _Picture(width, height, i);
      sources.Add(picture);

      Assert.That(
        encoder.TryEncode(new() { Width = width, Height = height, Format = PixelFormat.Rgb24, PixelData = picture }, i, out var packet),
        Is.True, $"frame {i}");

      packets.Add(packet);
    }

    Assert.Multiple(() => {
      Assert.That(packets[0].IsKeyFrame, Is.True, "a stream has to begin at an intra picture");
      Assert.That(packets.Skip(1).Take(11).Any(p => p.IsKeyFrame), Is.False, "intra pictures are twelve apart");
      Assert.That(packets[12].IsKeyFrame, Is.True);
    });

    var decoder = MsMpeg4VideoDecoder.Create(encoder.DescribeStream());
    for (var i = 0; i < frames; ++i) {
      Assert.That(decoder.TryDecode(packets[i], out var decoded), Is.True, $"frame {i}");

      var error = _MeanSquaredError(sources[i], decoded.PixelData);
      Assert.That(error, Is.LessThan(_WORST_MEAN_SQUARED_ERROR),
        $"frame {i} of the {tag} round trip came back {error:F1} squared levels from what went in");

      // The picture moves two samples a frame, so a frame decoded as its neighbour — a vector read
      // wrong, a prediction taken from the wrong place — is further from its own source than from
      // that neighbour's, and no error bound loose enough to allow the quantiser would catch it.
      for (var other = 0; other < frames; ++other)
        if (other != i)
          Assert.That(error, Is.LessThan(_MeanSquaredError(sources[other], decoded.PixelData)),
            $"frame {i} of the {tag} round trip came back closer to frame {other} than to itself");
    }
  }

  [TestCase("MPG4")]
  [TestCase("MP42")]
  [TestCase("MP43")]
  [Category("Unit")]
  public void APictureThatDoesNotMoveCostsAlmostNothingToCodeAgain(string tag) {
    const int width = 64;
    const int height = 64;

    var encoder = MsMpeg4VideoEncoder.Create(_Stream(tag, width, height));

    // One flat colour, which the intra picture reconstructs exactly: a flat block's transform is its
    // DC and nothing else, and the DC's own step divides it without remainder. So the second picture
    // has no residual at all to code and every macroblock of it is skipped.
    var picture = new byte[width * height * 3];
    Array.Fill(picture, (byte)0x60);

    encoder.TryEncode(new() { Width = width, Height = height, Format = PixelFormat.Rgb24, PixelData = picture }, 0, out var first);
    encoder.TryEncode(new() { Width = width, Height = height, Format = PixelFormat.Rgb24, PixelData = picture }, 1, out var second);

    Assert.Multiple(() => {
      Assert.That(second.Data.Length, Is.LessThan(first.Data.Length));

      // The whole picture is its header — never more than forty-four bits, which is version 1's start
      // code and picture number — and one skip bit per macroblock.
      Assert.That(second.Data.Length, Is.LessThanOrEqualTo(8),
        "a predicted picture identical to the one before it is nothing but skip bits");
    });
  }

  [Test]
  [Category("Unit")]
  public void ASizeThatIsNotAWholeNumberOfMacroblocksStillRoundTrips() {
    // The format codes whole macroblocks and says nowhere that one is partly outside the picture, so
    // the samples past the edge are coded like any others and cropped on the way out.
    const int width = 100;
    const int height = 60;

    var encoder = MsMpeg4VideoEncoder.Create(_Stream("MP43", width, height));
    var picture = _Picture(width, height, 3);

    Assert.That(
      encoder.TryEncode(new() { Width = width, Height = height, Format = PixelFormat.Rgb24, PixelData = picture }, 0, out var packet),
      Is.True);

    var decoder = MsMpeg4VideoDecoder.Create(encoder.DescribeStream());
    Assert.That(decoder.TryDecode(packet, out var decoded), Is.True);

    Assert.Multiple(() => {
      Assert.That(decoded.Width, Is.EqualTo(width));
      Assert.That(decoded.Height, Is.EqualTo(height));
      Assert.That(_MeanSquaredError(picture, decoded.PixelData), Is.LessThan(_WORST_MEAN_SQUARED_ERROR));
    });
  }

  [Test]
  [Category("Unit")]
  public void APredictedPictureBeforeAnyIntraOneIsRefused() {
    var encoder = MsMpeg4VideoEncoder.Create(_Stream("MP43", 64, 64));
    var picture = _Picture(64, 64, 0);
    encoder.TryEncode(new() { Width = 64, Height = 64, Format = PixelFormat.Rgb24, PixelData = picture }, 0, out _);
    encoder.TryEncode(new() { Width = 64, Height = 64, Format = PixelFormat.Rgb24, PixelData = _Picture(64, 64, 1) }, 1, out var predicted);

    var decoder = MsMpeg4VideoDecoder.Create(encoder.DescribeStream());
    var refusal = Assert.Throws<InvalidDataException>(() => decoder.TryDecode(predicted, out _));
    Assert.That(refusal!.Message, Does.Contain("intra picture"));
  }

  [Test]
  [Category("Unit")]
  public void TheEncoderDescribesTheStreamWithTheCodeItWasAskedFor() {
    var stream = MsMpeg4VideoEncoder.Create(_Stream("MP42", 64, 64)).DescribeStream();

    Assert.Multiple(() => {
      Assert.That(stream.Codec, Is.EqualTo(CodecTag.FromCharacters("MP42")));
      Assert.That(stream.Width, Is.EqualTo(64));
      Assert.That(stream.Height, Is.EqualTo(64));
      Assert.That(stream.CodecPrivateData.Length, Is.GreaterThan(0), "the size lives in the container and nowhere else");
    });
  }

  [Test]
  [Category("Unit")]
  public void TheRegistryReachesBothHalvesOfTheCodec() {
    var stream = _Stream("MP43");

    Assert.Multiple(() => {
      Assert.That(VideoFormatRegistry.CanDecode(stream), Is.True);
      Assert.That(VideoFormatRegistry.CanEncode(stream), Is.True);
      Assert.That(VideoFormatRegistry.CreateDecoder(stream), Is.InstanceOf<MsMpeg4VideoDecoder>());
      Assert.That(VideoFormatRegistry.CreateEncoder(stream), Is.InstanceOf<MsMpeg4VideoEncoder>());
    });
  }

  // ============================================================================================
  // Fixtures
  // ============================================================================================

  /// <summary>Reads a picture header out of bytes written by hand, the reader being a ref struct.</summary>
  private static MsMpeg4PictureHeader _ParseHeader(byte[] bytes, MsMpeg4Version version, int macroblockHeight, int previousSliceHeight) {
    var reader = new Mpeg4BitReader(bytes);
    return MsMpeg4PictureHeader.Parse(ref reader, version, macroblockHeight, previousSliceHeight);
  }

  private static MediaStreamInfo _Stream(string tag, int width = 176, int height = 144) => new() {
    Index = 0,
    Kind = MediaStreamKind.Video,
    Codec = CodecTag.FromCharacters(tag),
    Width = width,
    Height = height,
    FrameRate = new(25, 1),
    TimeBase = new(1, 25),
  };

  /// <summary>
  /// A picture with flat regions, a gradient and a hard edge, moved a little every frame.
  /// </summary>
  /// <remarks>
  /// Each of the three exercises a different part of the coding: a flat region is a DC and nothing
  /// else, a gradient fills the low coefficients, and a hard edge fills the high ones and is what a
  /// motion vector has anything to lock onto. Moving it is what makes the predicted pictures code a
  /// vector rather than skipping.
  /// </remarks>
  private static byte[] _Picture(int width, int height, int frame) {
    var rgb = new byte[width * height * 3];
    var shift = 2 * frame;

    for (var y = 0; y < height; ++y)
      for (var x = 0; x < width; ++x) {
        var at = (y * width + x) * 3;
        var moved = x + shift;

        rgb[at] = (byte)(moved % 64 < 32 ? 200 : 40);
        rgb[at + 1] = (byte)(32 * ((y + shift) / 16 % 8));
        rgb[at + 2] = (byte)((moved * 255 / Math.Max(1, width)) & 0xFF);
      }

    return rgb;
  }

  private static double _MeanSquaredError(byte[] source, byte[] decoded) {
    var total = 0d;
    for (var i = 0; i < source.Length; ++i) {
      var difference = source[i] - decoded[i];
      total += difference * difference;
    }

    return total / source.Length;
  }
}
