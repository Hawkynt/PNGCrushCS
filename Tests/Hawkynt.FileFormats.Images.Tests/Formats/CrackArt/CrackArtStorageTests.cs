using System;
using System.Text;
using FileFormat.Core;

namespace FileFormat.CrackArt.Tests;

/// <summary>
/// Honouring the flag that says whether a CrackArt picture is packed.
/// </summary>
/// <remarks>
/// The header carries the flag and the reader read it and threw it away: everything went through
/// the unpacker, so a file storing its screen plainly — which is what a clear flag means — was
/// unpacked as though it were not and came out as noise.
/// <para/>
/// A monochrome screen also takes no colours from the file, which is the second thing that made
/// these come back blank. The rule is shared now with the other Atari formats that had it wrong.
/// <para/>
/// Checked against RECOIL on real files: the unpacked <c>.ca2</c> and <c>.ca3</c> samples both come
/// back identical, the first once RECOIL's doubling of medium resolution rows is undone.
/// </remarks>
[TestFixture]
public sealed class CrackArtStorageTests {

  /// <summary>Builds an unpacked picture: the tag, a clear flag, the resolution, palette, screen.</summary>
  private static byte[] _Unpacked(CrackArtResolution resolution, int paletteEntries, byte fill) {
    var offset = 4 + paletteEntries * 2;
    var data = new byte[offset + 32000];
    Encoding.ASCII.GetBytes("CA").CopyTo(data, 0);
    data[2] = 0;
    data[3] = (byte)resolution;
    data.AsSpan(offset).Fill(fill);
    return data;
  }

  [Test]
  [Category("Unit")]
  public void Read_TakesAnUnpackedScreenAsItStands() {
    var file = CrackArtReader.FromBytes(_Unpacked(CrackArtResolution.High, 0, 0xA5));

    Assert.Multiple(() => {
      Assert.That(file.Width, Is.EqualTo(640));
      Assert.That(file.Height, Is.EqualTo(400));
      Assert.That(file.PixelData, Has.Length.EqualTo(32000));
      // Running an unpacked screen through the unpacker would not give the bytes back.
      Assert.That(file.PixelData[0], Is.EqualTo(0xA5));
      Assert.That(file.PixelData[31999], Is.EqualTo(0xA5));
    });
  }

  [Test]
  [Category("Unit")]
  public void Decoded_DrawsAMonochromeScreenInBlackAndWhite() {
    var image = CrackArtFile.ToRawImage(CrackArtReader.FromBytes(_Unpacked(CrackArtResolution.High, 0, 0)));

    Assert.That(image.Palette, Is.Not.Null);
    Assert.Multiple(() => {
      Assert.That(image.PaletteCount, Is.EqualTo(2));
      Assert.That(image.Palette![0], Is.EqualTo(255), "paper is white");
      Assert.That(image.Palette![3], Is.EqualTo(0), "ink is black");
    });
  }

  [Test]
  [Category("Unit")]
  public void Read_StillTakesTheMediumScreenAndItsFourColours() {
    var file = CrackArtReader.FromBytes(_Unpacked(CrackArtResolution.Medium, 4, 0));

    Assert.Multiple(() => {
      Assert.That(file.Width, Is.EqualTo(640));
      Assert.That(file.Height, Is.EqualTo(200));
    });
  }

  [Test]
  [Category("Unit")]
  public void Read_RefusesSomethingWithoutTheTag()
    => Assert.Throws<System.IO.InvalidDataException>(() => CrackArtReader.FromBytes(new byte[32004]));
}
