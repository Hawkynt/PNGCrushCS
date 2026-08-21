using System;
using System.Buffers.Binary;
using System.IO;
using FileFormat.Core;

namespace FileFormat.Codecs.Ffv1.Tests;

/// <summary>
/// The parts of FFV1 whose answers can be written down, and the refusals.
/// </summary>
/// <remarks>
/// The decoder as a whole was measured against ffmpeg over eighty streams and 363 frames — every
/// pixel format its encoder writes at eight bits, both entropy coders, versions 0, 1 and 3, one
/// slice and sixteen, with and without slice checksums. What these tests add is the arithmetic
/// underneath it, where a mistake can hide behind a stream that happens not to reach it: the coder's
/// two extreme inputs, the Golomb codes the specification prints worked examples of, the state
/// transition mirror, the checksum's defining property, and the border a slice's leftmost column is
/// predicted from.
/// </remarks>
[TestFixture]
public class Ffv1DecoderTests {

  // ============================================================================================
  // The range coder
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void AllZeroBytesReadAsAllZeroBits() {
    // With nothing in the low register the split always falls above it, so every bit is a nought.
    // A signed number is then "not zero", an exponent of nothing, and a positive sign: exactly one.
    var coder = _Coder(new byte[16]);
    var states = _States();

    Assert.That(coder.Symbol(states, true), Is.EqualTo(1));
    Assert.That(coder.Symbol(states, false), Is.EqualTo(1));
  }

  [Test]
  [Category("Unit")]
  public void AllOneBytesReadAsAllOneBits() {
    // The opposite end: the coder starts saturated, every bit is a one, and the first of them says
    // the number is zero.
    var coder = _Coder([0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF]);
    var states = _States();

    Assert.That(coder.Symbol(states, true), Is.EqualTo(0));
  }

  [Test]
  [Category("Unit")]
  public void ReadingABitMovesTheStateAlongTheTableTheBitChose() {
    var states = _States();
    var coder = _Coder(new byte[16]);

    coder.Get(states, 0);
    var (zero, _) = Ffv1StateTransition.Build([]);

    Assert.That(states[0], Is.EqualTo(zero[128]));
    Assert.That(states[1], Is.EqualTo(128), "the other states are left where they were");
  }

  // ============================================================================================
  // The state transition tables
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void TheZeroTableIsTheMirrorOfTheOneTable() {
    var (zero, one) = Ffv1StateTransition.Build([]);

    for (var i = 1; i < 256; ++i)
      Assert.That(zero[i], Is.EqualTo((byte)((256 - one[256 - i]) & 0xFF)), $"state {i}");
  }

  [Test]
  [Category("Unit")]
  public void TheDefaultTableIsTheOneTheSpecificationPrints() {
    var (_, one) = Ffv1StateTransition.Build([]);

    Assert.That(one[8], Is.EqualTo(20));
    Assert.That(one[128], Is.EqualTo(134));
    Assert.That(one[248], Is.EqualTo(248));
    Assert.That(one[7], Is.EqualTo(0), "the states below eight are never reached");
    Assert.That(one[249], Is.EqualTo(0), "nor the ones above two hundred and forty-eight");
  }

  [Test]
  [Category("Unit")]
  public void AStreamsOwnTableIsTheDefaultOnePlusWhatItStates() {
    var deltas = new int[256];
    deltas[128] = 5;
    deltas[64] = -3;

    var (_, one) = Ffv1StateTransition.Build(deltas);

    Assert.That(one[128], Is.EqualTo(139));
    Assert.That(one[64], Is.EqualTo(71));
    Assert.That(one[100], Is.EqualTo(108), "a state the stream said nothing about is left alone");
  }

  // ============================================================================================
  // Golomb-Rice
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void TheGolombCodesAreTheOnesTheSpecificationWorksThrough() {
    // RFC 9043 §3.8.2.1 prints these five, which between them cover the unary prefix, the k-bit
    // suffix, and a prefix longer than one bit.
    Assert.That(_Golomb("1", 0), Is.EqualTo(0));
    Assert.That(_Golomb("001", 0), Is.EqualTo(2));
    Assert.That(_Golomb("100", 2), Is.EqualTo(0));
    Assert.That(_Golomb("110", 2), Is.EqualTo(2));
    Assert.That(_Golomb("0101", 2), Is.EqualTo(5));
  }

