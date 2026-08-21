using System;
using System.IO;
using System.Linq;
using FileFormat.Codecs;
using FileFormat.Codecs.Vc1;
using FileFormat.Core;

namespace FileFormat.Codecs.Vc1.Tests;

/// <summary>
/// The VC-1 decoder: what it reads, what it refuses, and the one picture whose reconstruction can be
/// worked out on paper.
/// </summary>
/// <remarks>
/// The claim that this decodes SMPTE 421M exactly is not made here — it is made by measurement, on
/// thirty-five intra pictures of seven files decoded here and by ffmpeg and compared plane by plane,
/// every sample identical. What these tests hold down is everything that measurement cannot reach
/// without a sample in the tree: the sequence header, the refusals, the registry, and a hand-built
/// picture whose every syntax element was chosen so that the answer is one number repeated.
/// </remarks>
[TestFixture]
public sealed class Vc1VideoDecoderTests {

  private const int _WIDTH = 32;
  private const int _HEIGHT = 32;

  private static MediaStreamInfo _Stream(byte[] sequenceHeader, int width = _WIDTH, int height = _HEIGHT, string code = "WMV3")
    => new() {
      Index = 0,
      Kind = MediaStreamKind.Video,
      Codec = CodecTag.FromCharacters(code),
      Width = width,
      Height = height,
      CodecPrivateData = Vc1TestStream.AsCodecPrivateData(sequenceHeader, width, height),
    };

  // ------------------------------------------------------------------------------------------
  // The sequence header
  // ------------------------------------------------------------------------------------------

  [TestCase(0)]
  [TestCase(4)]
  [Category("Unit")]
  public void TheSequenceHeaderIsReadOutOfTheContainersPrivateData(int profile) {
    var header = Vc1SequenceHeader.ReadFrom(Vc1TestStream.SequenceHeader(profile: profile, overlap: true, quantiser: 3));

    Assert.Multiple(() => {
      Assert.That((int)header.Profile, Is.EqualTo(profile));
      Assert.That(header.Overlap, Is.True);
      Assert.That(header.Quantiser, Is.EqualTo(3));
    });
  }

  [Test]
  [Category("Unit")]
  public void ASequenceHeaderShorterThanFourBytesIsRefused()
    => Assert.Throws<InvalidDataException>(() => Vc1SequenceHeader.ReadFrom(new byte[3]));

  [TestCase(1, 1, 0, 1)]
  [TestCase(0, 0, 0, 1)]
  [TestCase(0, 1, 1, 1)]
  [TestCase(0, 1, 0, 0)]
  [Category("Unit")]
  public void TheReservedBitsSayWhetherTheHeaderWasReadTheRightWayRound(int three, int four, int five, int six) {
    // Four bits the standard fixes at 0, 1, 0, 1. They are the whole reason a thirty-two bit field with
    // no length, no signature and no checksum can be recognised at all: the same four bytes read as a
    // little-endian number satisfies none of them.
    var header = Vc1TestStream.SequenceHeader(reserved3: three, reserved4: four, reserved5: five, reserved6: six);

    var refusal = Assert.Throws<InvalidDataException>(() => Vc1SequenceHeader.ReadFrom(header));

    Assert.That(refusal!.Message, Does.Contain("reserved"));
  }

  [TestCase(44)]
  [TestCase(40)]
  [Category("Unit")]
  public void TheSequenceHeaderSitsAFixedDistanceInsideTheBitmapHeader(int declaredHeaderSize) {
    // The bitmap header's own size field counts the codec's data as well as the structure, so a
    // Windows Media stream states 44 for a forty-byte header and four bytes of sequence header. A
    // reader that stepped over that many bytes would step over the sequence header itself and land
    // back on the size field, where the low nibble reads as a profile of 2. Both spellings of the
    // field have to reach the same four bytes.
    var stream = new MediaStreamInfo {
      Index = 0,
      Kind = MediaStreamKind.Video,
      Codec = CodecTag.FromCharacters("WMV3"),
      Width = _WIDTH,
      Height = _HEIGHT,
      CodecPrivateData = Vc1TestStream.AsCodecPrivateData(
        Vc1TestStream.SequenceHeader(profile: 4), _WIDTH, _HEIGHT, declaredHeaderSize),
    };

    Assert.DoesNotThrow(() => Vc1VideoDecoder.Create(stream));
  }

