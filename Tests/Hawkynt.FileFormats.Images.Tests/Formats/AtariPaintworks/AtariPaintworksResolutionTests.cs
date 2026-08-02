using System;
using System.Buffers.Binary;
using System.Text;
using FileFormat.Core;

namespace FileFormat.AtariPaintworks.Tests;

/// <summary>
/// Taking the resolution from the file rather than guessing it.
/// </summary>
/// <remarks>
/// It used to be answered from the file's length, which cannot say: the screen is the same 32000
/// bytes in all three resolutions, so every file measured the same and every one was called low. A
/// 640 by 400 picture came back 320 by 200, drawn from the wrong part of its own data, and counted
/// as a decode. The resolution is the long word the file opens with.
/// <para/>
/// High resolution is also monochrome — the Atari's palette registers do not colour it — so reading
/// the stored palette paints the ink whatever happens to have been left there.
/// <para/>
/// Checked against RECOIL on a real 640 by 400 file: all 256000 pixels match.
/// </remarks>
[TestFixture]
public sealed class AtariPaintworksResolutionTests {

  private static byte[] _Picture(int resolution, byte fill) {
    var data = new byte[128 + 32000];
    BinaryPrimitives.WriteInt32BigEndian(data.AsSpan(0), resolution);

    // Sixteen palette words, then the name field and the signature the format carries.
    for (var i = 0; i < 16; ++i)
      BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(4 + i * 2), (ushort)(0x0777 - i * 0x0011));

    Encoding.ASCII.GetBytes("ANvisionA").CopyTo(data, 54);
    data.AsSpan(128).Fill(fill);
    return data;
  }

  [Test]
  [Category("Unit")]
  public void Read_TakesLowResolutionFromTheHeader() {
    var file = AtariPaintworksReader.FromBytes(_Picture(0, 0));

    Assert.Multiple(() => {
      Assert.That(file.Width, Is.EqualTo(320));
      Assert.That(file.Height, Is.EqualTo(200));
    });
  }

  [Test]
  [Category("Unit")]
  public void Read_TakesMediumResolutionFromTheHeader() {
    var file = AtariPaintworksReader.FromBytes(_Picture(1, 0));

    Assert.Multiple(() => {
      Assert.That(file.Width, Is.EqualTo(640));
      Assert.That(file.Height, Is.EqualTo(200));
    });
  }

  [Test]
  [Category("Unit")]
  public void Read_TakesHighResolutionFromTheHeader() {
    // Every resolution makes a file of the same length, so length cannot be what decides this.
    var file = AtariPaintworksReader.FromBytes(_Picture(2, 0));

    Assert.Multiple(() => {
      Assert.That(file.Width, Is.EqualTo(640));
      Assert.That(file.Height, Is.EqualTo(400));
    });
  }

  [Test]
  [Category("Unit")]
  public void Decoded_DrawsHighResolutionInBlackAndWhiteWhateverThePaletteHolds() {
    // The stored palette starts near white, which a monochrome screen must not use as its ink.
    var image = AtariPaintworksFile.ToRawImage(AtariPaintworksReader.FromBytes(_Picture(2, 0)));

    Assert.That(image.Palette, Is.Not.Null);
    Assert.Multiple(() => {
      Assert.That(image.Palette![0], Is.EqualTo(255), "paper is white");
      Assert.That(image.Palette![3], Is.EqualTo(0), "ink is black");
      Assert.That(image.Palette![4], Is.EqualTo(0));
      Assert.That(image.Palette![5], Is.EqualTo(0));
    });
  }

  [Test]
  [Category("Unit")]
  public void Decoded_StillUsesTheStoredPaletteInLowResolution() {
    var image = AtariPaintworksFile.ToRawImage(AtariPaintworksReader.FromBytes(_Picture(0, 0)));

    Assert.That(image.PaletteCount, Is.EqualTo(16), "low resolution has sixteen colours of its own");
  }

  [Test]
  [Category("Unit")]
  public void Read_SaysACompressedFileIsCompressedAndNotTooSmall() {
    var data = _Picture(0, 0)[..5000];
    var failure = Assert.Throws<System.IO.InvalidDataException>(() => AtariPaintworksReader.FromBytes(data));

    Assert.That(failure!.Message, Does.Contain("compressed"));
  }
}