  [Test]
  [Category("Unit")]
  public void TwelveZeroesEscapeIntoAValueWrittenOutInFull() {
    // The same section's last example: twelve zeroes, then the value less eleven in eight bits.
    Assert.That(_Golomb("000000000000 10000000", 0, 8), Is.EqualTo(139));
  }

  [Test]
  [Category("Unit")]
  public void TheSignedCodeFoldsTheSignIntoTheLowBit() {
    Assert.That(_SignedGolomb("1", 0), Is.EqualTo(0));
    Assert.That(_SignedGolomb("01", 0), Is.EqualTo(-1));
    Assert.That(_SignedGolomb("001", 0), Is.EqualTo(1));
    Assert.That(_SignedGolomb("0001", 0), Is.EqualTo(-2));
  }

  [Test]
  [Category("Unit")]
  public void AGolombContextStartsWhereTheSpecificationSaysItDoes() {
    var state = new Ffv1GolombState();

    Assert.That(state.Drift, Is.EqualTo(0));
    Assert.That(state.ErrorSum, Is.EqualTo(4));
    Assert.That(state.Bias, Is.EqualTo(0));
    Assert.That(state.Count, Is.EqualTo(1));
  }

  // ============================================================================================
  // The checksum
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void PuttingTheRemainderAtTheEndMakesTheWholeThingComeOutAtNothing() {
    // The property everything checks by: a slice or a configuration record carries four bytes chosen
    // so that running the check over all of it, those four included, leaves nothing behind.
    var body = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 };
    var whole = new byte[body.Length + 4];
    body.CopyTo(whole, 0);
    BinaryPrimitives.WriteUInt32BigEndian(whole.AsSpan(body.Length), Ffv1Crc.Of(body));