  [Test]
  [Category("Unit")]
  public void APrivateDataBlockWithNoBitmapHeaderIsReadJustAsWell() {
    // ASF and AVI both hand the sequence header over behind a BITMAPINFOHEADER, but the four bytes on
    // their own are what the standard defines, and a container that passes only those is not wrong.
    var sequence = Vc1TestStream.SequenceHeader(profile: 4);
    var stream = new MediaStreamInfo {
      Index = 0,
      Kind = MediaStreamKind.Video,
      Codec = CodecTag.FromCharacters("WMV3"),
      Width = _WIDTH,
      Height = _HEIGHT,
      CodecPrivateData = sequence,
    };

    Assert.DoesNotThrow(() => Vc1VideoDecoder.Create(stream));
  }

  // ------------------------------------------------------------------------------------------
  // A picture whose answer is known
  // ------------------------------------------------------------------------------------------

  [Test]
  [Category("Unit")]
  public void APictureOfNoCoefficientsWithoutSmoothingIsTheDcSeedTransformed() {
    // Every block uncoded and every DC differential nought, so every block reconstructs from the seed
    // the standard puts in for a neighbour that is not there. With smoothing off that seed is
    // (1024 + DCStepSize/2) / DCStepSize, which at a quantiser of 5 is 128 over a step of 8; the DC
    // coefficient is then 1024, and the transform's gain of 144/1024 makes every sample 144. Nothing is
    // added afterwards, because the seed is what carries the offset when smoothing is off.
    var frame = _Decode(Vc1TestStream.SequenceHeader(quantiser: 3, overlap: false), quantiserIndex: 5);

    _AssertEveryPixelIs(frame, 144);
  }

  [Test]
  [Category("Unit")]
  public void APictureOfNoCoefficientsWithSmoothingIsMidGrey() {
    // The other half of the same rule. With smoothing on the seed is nought, so every block
    // reconstructs to nought and the constant 128 is added at the end instead — and the smoothing
    // filter leaves a flat field flat, so the answer is 128 everywhere. A decoder that added 128 in
    // both cases, or in neither, fails one of these two tests.
    var frame = _Decode(Vc1TestStream.SequenceHeader(quantiser: 3, overlap: true), quantiserIndex: 10);

    _AssertEveryPixelIs(frame, 128);
  }

  [Test]
  [Category("Unit")]
  public void SmoothingIsOnlyAppliedAboveTheQuantiserTheStandardNames() {
    // Overlap smoothing runs on an I picture only where the sequence asked for it and the picture
    // quantiser is 9 or above (8.5.1). At 8 it does not, which puts the seed back and the constant 128
    // away again.
    var frame = _Decode(Vc1TestStream.SequenceHeader(quantiser: 3, overlap: true), quantiserIndex: 8);

    // A quantiser of 8 gives a step of 10, a seed of (1024 + 5) / 10 = 102, a DC of 1020 and a sample
    // of (12 * ((12 * 1020 + 4) >> 3) + 64) >> 7.
    var expected = ((12 * (((12 * 1020) + 4) >> 3)) + 64) >> 7;
    _AssertEveryPixelIs(frame, expected);
  }

  private static Vc1Frame _Decode(byte[] sequenceHeader, int quantiserIndex) {
    var sequence = Vc1SequenceHeader.ReadFrom(sequenceHeader);
    var decoder = new Vc1PictureDecoder(sequence, _WIDTH / 16, _HEIGHT / 16);
    var picture = Vc1TestStream.FlatIntraPicture(_WIDTH / 16, _HEIGHT / 16, quantiserIndex);

    return decoder.Decode(picture, default, out _);
  }

  private static void _AssertEveryPixelIs(Vc1Frame frame, int expected) {
    Assert.Multiple(() => {
      Assert.That(frame.Luma.All(v => v == expected), Is.True,
        $"the luma plane is not all {expected}; it holds {string.Join(", ", frame.Luma.Distinct().Take(4))}");
      Assert.That(frame.Cb.All(v => v == expected), Is.True,
        $"the Cb plane is not all {expected}; it holds {string.Join(", ", frame.Cb.Distinct().Take(4))}");
      Assert.That(frame.Cr.All(v => v == expected), Is.True,
        $"the Cr plane is not all {expected}; it holds {string.Join(", ", frame.Cr.Distinct().Take(4))}");
    });
  }

