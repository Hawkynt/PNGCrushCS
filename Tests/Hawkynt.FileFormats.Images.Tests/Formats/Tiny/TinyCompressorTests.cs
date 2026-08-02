using System;
using FileFormat.Tiny;

namespace FileFormat.Tiny.Tests;

/// <summary>
/// The Tiny coder and decoder, taken round the trip.
/// </summary>
/// <remarks>
/// These used to pass a plane count and a words-per-plane to both halves, which is the shape of the
/// invented scheme that used to be here: a single stream with the counts inline. A real file has two
/// blocks, so the coder now returns both and the decoder takes both.
/// </remarks>
[TestFixture]
public sealed class TinyCompressorTests {

  private static void _AssertSurvivesTheTrip(byte[] screen) {
    var (control, data) = TinyCompressor.Compress(screen);

    Assert.That(TinyCompressor.Decompress(control, data), Is.EqualTo(screen));
  }

  [Test]
  [Category("Unit")]
  public void RoundTrip_AllZeros() => _AssertSurvivesTheTrip(new byte[32000]);

  [Test]
  [Category("Unit")]
  public void RoundTrip_AllSame() {
    var original = new byte[32000];
    for (var i = 0; i < original.Length; i += 2) {
      original[i] = 0x12;
      original[i + 1] = 0x34;
    }

    _AssertSurvivesTheTrip(original);
  }

  [Test]
  [Category("Unit")]
  public void RoundTrip_MixedData() {
    var original = new byte[32000];
    for (var i = 0; i < original.Length; i += 2) {
      original[i] = (byte)((i / 2) >> 8 & 0xFF);
      original[i + 1] = (byte)((i / 2) & 0xFF);
    }

    _AssertSurvivesTheTrip(original);
  }

  [Test]
  [Category("Unit")]
  public void RoundTrip_DataThatNeverRepeats() {
    var original = new byte[32000];
    for (var i = 0; i < original.Length; i += 2) {
      original[i] = (byte)(i % 7);
      original[i + 1] = (byte)(i % 13);
    }

    _AssertSurvivesTheTrip(original);
  }

  [Test]
  [Category("Unit")]
  public void RoundTrip_RunsLongerThanAControlByteCanCount() {
    // Over 127 words of one value, which has to spill the count into the control block.
    var original = new byte[32000];
    for (var i = 20000; i < 32000; i += 2)
      original[i] = 0x5A;

    _AssertSurvivesTheTrip(original);
  }

  [Test]
  [Category("Unit")]
  public void Compressed_ShortensAScreenOfOneValue() {
    var original = new byte[32000];
    Array.Fill(original, (byte)0x3C);

    var (control, data) = TinyCompressor.Compress(original);

    Assert.That(control.Length + data.Length, Is.LessThan(500), "one repeated word is the best case there is");
  }
}
