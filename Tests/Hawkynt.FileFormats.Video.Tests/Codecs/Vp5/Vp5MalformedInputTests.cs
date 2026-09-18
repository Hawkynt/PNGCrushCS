using System;
using System.IO;
using System.Linq;

namespace FileFormat.Codecs.Vp5.Tests;

/// <summary>
/// What the VP5 decoder refuses, and the boundaries either side of each refusal.
/// </summary>
/// <remarks>
/// Every frame here is built rather than captured, because the interesting inputs are the ones no
/// encoder writes: a picture with no area, a bitstream version that does not exist, a display size
/// larger than the coded one. <see cref="Vp5BoolWriter"/> writes the header those need, and the first
/// test in this file is the one that says it may be believed.
/// <para/>
/// Each refusal is paired with the value one step inside it, so that a test fails when a bound moves
/// in either direction rather than only when it disappears.
/// </remarks>
[TestFixture]
public sealed class Vp5MalformedInputTests {

  /// <summary>
  /// The writer these tests are built on is the decoder's inverse, and here is the proof of it.
  /// </summary>
  [Test]
  [Category("Unit")]
  public void TheHeaderWriterAndTheRangeDecoderAreInverses() {
    var writer = new Vp5BoolWriter();
    var random = new Random(20060101);
    var bits = Enumerable.Range(0, 400).Select(_ => (Bit: random.Next(2), Probability: 1 + random.Next(254))).ToArray();
    foreach (var (bit, probability) in bits)
      writer.WriteBool(bit, probability);

    var decoder = new Vp5RangeDecoder(writer.Finish());
    foreach (var (bit, probability) in bits)
      Assert.That(decoder.ReadBool(probability), Is.EqualTo(bit));
  }

  // ==============================================================================================
  // The frame header
  // ==============================================================================================

  [Test]
  [Category("Unit")]
  public void APacketWithNoBytesAtAllIsRefused() {
    var failure = Assert.Throws<InvalidDataException>(() => new Vp5Decoder().Decode(ReadOnlyMemory<byte>.Empty));
    Assert.That(failure!.Message, Does.Contain("no range-coder bytes"));
  }

  [Test]
  [Category("Unit")]
  public void AStreamThatBeginsWithAnInterFrameIsRefused() {
    var failure = Assert.Throws<InvalidDataException>(() => new Vp5Decoder().Decode(_InterFrameHeader()));
    Assert.That(failure!.Message, Does.Contain("begins with an inter frame"));
  }

  [TestCase(0, 19)]
  [TestCase(32, 0)]
  [TestCase(0, 0)]
  [Category("Unit")]
  public void AKeyFrameStatingAPictureWithNoAreaIsRefused(int columns, int rows) {
    var failure = Assert.Throws<InvalidDataException>(
      () => new Vp5Decoder().Decode(_KeyFrameHeader(columns: columns, rows: rows)));
    Assert.That(failure!.Message, Does.Contain("no area"));
  }

  [TestCase(6)]
  [TestCase(31)]
  [Category("Unit")]
  public void AKeyFrameStatingABitstreamVersionThatDoesNotExistIsRefused(int version) {
    var failure = Assert.Throws<InvalidDataException>(
      () => new Vp5Decoder().Decode(_KeyFrameHeader(majorVersion: version)));
    Assert.That(failure!.Message, Does.Contain("bitstream version"));
  }

  /// <summary>
  /// The versions either side of the refusal, which have to get past the header and stop later.
  /// </summary>
  /// <remarks>
  /// "Later" is the point: these headers have no coefficient data behind them, so the only thing that
  /// can be asserted is which complaint arrives. A version the decoder accepted and then stopped on
  /// for the stated reason has been accepted; one it refused by name has not.
  /// </remarks>
  [TestCase(0)]
  [TestCase(5)]
  [Category("Unit")]
  public void TheDefinedBitstreamVersionsAreNotRefused(int version)
    => Assert.That(
      () => new Vp5Decoder().Decode(_KeyFrameHeader(majorVersion: version)),
      Throws.TypeOf<InvalidDataException>().With.Message.Contains("ran out"));

