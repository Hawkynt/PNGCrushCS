using System;
using System.IO;

namespace FileFormat.LuraDocument.Tests;

/// <summary>The JBIG (ITU-T T.82) mask codec, held to the standard's own test data and to jbig-kit.</summary>
[TestFixture]
public sealed class LuraDocumentJbigTests {

  // T.82 clause 7.1: 256 pixels in two contexts and the coded data the arithmetic coder must turn them into.
  private static readonly byte[] _T82Pixels = Convert.FromHexString("05e000008b0001c4170000347fff1a3f951b05d81d17e7700000000006560e6a");
  private static readonly byte[] _T82Contexts = Convert.FromHexString("0fe000000f0000f0ff00" + new string('0', 44));
  private static readonly byte[] _T82Coded = Convert.FromHexString("6989995c32eafaa0d5ff527fffffffc00000003fff2d208291");

  private static int _Bit(byte[] bits, int index) => (bits[index >> 3] >> (7 - (index & 7))) & 1;

  [Test]
  [Category("Unit")]
  public void ArithmeticEncoderMatchesTheStandardsTestData() {
    var contexts = new LuraDocumentQmCoder.Contexts();
    var encoder = new LuraDocumentQmCoder.Encoder();
    for (var i = 0; i < 256; ++i)
      encoder.Encode(contexts, _Bit(_T82Contexts, i), _Bit(_T82Pixels, i));
    Assert.That(encoder.Finish(), Is.EqualTo(_T82Coded));
  }

  [Test]
  [Category("Unit")]
  public void ArithmeticDecoderMatchesTheStandardsTestData() {
    var contexts = new LuraDocumentQmCoder.Contexts();
    var decoder = new LuraDocumentQmCoder.Decoder(_T82Coded, 0, _T82Coded.Length);
    var pixels = new byte[32];
    for (var i = 0; i < 256; ++i)
      if (decoder.Decode(contexts, _Bit(_T82Contexts, i)) != 0)
        pixels[i >> 3] |= (byte)(0x80 >> (i & 7));
    Assert.That(pixels, Is.EqualTo(_T82Pixels));
  }

  // jbig-kit 2.2's pbmtojbg wrote these (single layer); its jbgtopbm decoded them to the .expected.pbm beside them.
  [TestCase("jbig-atmove", TestName = "jbig-kit: adaptive pixel moved mid-image")]
  [TestCase("jbig-twoline-reset", TestName = "jbig-kit: two-line template, every stripe resetting its state")]
  [TestCase("jbig-newlen-comment", TestName = "jbig-kit: comment and a shortened image")]
  [Category("Integration")]
  public void DecodesWhatJbigKitDecodes(string name) {
    var (rows, width, height) = LuraDocumentJbig.Decode(LuraDocumentFixtures.Bytes(name + ".jbg"));
    var expected = LuraDocumentFixtures.Pnm(LuraDocumentFixtures.Bytes(name + ".expected.pbm"));
    Assert.Multiple(() => {
      Assert.That(width, Is.EqualTo(expected.Width));
      Assert.That(height, Is.EqualTo(expected.Height));
      Assert.That(rows, Is.EqualTo(expected.Pixels));
    });
  }

  [TestCase(1, 1)]
  [TestCase(9, 3)]
  [TestCase(64, 48)]
  [TestCase(301, 203)]
  [Category("Unit")]
  public void EncodedMasksDecodeToThemselves(int width, int height) {
    var stride = (width + 7) >> 3;
    var rows = new byte[stride * height];
    var random = new Random(width * 7919 + height);
    for (var y = 0; y < height; ++y) {
      // Repeated lines exercise typical prediction, sparse ones the arithmetic coder's long runs.
      if (y > 0 && y % 5 == 0) {
        Array.Copy(rows, (y - 1) * stride, rows, y * stride, stride);
        continue;
      }

      for (var x = 0; x < width; ++x)
        if (random.Next(y % 3 == 0 ? 2 : 17) == 0)
          rows[y * stride + (x >> 3)] |= (byte)(0x80 >> (x & 7));
    }

    var (decoded, w, h) = LuraDocumentJbig.Decode(LuraDocumentJbig.Encode(rows, width, height));
    Assert.Multiple(() => {
      Assert.That((w, h), Is.EqualTo((width, height)));
      Assert.That(decoded, Is.EqualTo(rows));
    });
  }

  [Test]
  [Category("Unit")]
  public void DifferentialLayersAreRefusedByName() {
    var bie = LuraDocumentJbig.Encode(new byte[8], 8, 8);
    bie[1] = 1;
    Assert.That(() => LuraDocumentJbig.Decode(bie), Throws.TypeOf<InvalidDataException>().With.Message.Contains("differential"));
  }

  [Test]
  [Category("Unit")]
  public void TruncatedMaskIsRefused() {
    var bie = LuraDocumentJbig.Encode(new byte[8 * 40], 64, 40);
    Assert.That(() => LuraDocumentJbig.Decode(bie[..^2]), Throws.TypeOf<InvalidDataException>());
  }

  [Test]
  [Category("Unit")]
  public void ScramblingIsItsOwnInverse() {
    var data = new byte[50];
    new Random(4).NextBytes(data);
    var copy = data[..];
    var seed = LuraDocumentMask.Seed(1200, 160, 120);
    LuraDocumentMask.Apply(copy, seed);
    Assert.That(copy, Is.Not.EqualTo(data));
    LuraDocumentMask.Apply(copy, seed);
    Assert.That(copy, Is.EqualTo(data));
  }

  [Test]
  [Category("Unit")]
  public void ScramblingMatchesLuraTechsKeystream() {
    // The plug-in's mask for a 160x120 page at 1200 dpi starts 69 F7 1D D6 over a header that starts 00 00 01 00.
    var header = new byte[] { 0x69, 0xF7, 0x1D, 0xD6 };
    LuraDocumentMask.Apply(header, LuraDocumentMask.Seed(1200, 160, 120));
    Assert.That(header, Is.EqualTo(new byte[] { 0x00, 0x00, 0x01, 0x00 }));
  }
}
