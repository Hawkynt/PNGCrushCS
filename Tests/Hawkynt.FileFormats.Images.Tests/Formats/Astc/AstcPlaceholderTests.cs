using System;
using FileFormat.Core.BlockDecoders;

namespace FileFormat.Astc.Tests;

/// <summary>
/// What the ASTC decoder does with a block it cannot read.
/// </summary>
/// <remarks>
/// It used to fill such a block with magenta and say nothing, so a picture came back looking
/// decoded and the caller had no way to tell one apart from a real one. An ordinary ASTC file — the
/// kind almost every real one is — came out as a magenta sheet that counted as a success, and
/// converting it would have written the magenta out as though it were the picture.
/// <para/>
/// Only void-extent blocks, which hold a single colour, are decoded; the rest of ASTC is not
/// implemented. The count of blocks left undone is now returned so the readers can refuse the file.
/// </remarks>
[TestFixture]
public sealed class AstcPlaceholderTests {

  /// <summary>A void-extent block of one colour: the top six bits of the first byte all set.</summary>
  private static byte[] _VoidExtent(byte r, byte g, byte b, byte a) {
    var block = new byte[16];
    block[0] = 0xFC;
    block[9] = r;
    block[11] = g;
    block[13] = b;
    block[15] = a;
    return block;
  }

  [Test]
  [Category("Unit")]
  public void DecodeBlock_SaysItReadASingleColourBlock() {
    var output = new byte[4 * 4 * 4];

    Assert.That(AstcBlockDecoder.DecodeBlock(_VoidExtent(10, 20, 30, 40), 4, 4, output), Is.True);
    Assert.Multiple(() => {
      Assert.That(output[0], Is.EqualTo(10));
      Assert.That(output[1], Is.EqualTo(20));
      Assert.That(output[2], Is.EqualTo(30));
      Assert.That(output[3], Is.EqualTo(40));
    });
  }

  [Test]
  [Category("Unit")]
  public void DecodeBlock_SaysSoWhenItCannotReadOne() {
    // Anything without the void-extent marker needs the whole of ASTC, which is not implemented.
    var output = new byte[4 * 4 * 4];

    Assert.That(AstcBlockDecoder.DecodeBlock(new byte[16], 4, 4, output), Is.False);
  }

  [Test]
  [Category("Unit")]
  public void DecodeBlock_LeavesNoMagentaBehind() {
    var output = new byte[4 * 4 * 4];
    AstcBlockDecoder.DecodeBlock(new byte[16], 4, 4, output);

    for (var i = 0; i < 16; ++i)
      Assert.That((output[i * 4], output[i * 4 + 1], output[i * 4 + 2]), Is.Not.EqualTo(((byte)255, (byte)0, (byte)255)),
        $"pixel {i} was the placeholder colour");
  }

  [Test]
  [Category("Unit")]
  public void DecodeImage_CountsTheBlocksItCouldNotRead() {
    // Four blocks across one row of an 8x4 picture in 4x4 blocks: two readable, two not.
    var data = new byte[16 * 2];
    _VoidExtent(1, 2, 3, 4).CopyTo(data, 0);

    var output = new byte[8 * 4 * 4];
    var undecoded = AstcBlockDecoder.DecodeImage(data, 8, 4, 4, 4, output);

    Assert.That(undecoded, Is.EqualTo(1), "the second block is not a void extent");
  }

  [Test]
  [Category("Unit")]
  public void DecodeImage_ReportsNothingLeftWhenEveryBlockIsReadable() {
    var data = new byte[16 * 2];
    _VoidExtent(9, 9, 9, 255).CopyTo(data, 0);
    _VoidExtent(9, 9, 9, 255).CopyTo(data, 16);

    var output = new byte[8 * 4 * 4];

    Assert.That(AstcBlockDecoder.DecodeImage(data, 8, 4, 4, 4, output), Is.Zero);
  }
}
