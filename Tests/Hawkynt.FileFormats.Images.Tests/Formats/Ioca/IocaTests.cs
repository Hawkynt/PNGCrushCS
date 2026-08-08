using System;
using System.Collections.Generic;
using System.IO;
using FileFormat.Core;
using FileFormat.Ioca;

namespace FileFormat.Ioca.Tests;

[TestFixture]
public sealed class IocaReaderTests {

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => IocaReader.FromFile(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".ica"));
    Assert.Throws<FileNotFoundException>(() => IocaReader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => IocaReader.FromBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException() {
    Assert.Throws<InvalidDataException>(() => IocaReader.FromBytes(new byte[4]));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_AFourByteSizeIsNotAnIocaImage() {
    // What this used to accept: any file at all, with its first four bytes taken as a width and a
    // height. A real IOCA image is a chain of MO:DCA structured fields — two bytes of length, the
    // introducer 0xD3, a three-byte type — and it states its size in an Image Size field. No file
    // has the four-byte header this once invented, and the writer beside it wrote the same
    // invention, so the two agreed and nothing else could read either.
    var data = new byte[] { 0x00, 0x08, 0x00, 0x02, 0xFF, 0xAA, 0x00, 0x00, 0x00, 0x00 };

    Assert.Throws<InvalidDataException>(() => IocaReader.FromBytes(data));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_AStructuredFieldChainThatCarriesNoPictureIsRefused() {
    var data = new List<byte>();
    IocaFixture.Field(data, 0xA8, 0xA8, []);
    IocaFixture.Field(data, 0xA9, 0xA8, []);

    Assert.Throws<InvalidDataException>(() => IocaReader.FromBytes(data.ToArray()));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_AChainThatDoesNotLandOnTheEndIsRefused() {
    // Four bytes of stub behind the last field, where the walk expects either nothing or another
    // field header. An Amiga music module under the name .mod fails on exactly this kind of check
    // rather than being drawn.
    var data = new List<byte>(IocaFixture.Document(2, 2, IocaFixture.G4OfTwoByTwo));
    data.AddRange([(byte)0x00, (byte)0x00, (byte)0x00, (byte)0x00]);

    Assert.Throws<InvalidDataException>(() => IocaReader.FromBytes(data.ToArray()));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_AFieldWithoutTheIntroducerIsRefused() {
    var data = IocaFixture.Document(2, 2, IocaFixture.G4OfTwoByTwo);
    data[2] = 0xD2;

    Assert.Throws<InvalidDataException>(() => IocaReader.FromBytes(data));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ACompressionOtherThanG4IsRefused() {
    var data = IocaFixture.Document(2, 2, IocaFixture.G4OfTwoByTwo, compression: 0x03);

    var thrown = Assert.Throws<InvalidDataException>(() => IocaReader.FromBytes(data));
    Assert.That(thrown!.Message, Does.Contain("0x03"));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_CodingShorterThanTheStatedHeightIsRefused() {
    // The Image Size field states four rows where the coding holds two, which is what a truncated
    // file gives — and what would otherwise draw two rows of picture over two rows of nothing.
    var data = IocaFixture.Document(2, 4, IocaFixture.G4OfTwoByTwo);

    Assert.Throws<InvalidDataException>(() => IocaReader.FromBytes(data));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_AWholeDocumentIsReadToTheSizeItStates() {
    var data = IocaFixture.Document(2, 2, IocaFixture.G4OfTwoByTwo);

    var read = IocaReader.FromBytes(data);

    Assert.That(read.Width, Is.EqualTo(2));
    Assert.That(read.Height, Is.EqualTo(2));
    Assert.That(read.PixelData.Length, Is.EqualTo(2));
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_ASetBitIsInk() {
    var data = IocaFixture.Document(2, 2, IocaFixture.G4OfTwoByTwo);

    var image = IocaFile.ToRawImage(IocaReader.FromBytes(data));

    Assert.That(image.Width, Is.EqualTo(2));
    Assert.That(image.Height, Is.EqualTo(2));
    Assert.That(image.Palette![0], Is.EqualTo(255));
    Assert.That(image.Palette![3], Is.EqualTo(0));
  }
}

[TestFixture]
public sealed class IocaRoundTripTests {

  [Test]
  [Category("Integration")]
  public void RoundTrip_TheDocumentTheWriterMakesIsOneTheReaderWalks() {
    var pixels = new byte[] { 0b1010_0000, 0b0101_0000, 0b1111_0000, 0b0000_0000 };
    var original = new IocaFile { Width = 4, Height = 4, PixelData = pixels };

    var bytes = IocaWriter.ToBytes(original);
    var restored = IocaReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(4));
    Assert.That(restored.Height, Is.EqualTo(4));
    Assert.That(restored.PixelData, Is.EqualTo(pixels));
  }

  [Test]
  [Category("Integration")]
  public void ToBytes_WritesAChainOfStructuredFieldsThatLandsOnTheEnd() {
    var original = new IocaFile { Width = 8, Height = 8, PixelData = new byte[8] };

    var bytes = IocaWriter.ToBytes(original);

    var at = 0;
    var fields = 0;
    while (at < bytes.Length) {
      Assert.That(bytes[at + 2], Is.EqualTo(0xD3));
      at += (bytes[at] << 8) | bytes[at + 1];
      ++fields;
    }

    Assert.That(at, Is.EqualTo(bytes.Length));
    Assert.That(fields, Is.GreaterThan(6));
  }
}

/// <summary>Builds MO:DCA documents in the shape the one real sample has.</summary>
internal static class IocaFixture {

  /// <summary>Two rows of two pixels, coded by this assembly's own Group 4 encoder.</summary>
  internal static byte[] G4OfTwoByTwo => FileFormat.Ccitt.CcittG4Encoder.Encode([0b1000_0000, 0b0100_0000], 2, 2);

  internal static void Field(List<byte> output, byte typeHigh, byte typeLow, byte[] payload) {
    var length = 8 + payload.Length;
    output.Add((byte)(length >> 8));
    output.Add((byte)length);
    output.Add(0xD3);
    output.Add(typeHigh);
    output.Add(typeLow);
    output.Add(0x00);
    output.Add(0x00);
    output.Add(0x00);
    output.AddRange(payload);
  }

  internal static byte[] Document(int width, int height, byte[] coded, byte compression = 0x82) {
    byte[] size = [
      0x00,
      0x07, 0xD0, 0x07, 0xD0,
      (byte)(width >> 8), (byte)width,
      (byte)(height >> 8), (byte)height,
    ];

    var content = new List<byte> { 0x70, 0x00, 0x91, 0x01, 0xFF, 0x94, 0x09 };
    content.AddRange(size);
    content.AddRange([0x95, 0x02, compression, 0x01, 0x96, 0x01, 0x01]);
    content.AddRange([0xFE, 0x92, (byte)(coded.Length >> 8), (byte)coded.Length]);
    content.AddRange(coded);
    content.AddRange([0x93, 0x00, 0x71, 0x00]);

    var document = new List<byte>();
    Field(document, 0xA8, 0xA8, []);
    Field(document, 0xA8, 0xAF, []);
    Field(document, 0xA8, 0xFB, []);
    Field(document, 0xA6, 0xFB, size);
    Field(document, 0xEE, 0xFB, content.ToArray());
    Field(document, 0xA9, 0xFB, []);
    Field(document, 0xA9, 0xAF, []);
    Field(document, 0xA9, 0xA8, []);

    return document.ToArray();
  }
}