  [TestCase(33, 19)]
  [TestCase(32, 20)]
  [TestCase(0, 19)]
  [TestCase(32, 0)]
  [Category("Unit")]
  public void AKeyFrameWhoseDisplaySizeDoesNotFitItsCodedSizeIsRefused(int displayColumns, int displayRows) {
    var failure = Assert.Throws<InvalidDataException>(() => new Vp5Decoder().Decode(
      _KeyFrameHeader(displayColumns: displayColumns, displayRows: displayRows)));
    Assert.That(failure!.Message, Does.Contain("display size"));
  }

  [Test]
  [Category("Unit")]
  public void ADisplaySizeEqualToTheCodedSizeIsTheBoundaryAndIsAllowed()
    => Assert.That(
      () => new Vp5Decoder().Decode(_KeyFrameHeader(displayColumns: 32, displayRows: 19)),
      Throws.TypeOf<InvalidDataException>().With.Message.Contains("ran out"));

  /// <summary>
  /// A header with nothing behind it runs the coder off the end, and that has to be said out loud.
  /// </summary>
  /// <remarks>
  /// This is the case a decoder is most tempted to paper over. The range coder answers every question
  /// it is asked whether or not there is anything left to answer with, so a truncated frame does not
  /// announce itself: it produces a picture. The only thing that separates that picture from a real
  /// one is the count of how far past the end the coder has read.
  /// </remarks>
  [Test]
  [Category("Unit")]
  public void AKeyFrameWithNoCoefficientDataBehindItIsRefusedRatherThanDecoded() {
    var failure = Assert.Throws<InvalidDataException>(() => new Vp5Decoder().Decode(_KeyFrameHeader()));
    Assert.That(failure!.Message, Does.Contain("ran out"));
  }

  /// <summary>
  /// A real key frame cut short at every length, none of which may fail in a way a caller cannot name.
  /// </summary>
  /// <remarks>
  /// A truncated frame is the ordinary case for a damaged file, and the decoder is free to refuse it
  /// or, where the coder happened to land somewhere coherent, to decode it. What it may not do is
  /// escape with an index or a null: those say nothing to a caller and cannot be handled, which for a
  /// reader handed arbitrary bytes is the same as a crash.
  /// </remarks>
  [Test]
  [Category("Unit")]
  public void EveryTruncationOfARealKeyFrameIsEitherDecodedOrRefusedByName() {
    var whole = Vp5Fixtures.Packets(Vp5Fixtures.SIXTY_FRAMES).First();

    for (var length = 1; length < whole.Length; length += 37) {
      var truncated = whole[..length];
      var decoder = new Vp5Decoder();

      try {
        decoder.Decode(truncated);
      } catch (InvalidDataException) {
        // A refusal by name is one of the two acceptable outcomes.
      } catch (Exception exception) {
        Assert.Fail(
          $"a key frame truncated to {length} of {whole.Length} bytes threw "
          + $"{exception.GetType().Name}, which a caller cannot handle: {exception.Message}");
      }
    }
  }

  // ==============================================================================================

  /// <summary>An inter frame header, which is all of an inter frame the decoder reads before refusing.</summary>
  private static byte[] _InterFrameHeader() {
    var writer = new Vp5BoolWriter();
    writer.WriteFlag(1);
    writer.WriteFlag(0);
    writer.WriteLiteral(20, 6);

    return writer.Finish();
  }

  /// <summary>A complete and otherwise valid key frame header, with no coefficient data behind it.</summary>
  private static byte[] _KeyFrameHeader(
    int quantizer = 20,
    int majorVersion = 5,
    bool interlaced = false,
    int columns = 32,
    int rows = 19,
    int displayColumns = 32,
    int displayRows = 19) {
    var writer = new Vp5BoolWriter();
    writer.WriteFlag(0);
    writer.WriteFlag(0);
    writer.WriteLiteral(quantizer, 6);
    writer.WriteLiteral(0, 8);
    writer.WriteLiteral(majorVersion, 5);
    writer.WriteLiteral(0, 2);
    writer.WriteFlag(interlaced ? 1 : 0);
    writer.WriteLiteral(rows, 8);
    writer.WriteLiteral(columns, 8);
    writer.WriteLiteral(displayRows, 8);
    writer.WriteLiteral(displayColumns, 8);
    writer.WriteLiteral(0, 2);

    return writer.Finish();
  }
}
