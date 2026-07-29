using System;
using System.IO;
using FileFormat.Core;
using FileFormat.SevenuP;

namespace FileFormat.SevenuP.Tests;

[TestFixture]
public sealed class SevenuPTests {

  private const int _WIDTH = 64;
  private const int _HEIGHT = 48;

  private static SevenuPFile _Sample() {
    var cells = new byte[SevenuPFile.FileSizeFor(_WIDTH, _HEIGHT) - SevenuPFile.CellDataOffset];
    for (var i = 0; i < cells.Length; ++i)
      cells[i] = (byte)(i * 13 % 128);

    return new() { Width = _WIDTH, Height = _HEIGHT, CellData = cells };
  }

  [Test]
  [Category("Unit")]
  public void FileSize_IsHeaderPlusNineBytesPerCell() {
    // 8x6 cells of 9 bytes, after the 14-byte header.
    Assert.That(SevenuPFile.FileSizeFor(_WIDTH, _HEIGHT), Is.EqualTo(14 + 8 * 6 * 9));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_EmitsTheSignatureAndDimensions() {
    var bytes = SevenuPWriter.ToBytes(_Sample());

    Assert.Multiple(() => {
      Assert.That(bytes[..SevenuPFile.Signature.Length], Is.EqualTo(SevenuPFile.Signature.ToArray()));
      Assert.That(bytes[SevenuPFile.WidthOffset] | (bytes[SevenuPFile.WidthOffset + 1] << 8), Is.EqualTo(_WIDTH));
      Assert.That(bytes[SevenuPFile.HeightOffset] | (bytes[SevenuPFile.HeightOffset + 1] << 8), Is.EqualTo(_HEIGHT));
    });
  }

  [Test]
  [Category("Unit")]
  public void RoundTrip_PreservesDimensionsAndCells() {
    var original = _Sample();
    var restored = SevenuPReader.FromBytes(SevenuPWriter.ToBytes(original));

    Assert.Multiple(() => {
      Assert.That(restored.Width, Is.EqualTo(original.Width));
      Assert.That(restored.Height, Is.EqualTo(original.Height));
      Assert.That(restored.CellData, Is.EqualTo(original.CellData));
    });
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_RejectsDataWithoutTheHeader()
    => Assert.Throws<InvalidDataException>(() => SevenuPReader.FromBytes(new byte[64]));

  [Test]
  [Category("Unit")]
  public void FromBytes_RejectsTruncatedCellData() {
    var bytes = SevenuPWriter.ToBytes(_Sample());
    Assert.Throws<InvalidDataException>(() => SevenuPReader.FromBytes(bytes[..^9]));
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_ProducesTheStatedDimensions() {
    var raw = SevenuPFile.ToRawImage(_Sample());

    Assert.Multiple(() => {
      Assert.That(raw.Width, Is.EqualTo(_WIDTH));
      Assert.That(raw.Height, Is.EqualTo(_HEIGHT));
      Assert.That(raw.PaletteCount, Is.EqualTo(ZxSpectrumGraphics.PaletteEntryCount));
    });
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_RejectsDimensionsThatAreNotWholeCells() {
    var raw = new RawImage { Width = 60, Height = 48, Format = PixelFormat.Rgba32, PixelData = new byte[60 * 48 * 4] };

    Assert.Throws<ArgumentException>(() => SevenuPFile.FromRawImage(raw));
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_ProducesAConformantFile() {
    var data = new byte[_WIDTH * _HEIGHT * 4];
    for (var i = 0; i < data.Length; i += 4) {
      data[i] = (byte)(i % 251);
      data[i + 3] = 255;
    }

    var raw = new RawImage { Width = _WIDTH, Height = _HEIGHT, Format = PixelFormat.Rgba32, PixelData = data };

    Assert.That(SevenuPWriter.ToBytes(SevenuPFile.FromRawImage(raw)),
      Has.Length.EqualTo(SevenuPFile.FileSizeFor(_WIDTH, _HEIGHT)));
  }
}
