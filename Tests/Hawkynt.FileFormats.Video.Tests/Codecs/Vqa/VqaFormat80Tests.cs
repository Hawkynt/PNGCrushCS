using System;
using System.IO;
using FileFormat.Codecs.Vqa;

namespace FileFormat.Codecs.Vqa.Tests;

/// <summary>Westwood's normal and HiColor-relative format80 schemes, on streams built byte by byte.</summary>
[TestFixture]
public sealed class VqaFormat80Tests {

  [Test]
  [Category("Unit")]
  public void ALiteralRunIsCopiedStraightFromTheStream() {
    byte[] source = [0x85, (byte)'A', (byte)'B', (byte)'C', (byte)'D', (byte)'E', 0x80];
    Assert.That(VqaFormat80.Decompress(source), Is.EqualTo("ABCDE"u8.ToArray()));
  }

  [Test]
  [Category("Unit")]
  public void AFillWritesOneByteRepeated() {
    byte[] source = [0xFE, 5, 0, (byte)'Z', 0x80];
    Assert.That(VqaFormat80.Decompress(source), Is.EqualTo("ZZZZZ"u8.ToArray()));
  }

  [Test]
  [Category("Unit")]
  public void AShortBackReferenceWithOffsetOneRepeatsTheLastByte() {
    byte[] source = [0x81, (byte)'Q', 0b0000_0000, 0x01, 0x80];
    Assert.That(VqaFormat80.Decompress(source), Is.EqualTo("QQQQ"u8.ToArray()));
  }

  [Test]
  [Category("Unit")]
  public void ALongBackReferenceCopiesFromAnAbsolutePosition() {
    byte[] source = [0x88, (byte)'A', (byte)'B', (byte)'C', (byte)'D', (byte)'E', (byte)'F', (byte)'G', (byte)'H', 0xC1, 2, 0, 0x80];
    Assert.That(VqaFormat80.Decompress(source), Is.EqualTo("ABCDEFGHCDEF"u8.ToArray()));
  }

  [Test]
  [Category("Unit")]
  public void ModifiedLongBackReferenceUsesDistanceFromTheWritePosition() {
    // After ABCD, command 3 writes three bytes from four bytes behind the current output pointer.
    byte[] source = [0x84, (byte)'A', (byte)'B', (byte)'C', (byte)'D', 0xC0, 4, 0, 0x80];
    Assert.That(VqaFormat80.DecompressRelative(source), Is.EqualTo("ABCDABC"u8.ToArray()));
  }

  [Test]
  [Category("Unit")]
  public void ModifiedCommandFiveAlsoUsesBackwardDistance() {
    byte[] source = [0x84, (byte)'A', (byte)'B', (byte)'C', (byte)'D', 0xFF, 4, 0, 4, 0, 0x80];
    Assert.That(VqaFormat80.DecompressRelative(source), Is.EqualTo("ABCDABCD"u8.ToArray()));
  }

  [Test]
  [Category("Unit")]
  public void CommandFiveCopiesFromAnAbsolutePositionWithAWordCount() {
    byte[] source = [0x83, (byte)'W', (byte)'X', (byte)'Y', 0xFF, 2, 0, 1, 0, 0x80];
    Assert.That(VqaFormat80.Decompress(source), Is.EqualTo("WXYXY"u8.ToArray()));
  }

  [Test]
  [Category("Unit")]
  public void LiteralWriterRoundTripsAcrossCommandLengthBoundaries() {
    var source = new byte[257];
    for (var i = 0; i < source.Length; ++i)
      source[i] = (byte)(i * 37 + 11);

    var compressed = VqaFormat80.CompressLiterals(source);

    Assert.Multiple(() => {
      Assert.That(compressed[^1], Is.EqualTo(0x80));
      Assert.That(VqaFormat80.Decompress(compressed), Is.EqualTo(source));
    });
  }

  [Test]
  [Category("Unit")]
  public void DecompressionStopsAtTheZeroCountLiteralRunMarkerRatherThanReadingFurther() {
    byte[] source = [0x81, (byte)'A', 0x80, 0x81, (byte)'B'];
    Assert.That(VqaFormat80.Decompress(source), Is.EqualTo("A"u8.ToArray()));
  }

  [Test]
  [Category("Unit")]
  public void AStreamWithNoEndMarkerDecompressesToItsOwnEnd() {
    byte[] source = [0x83, (byte)'X', (byte)'Y', (byte)'Z'];
    Assert.That(VqaFormat80.Decompress(source), Is.EqualTo("XYZ"u8.ToArray()));
  }

  [Test]
  [Category("Unit")]
  public void AFixedSizeDestinationLeavesUnwrittenBytesAsZero() {
    byte[] source = [0x82, (byte)'A', (byte)'B', 0x80];
    Assert.That(VqaFormat80.Decompress(source, 5), Is.EqualTo(new byte[] { (byte)'A', (byte)'B', 0, 0, 0 }));
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
    byte[] source = [0xFE, 5, 0];
    Assert.Throws<InvalidDataException>(() => VqaFormat80.Decompress(source));
  }

  [Test]
  [Category("Unit")]
  public void ModifiedReferenceBeforeStartRefuses() {
    byte[] source = [0x81, (byte)'A', 0xC0, 2, 0, 0x80];
    Assert.Throws<InvalidDataException>(() => VqaFormat80.DecompressRelative(source));
  }
}