  [Test]
  [Category("Unit")]
  public void APictureIsReadToWithinAByteOfItsEnd() {
    // A decode that has gone out of step almost always ends a long way from the end of the packet, so
    // how much was left over is the cheapest check there is that the bitstream was read the way it was
    // written. The encoder pads to a byte boundary, so up to seven bits are expected to remain.
    var sequence = Vc1SequenceHeader.ReadFrom(Vc1TestStream.SequenceHeader(quantiser: 3));
    var decoder = new Vc1PictureDecoder(sequence, 4, 4);
    var picture = Vc1TestStream.FlatIntraPicture(4, 4, 5);

    decoder.Decode(picture, default, out _);

    Assert.That(decoder.BitsAvailable - decoder.BitsConsumed, Is.InRange(0, 7));
  }

  // ------------------------------------------------------------------------------------------
  // The inverse transform
  // ------------------------------------------------------------------------------------------

  [Test]
  [Category("Unit")]
  public void TheInverseTransformOfNothingIsNothing() {
    var block = new int[64];

    Vc1InverseTransform.Apply(block);

    Assert.That(block.All(v => v == 0), Is.True);
  }

  [Test]
  [Category("Unit")]
  public void TheInverseTransformOfADcAloneIsFlat() {
    // Every basis but the first sums to nought across a row, so a block holding only a DC comes out the
    // same everywhere. It is also the case that pins the transform's gain, which is 144 parts in 1024.
    var block = new int[64];
    block[0] = 1024;

    Vc1InverseTransform.Apply(block);

    Assert.That(block.All(v => v == 144), Is.True, $"the block holds {string.Join(", ", block.Distinct())}");
  }

  [Test]
  [Category("Unit")]
  public void TheInverseTransformAppliesTheFirstStageAlongTheRows() {
    // A single coefficient in the first row and second column is the first horizontal harmonic and
    // nothing else, so every row of the answer is the same row and that row is odd about the centre.
    // A first stage applied down the columns instead — which is the same matrix transposed, and reads
    // as plausibly — produces the vertical harmonic, where every column is the same instead.
    var block = new int[64];
    block[1] = 1024;

    Vc1InverseTransform.Apply(block);

    Assert.Multiple(() => {
      for (var row = 1; row < 8; ++row)
        for (var column = 0; column < 8; ++column)
          Assert.That(block[(row * 8) + column], Is.EqualTo(block[column]).Within(1),
            $"row {row} column {column} differs from the first row");

      for (var column = 0; column < 4; ++column)
        Assert.That(block[column], Is.EqualTo(-block[7 - column]).Within(1),
          $"column {column} is not the opposite of column {7 - column}");

      Assert.That(block[0], Is.GreaterThan(0));
    });
  }

  // ------------------------------------------------------------------------------------------
  // Overlap smoothing
  // ------------------------------------------------------------------------------------------

  [Test]
  [Category("Unit")]
  public void SmoothingLeavesAFlatFieldFlat() {
    // The filter's rows each sum to eight, so a constant field is its own answer whatever the rounding
    // constants do. That is what makes the mid-grey test above exact rather than approximate.
    var plane = new int[16 * 16];
    Array.Fill(plane, 37);

    Vc1OverlapSmoothing.Apply(plane, 16, 16);

    Assert.That(plane.All(v => v == 37), Is.True);
  }

  [Test]
  [Category("Unit")]
  public void SmoothingMovesSamplesOnlyAtABlockEdge() {
    // Two pixels either side of every internal 8x8 boundary, and nothing else. A filter applied at the
    // frame edge as well would pull the border of every picture towards nothing.
    var plane = new int[16 * 16];
    for (var i = 0; i < plane.Length; ++i)
      plane[i] = (i % 16) < 8 ? 0 : 200;

    var before = (int[])plane.Clone();
    Vc1OverlapSmoothing.Apply(plane, 16, 16);

    Assert.Multiple(() => {
      for (var y = 0; y < 16; ++y)
        for (var x = 0; x < 16; ++x) {
          var moved = plane[(y * 16) + x] != before[(y * 16) + x];
          var nearEdge = x is 6 or 7 or 8 or 9 || y is 6 or 7 or 8 or 9;
          if (!nearEdge)
            Assert.That(moved, Is.False, $"the sample at ({x},{y}) is not beside a block edge and moved");
        }
    });
  }

  // ------------------------------------------------------------------------------------------
  // What it refuses
  // ------------------------------------------------------------------------------------------

