using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using FileFormat.Core;
using FileFormat.PicturePublisher;

namespace FileFormat.PicturePublisher.Tests;

[TestFixture]
public sealed class PicturePublisherReaderTests {

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => PicturePublisherReader.FromFile(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".pp5"));
    Assert.Throws<FileNotFoundException>(() => PicturePublisherReader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => PicturePublisherReader.FromBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException() {
    Assert.Throws<InvalidDataException>(() => PicturePublisherReader.FromBytes(new byte[10]));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_AFileWithoutTheSignatureIsRefused() {
    var data = PicturePublisherFixture.Document();
    data[0] = (byte)'X';

    Assert.Throws<InvalidDataException>(() => PicturePublisherReader.FromBytes(data));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ARecordChainThatDoesNotLandOnTheEndIsRefused() {
    var data = new List<byte>(PicturePublisherFixture.Document());
    data.AddRange([(byte)0x01, (byte)0x00, (byte)0x00]);

    Assert.Throws<InvalidDataException>(() => PicturePublisherReader.FromBytes(data.ToArray()));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_AnObjectRectangleThatIsNotTheSizeOfItsRasterIsRefused() {
    // The rectangle and the raster state the size twice, and a reader that trusts only one of them
    // will place a picture wrongly on the canvas rather than refusing.
    var data = PicturePublisherFixture.Document(objectRight: 7);

    var thrown = Assert.Throws<InvalidDataException>(() => PicturePublisherReader.FromBytes(data));
    Assert.That(thrown!.Message, Does.Contain("rectangle"));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_AStripThatDoesNotInflateToItsStatedSizeIsRefused() {
    var data = PicturePublisherFixture.Document(shortStrip: true);

    Assert.Throws<InvalidDataException>(() => PicturePublisherReader.FromBytes(data));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ACompressionOtherThanZlibIsRefused() {
    var data = PicturePublisherFixture.Document(compression: 1);

    var thrown = Assert.Throws<InvalidDataException>(() => PicturePublisherReader.FromBytes(data));
    Assert.That(thrown!.Message, Does.Contain("compression"));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TheCanvasIsTheSizeTheHeaderStates() {
    var read = PicturePublisherReader.FromBytes(PicturePublisherFixture.Document());

    Assert.That(read.Width, Is.EqualTo(4));
    Assert.That(read.Height, Is.EqualTo(4));
    Assert.That(read.ObjectCount, Is.EqualTo(1));
    Assert.That(read.PixelData.Length, Is.EqualTo(4 * 4 * 3));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_AnObjectIsDrawnWhereItsRectangleSaysAndNowhereElse() {
    // The object is a 2x2 patch of pure red at (1,1) on a 4x4 canvas. Everything the object does
    // not cover has to stay paper white — a reader that draws the raster at the origin, which is
    // what ignoring the rectangle amounts to, fails this.
    var read = PicturePublisherReader.FromBytes(PicturePublisherFixture.Document());
    var pixels = read.PixelData;

    Assert.That(pixels[0], Is.EqualTo(255));
    Assert.That(pixels[1], Is.EqualTo(255));
    Assert.That(pixels[2], Is.EqualTo(255));

    var at = (1 * 4 + 1) * 3;
    Assert.That(pixels[at], Is.EqualTo(255));
    Assert.That(pixels[at + 1], Is.EqualTo(0));
    Assert.That(pixels[at + 2], Is.EqualTo(0));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_AMaskOfNothingLeavesTheCanvasAlone() {
    // The mask is what makes the difference between an object and the page, and a reader that
    // ignores it draws every layer as an opaque rectangle.
    var read = PicturePublisherReader.FromBytes(PicturePublisherFixture.Document(maskValue: 0));
    var at = (1 * 4 + 1) * 3;

    Assert.That(read.PixelData[at], Is.EqualTo(255));
    Assert.That(read.PixelData[at + 1], Is.EqualTo(255));
    Assert.That(read.PixelData[at + 2], Is.EqualTo(255));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_HalfAMaskIsHalfTheColour() {
    var read = PicturePublisherReader.FromBytes(PicturePublisherFixture.Document(maskValue: 128));
    var at = (1 * 4 + 1) * 3;

    Assert.That(read.PixelData[at], Is.EqualTo(255));
    Assert.That(read.PixelData[at + 1], Is.EqualTo(127).Within(1));
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_IsTheCompositedCanvas() {
    var image = PicturePublisherFile.ToRawImage(
      PicturePublisherReader.FromBytes(PicturePublisherFixture.Document()));

    Assert.That(image.Format, Is.EqualTo(PixelFormat.Rgb24));
    Assert.That(image.Width, Is.EqualTo(4));
    Assert.That(image.Height, Is.EqualTo(4));
  }
}

/// <summary>Builds documents in the shape the one sample has: a header, an object, a raster, a mask.</summary>
internal static class PicturePublisherFixture {

  internal static byte[] Document(
    int objectRight = 2, int compression = 213, bool shortStrip = false, int? maskValue = 255) {

    const int canvas = 4;
    const int width = 2;
    const int height = 2;

    var document = new List<byte>();
    document.AddRange("PPUBII"u8.ToArray());
    document.AddRange([(byte)0x00, (byte)0x02]);
    document.AddRange(_Int(34));
    document.AddRange([(byte)0x00, (byte)0x00, (byte)0x05, (byte)0x00, (byte)0x05, (byte)0x00]);
    document.AddRange(_Int(canvas));
    document.AddRange(_Int(canvas));
    document.AddRange(_Int(150));
    document.AddRange([(byte)0x03, (byte)0x00]);
    document.AddRange(new byte[16]);

    var header = new byte[106];
    _WriteInt(header, 38, 1);
    _WriteInt(header, 42, 1);
    _WriteInt(header, 46, objectRight);
    _WriteInt(header, 50, 2);
    _WriteInt(header, 54, 255);
    _Record(document, 1, header);

    var red = new byte[width * height * 3];
    for (var i = 0; i < red.Length; i += 3)
      red[i] = 255;

    _Record(document, 2, _Raster(width, height, 3, red, compression, shortStrip));

    if (maskValue is { } value) {
      var mask = new byte[width * height];
      Array.Fill(mask, (byte)value);
      _Record(document, 3, _Raster(width, height, 1, mask, 213, false));
    }

    return document.ToArray();
  }

  /// <summary>One record: four bytes of payload length, two of type, then the payload.</summary>
  private static void _Record(List<byte> document, ushort type, byte[] payload) {
    document.AddRange(_Int(payload.Length));
    document.AddRange([(byte)(type & 0xFF), (byte)(type >> 8)]);
    document.AddRange(payload);
  }

  /// <summary>The cut-down TIFF one raster record holds: a directory and one zlib strip.</summary>
  private static byte[] _Raster(int width, int height, int samples, byte[] pixels, int compression, bool shortStrip) {
    var tags = new List<(ushort Tag, ushort Type, int Count, int Value)>();

    var directoryAt = 8;
    var entries = 8;
    var afterDirectory = directoryAt + 2 + entries * 12 + 4;

    var bitsAt = afterDirectory;
    var stripAt = bitsAt + samples * 2;

    tags.Add((256, 4, 1, width));
    tags.Add((257, 4, 1, height));
    tags.Add((258, 3, samples, samples == 1 ? 8 : bitsAt));
    tags.Add((259, 3, 1, compression));
    tags.Add((262, 3, 1, samples == 1 ? 1 : 2));
    tags.Add((273, 4, 1, stripAt));
    tags.Add((277, 3, 1, samples));
    tags.Add((284, 3, 1, 1));

    var raster = new List<byte>();
    raster.AddRange("II"u8.ToArray());
    raster.AddRange([(byte)0x2A, (byte)0x00]);
    raster.AddRange(_Int(directoryAt));
    raster.AddRange([(byte)entries, (byte)0x00]);

    foreach (var (tag, type, count, value) in tags) {
      raster.AddRange([(byte)(tag & 0xFF), (byte)(tag >> 8), (byte)(type & 0xFF), (byte)(type >> 8)]);
      raster.AddRange(_Int(count));
      raster.AddRange(type == 3 ? [(byte)(value & 0xFF), (byte)(value >> 8), 0, 0] : _Int(value));
    }

    raster.AddRange(_Int(0));

    for (var i = 0; i < samples; ++i)
      raster.AddRange([(byte)0x08, (byte)0x00]);

    var deflated = _Deflate(shortStrip ? pixels.AsSpan(0, pixels.Length - 1).ToArray() : pixels);
    raster.AddRange(deflated);

    return raster.ToArray();
  }

  private static byte[] _Deflate(byte[] data) {
    using var output = new MemoryStream();
    using (var zlib = new ZLibStream(output, CompressionLevel.Optimal, leaveOpen: true))
      zlib.Write(data, 0, data.Length);

    return output.ToArray();
  }

  private static byte[] _Int(int value)
    => [(byte)value, (byte)(value >> 8), (byte)(value >> 16), (byte)(value >> 24)];

  private static void _WriteInt(byte[] target, int at, int value) {
    target[at] = (byte)value;
    target[at + 1] = (byte)(value >> 8);
    target[at + 2] = (byte)(value >> 16);
    target[at + 3] = (byte)(value >> 24);
  }
}
