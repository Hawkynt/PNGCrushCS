using System;
using System.IO;
using FileFormat.Codecs.Vqa;

namespace FileFormat.Codecs.Vqa.Tests;

/// <summary>
/// Westwood's format80 run-length scheme, on streams built here byte by byte.
/// </summary>
/// <remarks>
/// Three real files, 245 pictures between them, exercised every command this decompresses in real
/// codebook, palette and index-table chunks with no differing sample against ffmpeg's own decode — see
/// <see cref="Codecs.Tests.VqaVideoDecoderTests"/>. What that comparison cannot reach on demand is one
/// command at a time in isolation, and the two ways a stream can end: the documented <c>0x80</c> marker,
/// and simply running out of compressed bytes, which the format's own description says happens too.
/// </remarks>
[TestFixture]
public sealed class VqaFormat80Tests {

  [Test]
  [Category("Unit")]
  public void ALiteralRunIsCopiedStraightFromTheStream() {
    byte[] source = [0x85, (byte)'A', (byte)'B', (byte)'C', (byte)'D', (byte)'E', 0x80];

    var result = VqaFormat80.Decompress(source);

    Assert.That(result, Is.EqualTo("ABCDE"u8.ToArray()));
  }

  [Test]
  [Category("Unit")]
  public void AFillWritesOneByteRepeated() {
    byte[] source = [0xFE, 5, 0, (byte)'Z', 0x80];

    var result = VqaFormat80.Decompress(source);

    Assert.That(result, Is.EqualTo("ZZZZZ"u8.ToArray()));
  }

  [Test]
  [Category("Unit")]
  public void AShortBackReferenceWithOffsetOneRepeatsTheLastByte() {
    // Literal 'Q', then a short back-reference: count=3 (bits4-6 = 0 => 0+3), offset=1.
    byte[] source = [0x81, (byte)'Q', 0b0000_0000, 0x01, 0x80];

    var result = VqaFormat80.Decompress(source);

    Assert.That(result, Is.EqualTo("QQQQ"u8.ToArray()));
  }

  [Test]
  [Category("Unit")]
  public void ALongBackReferenceCopiesFromAnAbsolutePosition() {
    // Literal "ABCDEFGH", then a long back-reference: count=4 (6 bits=1 => 1+3), position=2 ("CDEF").
    byte[] source = [0x88, (byte)'A', (byte)'B', (byte)'C', (byte)'D', (byte)'E', (byte)'F', (byte)'G', (byte)'H', 0xC1, 2, 0, 0x80];

    var result = VqaFormat80.Decompress(source);

    Assert.That(result, Is.EqualTo("ABCDEFGHCDEF"u8.ToArray()));
  }

  [Test]
  [Category("Unit")]
  public void CommandFiveCopiesFromAnAbsolutePositionWithAWordCount() {
    byte[] source = [0x83, (byte)'W', (byte)'X', (byte)'Y', 0xFF, 2, 0, 1, 0, 0x80];

    var result = VqaFormat80.Decompress(source);

    Assert.That(result, Is.EqualTo("WXYXY"u8.ToArray()));
  }

  [Test]
  [Category("Unit")]
  public void DecompressionStopsAtTheZeroCountLiteralRunMarkerRatherThanReadingFurther() {
    byte[] source = [0x81, (byte)'A', 0x80, 0x81, (byte)'B'];

    var result = VqaFormat80.Decompress(source);

    Assert.That(result, Is.EqualTo("A"u8.ToArray()));
  }

  [Test]
  [Category("Unit")]
  public void AStreamWithNoEndMarkerDecompressesToItsOwnEnd() {
    byte[] source = [0x83, (byte)'X', (byte)'Y', (byte)'Z'];

    var result = VqaFormat80.Decompress(source);

    Assert.That(result, Is.EqualTo("XYZ"u8.ToArray()));
  }

  [Test]
  [Category("Unit")]
  public void AFixedSizeDestinationLeavesUnwrittenBytesAsZero() {
    byte[] source = [0x82, (byte)'A', (byte)'B', 0x80];

    var result = VqaFormat80.Decompress(source, 5);

    Assert.That(result, Is.EqualTo(new byte[] { (byte)'A', (byte)'B', 0, 0, 0 }));
  }

  [Test]
  [Category("Unit")]
  public void AFixedSizeDestinationTooSmallForTheStreamRefuses() {
    byte[] source = [0x84, (byte)'A', (byte)'B', (byte)'C', (byte)'D', 0x80];

    Assert.Throws<InvalidDataException>(() => VqaFormat80.Decompress(source, 2));
  }

  [Test]
  [Category("Unit")]
  public void AStreamTruncatedInsideACommandRefuses() {
    byte[] source = [0xFE, 5, 0]; // fill command missing its count's second byte and its value byte

    Assert.Throws<InvalidDataException>(() => VqaFormat80.Decompress(source));
  }
}
