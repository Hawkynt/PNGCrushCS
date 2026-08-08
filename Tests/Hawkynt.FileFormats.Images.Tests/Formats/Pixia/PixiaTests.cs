using System;
using System.Buffers.Binary;
using System.IO;
using System.Text;
using FileFormat.Core;
using FileFormat.Pixia;

namespace FileFormat.Pixia.Tests;

[TestFixture]
public sealed class PixiaTests {

  private static RawImage _Picture(int width, int height) {
    var pixels = new byte[width * height * 3];
    for (var i = 0; i < width * height; ++i) {
      pixels[i * 3] = (byte)(i * 17);
      pixels[i * 3 + 1] = (byte)(i * 5);
      pixels[i * 3 + 2] = (byte)(i * 2);
    }

    return new() { Width = width, Height = height, Format = PixelFormat.Rgb24, PixelData = pixels };
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => PixiaReader.FromBytes(null!));

  [Test]
  [Category("Unit")]
  public void FromBytes_WrongMagic_ThrowsInvalidDataException()
    => Assert.Throws<InvalidDataException>(() => PixiaReader.FromBytes(new byte[PixiaFile.PreviewAt + 16]));

  /// <summary>Version 1 stores its rows uncompressed under a layout one sample cannot settle, so it is
  /// refused rather than drawn as a guess.</summary>
  [Test]
  [Category("Unit")]
  public void FromBytes_TheUncompressedFormIsRefused() {
    var data = new byte[PixiaFile.PreviewAt + 16];
    Encoding.ASCII.GetBytes(PixiaFile.Signature).CopyTo(data, 0);
    BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(PixiaFile.VersionAt), 1);
    BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(PixiaFile.LayerCountAt), 1);

    Assert.Throws<InvalidDataException>(() => PixiaReader.FromBytes(data));
  }

  /// <summary>The layers run to the end of the file, which is what says the runs were read as the
  /// format means them rather than as far as something plausible.</summary>
  [Test]
  [Category("Unit")]
  public void FromBytes_TheLayersMustAccountForTheFile() {
    var data = PixiaWriter.ToBytes(PixiaFile.FromRawImage(_Picture(8, 4)));
    Array.Resize(ref data, data.Length + 1);

    Assert.Throws<InvalidDataException>(() => PixiaReader.FromBytes(data));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_MoreLayersThanTheTablesHold_ThrowsInvalidDataException() {
    var data = PixiaWriter.ToBytes(PixiaFile.FromRawImage(_Picture(8, 4)));
    BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(PixiaFile.LayerCountAt), PixiaFile.MaximumLayers + 1);

    Assert.Throws<InvalidDataException>(() => PixiaReader.FromBytes(data));
  }

  /// <summary>The preview is the canvas rescaled and is not the picture, so it is kept rather than
  /// drawn — the file states its length and the layers follow it.</summary>
  [Test]
  [Category("Unit")]
  public void FromBytes_ThePreviewIsKeptAndNotDrawn() {
    var bytes = PixiaWriter.ToBytes(PixiaFile.FromRawImage(_Picture(8, 4)));
    var file = PixiaReader.FromBytes(bytes);
    var stated = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(PixiaFile.HeaderSize));

    Assert.Multiple(() => {
      Assert.That(file.Preview, Has.Length.EqualTo(stated), "the stated length is the preview's");
      Assert.That(file.Width, Is.EqualTo(8), "and the size comes from the layer, not the preview");
    });
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_AHiddenLayerIsLeftOut() {
    var data = PixiaWriter.ToBytes(PixiaFile.FromRawImage(_Picture(8, 4)));
    BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(PixiaFile.PropertiesAt + PixiaFile.PropertyVisibleAt), 0);

    var decoded = PixiaFile.ToRawImage(PixiaReader.FromBytes(data));

    Assert.That(decoded.PixelData, Is.All.EqualTo(0xFF), "nothing drawn leaves the white it composites over");
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ThePictureComesBackExactly() {
    var original = _Picture(16, 9);
    var decoded = PixiaFile.ToRawImage(
      PixiaReader.FromBytes(PixiaWriter.ToBytes(PixiaFile.FromRawImage(original))));

    Assert.Multiple(() => {
      Assert.That(decoded.Width, Is.EqualTo(16));
      Assert.That(decoded.Height, Is.EqualTo(9));
      Assert.That(decoded.PixelData, Is.EqualTo(original.PixelData));
    });
  }
}