  [Test]
  [Category("Unit")]
  public void APredictedPictureIsRefusedByName() {
    // It needs motion compensation against a reference this decoder never builds. There is no branch
    // that hands back a blank or a repeated picture instead: a repeated frame is what a legitimate
    // still passage looks like, and nobody checks a picture that looks like a picture.
    var sequence = Vc1SequenceHeader.ReadFrom(Vc1TestStream.SequenceHeader(quantiser: 3));
    var decoder = new Vc1PictureDecoder(sequence, 2, 2);

    var refusal = Assert.Throws<NotSupportedException>(() => decoder.Decode(Vc1TestStream.PredictedPicture(), default, out _));

    Assert.That(refusal!.Message, Does.Contain("predicted"));
  }

  [TestCase("WVC1")]
  [TestCase("VC-1")]
  [Category("Unit")]
  public void TheAdvancedProfileIsRefusedByItsOwnCode(string code) {
    // It states its sequence header and entry point inside the bitstream rather than in the container,
    // and shares only its block layer with what is read here.
    var stream = _Stream(Vc1TestStream.SequenceHeader(profile: 4), code: code);

    var refusal = Assert.Throws<NotSupportedException>(() => Vc1VideoDecoder.Create(stream));

    Assert.Multiple(() => {
      Assert.That(refusal!.Message, Does.Contain("Advanced"));
      Assert.That(refusal.Message, Does.Contain(code));

      // But it is still claimed, so that the refusal names the codec rather than the caller being told
      // only that nothing matched.
      Assert.That(Vc1VideoDecoder.Accepts(stream), Is.True);
    });
  }

  [Test]
  [Category("Unit")]
  public void AnAdvancedProfileSequenceHeaderIsRefusedEvenUnderAMainProfileCode() {
    var stream = _Stream(Vc1TestStream.SequenceHeader(profile: 12));

    var refusal = Assert.Throws<NotSupportedException>(() => Vc1VideoDecoder.Create(stream));

    Assert.That(refusal!.Message, Does.Contain("Advanced"));
  }

  [Test]
  [Category("Unit")]
  public void AProfileTheStandardDoesNotDefineIsRefused() {
    var refusal = Assert.Throws<InvalidDataException>(() => Vc1SequenceHeader.ReadFrom(Vc1TestStream.SequenceHeader(profile: 7)));

    Assert.That(refusal!.Message, Does.Contain("profile"));
  }

  [Test]
  [Category("Unit")]
  public void TheInLoopFilterIsRefusedByName() {
    // Part of the reconstruction rather than a postprocess: a picture decoded without it is not the
    // same picture with a blur missing, it is a different picture.
    var stream = _Stream(Vc1TestStream.SequenceHeader(loopFilter: true));

    var refusal = Assert.Throws<NotSupportedException>(() => Vc1VideoDecoder.Create(stream));

    Assert.That(refusal!.Message, Does.Contain("LOOPFILTER"));
  }

  [Test]
  [Category("Unit")]
  public void MultiResolutionCodingIsRefusedByName() {
    var stream = _Stream(Vc1TestStream.SequenceHeader(multiResolution: true));

    var refusal = Assert.Throws<NotSupportedException>(() => Vc1VideoDecoder.Create(stream));

    Assert.That(refusal!.Message, Does.Contain("MULTIRES"));
  }

  [Test]
  [Category("Unit")]
  public void RangeReductionIsRefusedByName() {
    var stream = _Stream(Vc1TestStream.SequenceHeader(rangeReduction: true));

    var refusal = Assert.Throws<NotSupportedException>(() => Vc1VideoDecoder.Create(stream));

    Assert.That(refusal!.Message, Does.Contain("RANGERED"));
  }

  [Test]
  [Category("Unit")]
  public void AStreamWithNoSequenceHeaderIsRefusedByNameRatherThanByItsBytes() {
    // A container may name a stream WMV3 and carry nothing but a bitmap header, which is a stream this
    // codec names and cannot decode — the contract for building a decoder makes that a refusal naming
    // the codec, not a complaint about the bytes, because Simple and Main profile put the sequence
    // header nowhere else and there is nothing to fall back on.
    var stream = new MediaStreamInfo {
      Index = 0,
      Kind = MediaStreamKind.Video,
      Codec = CodecTag.FromCharacters("WMV3"),
      Width = _WIDTH,
      Height = _HEIGHT,
      CodecPrivateData = new byte[40],
    };

    var refusal = Assert.Throws<NotSupportedException>(() => Vc1VideoDecoder.Create(stream));

    Assert.Multiple(() => {
      Assert.That(refusal!.Message, Does.Contain("WMV3"));
      Assert.That(refusal.InnerException, Is.TypeOf<InvalidDataException>());
    });
  }

