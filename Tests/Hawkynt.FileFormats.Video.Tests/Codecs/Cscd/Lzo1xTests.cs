using System;
using System.IO;

namespace FileFormat.Codecs.Cscd.Tests;

/// <summary>
/// LZO1X's own opcodes, checked directly against hand-built streams — the format CSCD's LZO path
/// rests on, verified separately from the codec that uses it.
/// </summary>
/// <remarks>
/// The decoder as a whole was cross-checked against three streams the <c>lzop</c> command-line tool
/// itself compressed — up to 80,000 bytes, covering long literal runs past the first byte's 238-byte
/// ceiling, long matches, and the variable-length escape every opcode class with an <c>L</c> field
/// falls back on — every one identical to the original input. What these tests add is the small,
/// hand-worked cases that pin down the one place the format's own documentation reads two ways: which
/// bits of the two-byte distance word are distance and which are state.
/// </remarks>
[TestFixture]
public class Lzo1xTests {

  [Test]
  [Category("Unit")]
  public void AFirstByteOfEighteenToTwentyOneCopiesThatManyLiteralsAndNothingElse() {
    // 21 - 17 = 4 literals, with nothing coded before them to predict from.
    var compressed = new byte[] { 21, 10, 20, 30, 40 };
    var result = Lzo1x.Decompress(compressed, 4);

    Assert.That(result, Is.EqualTo(new byte[] { 10, 20, 30, 40 }));
  }

  [Test]
  [Category("Unit")]
  public void AFirstByteOfTwentyTwoOrMoreCopiesByteMinusSeventeenLiterals() {
    var literals = new byte[10];
    for (var i = 0; i < literals.Length; ++i)
      literals[i] = (byte)(i + 1);

    var compressed = new byte[1 + literals.Length];
    compressed[0] = (byte)(17 + literals.Length);
    literals.CopyTo(compressed, 1);

    var result = Lzo1x.Decompress(compressed, literals.Length);

    Assert.That(result, Is.EqualTo(literals));
  }

  [Test]
  [Category("Unit")]
  public void AClassDMatchCopiesFromTheDistanceAndLengthItsBitsName() {
    // Four literal bytes, then a class-D instruction (01LDDDSS, opcode 0x60 = L=1,D=0,S=0): a
    // four-byte match one byte back, repeating the last literal byte four times.
    var compressed = new byte[] { 21, 10, 20, 30, 40, 0x60, 0x00 };
    var result = Lzo1x.Decompress(compressed, 8);

    Assert.That(result, Is.EqualTo(new byte[] { 10, 20, 30, 40, 40, 40, 40, 40 }));
  }

  [Test]
  [Category("Unit")]
  public void TheStateAfterAMatchIsTheLowTwoBitsOfTheDistanceWordNotTheHighTwo() {
    // Class C (32-63): opcode 33 (L=1) gives a fixed length of 3 with no extra length bytes. The
    // distance word that follows is 0x0004 (little-endian: 0x04, 0x00) — value 4. Reading state from
    // the low two bits (state = 4 & 3 = 0) and distance from the bits above them (4 >> 2 = 1, plus
    // the class's own +1, giving distance 2) reproduces what ffmpeg decodes; reading it the other way
    // round — state from the high two bits, distance from the low fourteen — gives distance 5 instead
    // and a picture that does not match.
    var literals = new byte[] { 5, 6, 7, 8 };
    var compressed = new byte[] { 21, 5, 6, 7, 8, 33, 0x04, 0x00 };
    var result = Lzo1x.Decompress(compressed, literals.Length + 3);

    Assert.That(result, Is.EqualTo(new byte[] { 5, 6, 7, 8, 7, 8, 7 }));
  }

  [Test]
  [Category("Unit")]
  public void RefusesAVersionOtherThanZero() {
    var compressed = new byte[] { 17, 1, 0, 0, 0 };
    var failure = Assert.Throws<NotSupportedException>(() => Lzo1x.Decompress(compressed, 4));
    Assert.That(failure!.Message, Does.Contain("version 1"));
  }

  [Test]
  [Category("Unit")]
  public void RefusesAMatchReachingBeforeTheStartOfThePicture() {
    // Four literal bytes are output first (op reaches 4), then a class-D match — opcode 0x70
    // (L=1, D=4, S=0) — names a distance of (0<<3)+4+1=5, one byte further back than anything has
    // been written yet.
    var compressed = new byte[] { 21, 1, 2, 3, 4, 0x70, 0x00 };
    var failure = Assert.Throws<InvalidDataException>(() => Lzo1x.Decompress(compressed, 8));
    Assert.That(failure!.Message, Does.Contain("reaches before the start"));
  }
}
