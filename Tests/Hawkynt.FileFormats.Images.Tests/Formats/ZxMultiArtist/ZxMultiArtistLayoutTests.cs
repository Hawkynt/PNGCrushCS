using System;
using System.IO;
using System.Text;
using FileFormat.Core;

namespace FileFormat.ZxMultiArtist.Tests;

/// <summary>
/// The shape a MultiArtist file actually has: a header, then two frames.
/// </summary>
/// <remarks>
/// What was assumed here before was a single frame with no header at all, its mode guessed from the
/// file's length. A real file opens with <c>MGH</c>, states its mode in the header, and carries both
/// bitmaps followed by both sets of attributes — so nothing real could be opened, and the guess from
/// length could only match a file this library had written itself.
/// <para/>
/// The two frames are shown one after the other fast enough to blend, which is how the picture holds
/// more colours in a cell than the hardware allows; drawing only the first gives the wrong colour
/// everywhere they differ.
/// <para/>
/// Checked against RECOIL on real files: the <c>.mg2</c>, <c>.mg4</c> and <c>.mg8</c> samples all
/// come back byte-identical.
/// </remarks>
[TestFixture]
public sealed class ZxMultiArtistLayoutTests {

  private static byte[] _Build(ZxMultiArtistMode mode, byte firstBitmap, byte secondBitmap, byte firstAttribute, byte secondAttribute) {
    var attributeSize = 768 * (8 / (int)mode);
    var data = new byte[256 + 2 * (6144 + attributeSize)];
    Encoding.ASCII.GetBytes("MGH").CopyTo(data, 0);
    data[3] = 1;
    data[4] = (byte)mode;

    data.AsSpan(256, 6144).Fill(firstBitmap);
    data.AsSpan(256 + 6144, 6144).Fill(secondBitmap);
    data.AsSpan(256 + 12288, attributeSize).Fill(firstAttribute);
    data.AsSpan(256 + 12288 + attributeSize, attributeSize).Fill(secondAttribute);

    return data;
  }

  [Test]
  [Category("Unit")]
  public void Read_TakesTheModeFromTheHeaderAndNotTheLength() {
    foreach (var mode in new[] { ZxMultiArtistMode.Mg2, ZxMultiArtistMode.Mg4, ZxMultiArtistMode.Mg8 }) {
      var file = ZxMultiArtistReader.FromBytes(_Build(mode, 0xFF, 0xFF, 0x07, 0x07));

      Assert.Multiple(() => {
        Assert.That(file.Mode, Is.EqualTo(mode));
        Assert.That(file.BitmapData, Has.Length.EqualTo(6144));
        Assert.That(file.SecondBitmapData, Has.Length.EqualTo(6144));
        Assert.That(file.AttributeData, Has.Length.EqualTo(768 * (8 / (int)mode)));
        Assert.That(file.SecondAttributeData, Has.Length.EqualTo(file.AttributeData.Length));
      });
    }
  }

  [Test]
  [Category("Unit")]
  public void Read_RefusesSomethingWithoutTheSignature()
    => Assert.Throws<InvalidDataException>(() => ZxMultiArtistReader.FromBytes(new byte[14080]));

  [Test]
  [Category("Unit")]
  public void Read_RefusesAFileShorterThanItsModeRequires() {
    var data = _Build(ZxMultiArtistMode.Mg8, 0, 0, 0, 0);

    Assert.Throws<InvalidDataException>(() => ZxMultiArtistReader.FromBytes(data[..(data.Length - 100)]));
  }

  [Test]
  [Category("Integration")]
  public void Decoded_BlendsTheTwoFramesRatherThanShowingOnlyTheFirst() {
    // Every pixel set in both frames, ink white in the first and black in the second.
    var file = ZxMultiArtistReader.FromBytes(_Build(ZxMultiArtistMode.Mg8, 0xFF, 0xFF, 0x47, 0x40));
    var image = ZxMultiArtistFile.ToRawImage(file);

    Assert.Multiple(() => {
      // White against black averages to the midpoint; drawing one frame alone gives an extreme.
      Assert.That(image.PixelData[0], Is.EqualTo(127).Within(1));
      Assert.That(image.PixelData[1], Is.EqualTo(127).Within(1));
      Assert.That(image.PixelData[2], Is.EqualTo(127).Within(1));
    });
  }

  [Test]
  [Category("Integration")]
  public void Decoded_UsesTheHardwareColourAndNotARoundedOne() {
    // Ink blue in both frames, so no blending is involved and the value is the palette's own.
    var image = ZxMultiArtistFile.ToRawImage(ZxMultiArtistReader.FromBytes(_Build(ZxMultiArtistMode.Mg8, 0xFF, 0xFF, 0x01, 0x01)));

    Assert.That(image.PixelData[2], Is.EqualTo(0xCD), "the Spectrum's dim channel is 205");
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_KeepsBothFrames() {
    var original = ZxMultiArtistReader.FromBytes(_Build(ZxMultiArtistMode.Mg4, 0xA5, 0x5A, 0x21, 0x12));
    var restored = ZxMultiArtistReader.FromBytes(ZxMultiArtistWriter.ToBytes(original));

    Assert.Multiple(() => {
      Assert.That(restored.BitmapData, Is.EqualTo(original.BitmapData));
      Assert.That(restored.SecondBitmapData, Is.EqualTo(original.SecondBitmapData));
      Assert.That(restored.AttributeData, Is.EqualTo(original.AttributeData));
      Assert.That(restored.SecondAttributeData, Is.EqualTo(original.SecondAttributeData));
    });
  }

  [Test]
  [Category("Integration")]
  public void Written_BeginsWithTheSignatureAndTheMode() {
    var file = ZxMultiArtistReader.FromBytes(_Build(ZxMultiArtistMode.Mg2, 1, 2, 3, 4));
    var bytes = ZxMultiArtistWriter.ToBytes(file);

    Assert.Multiple(() => {
      Assert.That(Encoding.ASCII.GetString(bytes, 0, 3), Is.EqualTo("MGH"));
      Assert.That(bytes[4], Is.EqualTo(2));
      Assert.That(bytes, Has.Length.EqualTo(256 + 2 * (6144 + 3072)));
    });
  }
}