    Assert.That(Ffv1Crc.Of(whole), Is.Zero);
  }

  [Test]
  [Category("Unit")]
  public void OneChangedBitStopsTheChecksumComingOut() {
    var body = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 };
    var whole = new byte[body.Length + 4];
    body.CopyTo(whole, 0);
    BinaryPrimitives.WriteUInt32BigEndian(whole.AsSpan(body.Length), Ffv1Crc.Of(body));

    whole[5] ^= 0x01;
    Assert.That(Ffv1Crc.Of(whole), Is.Not.Zero);
  }

  // ============================================================================================
  // The neighbourhood
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void TheColumnLeftOfASliceIsItsOwnFirstColumnOneRowDown() {
    var plane = new Ffv1Plane(3, 3);
    for (var y = 0; y < 3; ++y)
      for (var x = 0; x < 3; ++x)
        plane[x, y] = 10 * y + x;

    Assert.That(plane.At(-1, 1), Is.EqualTo(plane[0, 0]));
    Assert.That(plane.At(-1, 2), Is.EqualTo(plane[0, 1]));
    Assert.That(plane.At(-1, 0), Is.Zero, "the top of that column has nothing above it");
  }

  [Test]
  [Category("Unit")]
  public void EverythingFurtherLeftAndEverythingAboveIsNothing() {
    var plane = new Ffv1Plane(3, 3);
    for (var y = 0; y < 3; ++y)
      for (var x = 0; x < 3; ++x)
        plane[x, y] = 10 * y + x + 1;

    Assert.That(plane.At(-2, 1), Is.Zero);
    Assert.That(plane.At(-3, 2), Is.Zero);
    Assert.That(plane.At(0, -1), Is.Zero);
    Assert.That(plane.At(1, -2), Is.Zero);
  }

  [Test]
  [Category("Unit")]
  public void TheColumnRightOfASliceIsItsLastColumnRepeated() {
    var plane = new Ffv1Plane(3, 2);
    plane[2, 0] = 77;

    Assert.That(plane.At(3, 0), Is.EqualTo(77));
    Assert.That(plane.At(9, 0), Is.EqualTo(77));
  }

  // ============================================================================================
  // What refuses
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void AConfigurationRecordWhoseChecksumDoesNotComeOutIsRefused() {
    var record = new byte[32];
    record[0] = 0x12;

    var failure = Assert.Throws<InvalidDataException>(() => Ffv1Decoder.Create(_Stream(64, 48, record)));
    Assert.That(failure!.Message, Does.Contain("checksum does not come out"));
  }

  [Test]
  [Category("Unit")]
  public void AConfigurationRecordTooShortToHoldItsChecksumIsRefused() {
    var failure = Assert.Throws<InvalidDataException>(() => Ffv1Decoder.Create(_Stream(64, 48, [1, 2, 3])));
    Assert.That(failure!.Message, Does.Contain("shorter than the four bytes of checksum"));
  }

  [Test]
  [Category("Unit")]
  public void APictureSizeNoFrameCanBeDecodedIntoIsRefused() {
    var failure = Assert.Throws<InvalidDataException>(() => Ffv1Decoder.Create(_Stream(0, 48, null)));
    Assert.That(failure!.Message, Does.Contain("picture size"));
  }

  [Test]
  [Category("Unit")]
  public void AFrameOfNoBytesIsRefusedRatherThanRepeatingTheOneBeforeIt() {
    var decoder = Ffv1Decoder.Create(_Stream(64, 48, null));

    var failure = Assert.Throws<InvalidDataException>(() => decoder.TryDecode(new(0, Array.Empty<byte>()), out _));
    Assert.That(failure!.Message, Does.Contain("no bytes"));
  }

  [Test]
  [Category("Unit")]
  public void AStreamThatOpensWithoutAKeyframeIsRefused() {
    // A version 0 or 1 stream states how it is coded in its keyframes only, so there is nothing to
    // decode a frame that is not one against. All-zero bytes read as a nought for the keyframe bit.
    var decoder = Ffv1Decoder.Create(_Stream(64, 48, null));

    var failure = Assert.Throws<InvalidDataException>(() => decoder.TryDecode(new(0, new byte[64]), out _));
    Assert.That(failure!.Message, Does.Contain("not a keyframe"));
  }

  // ============================================================================================
  // Which streams it takes
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void TheCodecAnswersToItsCodeAndToTheNameMatroskaGivesIt() {
    Assert.That(Ffv1Decoder.Accepts(_Stream(64, 48, null)), Is.True);

    var matroska = new MediaStreamInfo {
      Index = 0,
      Kind = MediaStreamKind.Video,
      CodecId = "V_FFV1",
      Width = 64,
      Height = 48,
    };
    Assert.That(Ffv1Decoder.Accepts(matroska), Is.True);
  }

  [Test]
  [Category("Unit")]
  public void TheCodecDoesNotAnswerForAnotherCode() {
    var other = new MediaStreamInfo {
      Index = 0,
      Kind = MediaStreamKind.Video,
      Codec = CodecTag.FromCharacters("FFVH"),
      Width = 64,
      Height = 48,
    };

    Assert.That(Ffv1Decoder.Accepts(other), Is.False);
  }

  // ============================================================================================

  private static byte[] _States() {
    var states = new byte[Ffv1RangeCoder.CONTEXT_SIZE];
    Array.Fill(states, (byte)128);
    return states;
  }

  private static Ffv1RangeCoder _Coder(byte[] data) {
    var (zero, one) = Ffv1StateTransition.Build([]);
    return new(data, zero, one);
  }

  private static int _Golomb(string bits, int k, int escapeBits = 8) => _Reader(bits).UnsignedGolomb(k, escapeBits);

  private static int _SignedGolomb(string bits, int k) => _Reader(bits).SignedGolomb(k, 8);

  private static Ffv1GolombDecoder _Reader(string bits) {
    var clean = bits.Replace(" ", string.Empty);
    var bytes = new byte[(clean.Length + 7) / 8 + 4];
    for (var i = 0; i < clean.Length; ++i)
      if (clean[i] == '1')
        bytes[i >> 3] |= (byte)(0x80 >> (i & 7));

    return new(bytes, 0);
  }

  private static MediaStreamInfo _Stream(int width, int height, byte[]? record) {
    var description = new byte[40 + (record?.Length ?? 0)];
    record?.CopyTo(description, 40);

    return new() {
      Index = 0,
      Kind = MediaStreamKind.Video,
      Codec = CodecTag.FromCharacters("FFV1"),
      Width = width,
      Height = height,
      CodecPrivateData = description,
    };
  }
}