  [Test]
  [Category("Unit")]
  public void AStreamWhoseContainerStatesNoSizeIsRefusedByName() {
    // Simple and Main profile carry no picture size in the bitstream at all, so the container's is the
    // only one there is and a decoder cannot make one up.
    var stream = _Stream(Vc1TestStream.SequenceHeader(), width: 0, height: 0);

    var refusal = Assert.Throws<NotSupportedException>(() => Vc1VideoDecoder.Create(stream));

    Assert.That(refusal!.Message, Does.Contain("no picture size"));
  }

  // ------------------------------------------------------------------------------------------
  // Identity and registration
  // ------------------------------------------------------------------------------------------

  [Test]
  [Category("Unit")]
  public void TheDecoderIsDiscoveredAndRegistered() {
    var names = Hawkynt.FileFormats.Video.VideoFormatRegistry.AllCodecs.Select(c => c.CodecName).ToList();

    Assert.That(names, Has.Some.Contains("VC-1"));
  }

  [TestCase("WMV3")]
  [TestCase("wmv3")]
  [TestCase("WMV9")]
  [Category("Unit")]
  public void TheCodesAStreamMayNameItWith(string code) {
    // Spelling is not part of the identity: a file patched from one case to the other is read by every
    // other tool as the same codec.
    var stream = _Stream(Vc1TestStream.SequenceHeader(), code: code);

    Assert.Multiple(() => {
      Assert.That(Vc1VideoDecoder.Accepts(stream), Is.True);
      Assert.That(Hawkynt.FileFormats.Video.VideoFormatRegistry.CanDecode(stream), Is.True);
    });
  }

  [Test]
  [Category("Unit")]
  public void AStreamOfSomeOtherCodecIsNotClaimed() {
    var stream = _Stream(Vc1TestStream.SequenceHeader(), code: "MP43");

    Assert.That(Vc1VideoDecoder.Accepts(stream), Is.False);
  }

  [Test]
  [Category("Unit")]
  public void ASoundStreamIsNotClaimed() {
    var stream = new MediaStreamInfo {
      Index = 0,
      Kind = MediaStreamKind.Audio,
      Codec = CodecTag.FromCharacters("WMV3"),
    };

    Assert.That(Vc1VideoDecoder.Accepts(stream), Is.False);
  }

  [Test]
  [Category("Unit")]
  public void APacketOfOneByteIsASkippedPictureAndNotAFrame() {
    // The standard says a coded picture of one byte or fewer is a skipped frame, which is the previous
    // picture over again (7.1.1.4). This decoder holds no previous picture, so it answers that the
    // packet held none rather than inventing one.
    var decoder = Vc1VideoDecoder.Create(_Stream(Vc1TestStream.SequenceHeader()));

    Assert.That(decoder.TryDecode(new(0, new byte[1]), out _), Is.False);
  }

  [Test]
  [Category("Unit")]
  public void NothingIsHeldBackWhenThePacketsRunOut() {
    var decoder = Vc1VideoDecoder.Create(_Stream(Vc1TestStream.SequenceHeader()));

    Assert.That(decoder.Flush(), Is.Empty);
  }

  [Test]
  [Category("Unit")]
  public void APictureComesBackAtTheSizeTheContainerStated() {
    var decoder = Vc1VideoDecoder.Create(_Stream(Vc1TestStream.SequenceHeader(quantiser: 3)));

    Assert.That(decoder.TryDecode(new(0, Vc1TestStream.FlatIntraPicture(2, 2, 5)), out var frame), Is.True);
    Assert.Multiple(() => {
      Assert.That(frame.Width, Is.EqualTo(_WIDTH));
      Assert.That(frame.Height, Is.EqualTo(_HEIGHT));
      Assert.That(frame.Format, Is.EqualTo(PixelFormat.Rgb24));
      Assert.That(frame.PixelData, Has.Length.EqualTo(_WIDTH * _HEIGHT * 3));
    });
  }
}
