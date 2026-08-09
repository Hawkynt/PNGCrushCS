using System;
using System.IO;
using FileFormat.Core;
using FileFormat.Jpeg;
using FileFormat.Png;

namespace FileFormat.PowerPoint.Tests;

/// <summary>
/// The picture inside a PowerPoint presentation.
/// </summary>
/// <remarks>
/// XnView's converter sends both <c>.pps</c> and <c>.ppt</c> to one reader, and that reader never
/// opens the compound document's directory: it steps to offset 512 and walks OfficeArt record
/// headers, taking the first JPEG or PNG BLIP. Every expectation below was put to that converter on
/// a fixture built the same way as these, and it agreed on all ten — the three it read, and the
/// seven it refused for a record type, an instance or a container it does not follow.
/// </remarks>
[TestFixture]
public sealed class PowerPointTests {

  private const int _WIDTH = 5;
  private const int _HEIGHT = 4;

  private static RawImage _Picture(int width = _WIDTH, int height = _HEIGHT) {
    var pixels = new byte[width * height * 3];
    for (var y = 0; y < height; ++y)
      for (var x = 0; x < width; ++x) {
        var at = (y * width + x) * 3;
        pixels[at] = (byte)(x * 40 + 3);
        pixels[at + 1] = (byte)(y * 50 + 7);
        pixels[at + 2] = (byte)(x * y * 11 + 1);
      }

    return new() { Width = width, Height = height, Format = PixelFormat.Rgb24, PixelData = pixels };
  }

  private static byte[] _Png(int width = _WIDTH, int height = _HEIGHT)
    => PngWriter.ToBytes(PngFile.FromRawImage(_Picture(width, height)));

  private static byte[] _Jpeg() => JpegWriter.ToBytes(JpegFile.FromRawImage(_Picture()));

  /// <summary>An OfficeArt record: version and instance, type, length, and the data behind them.</summary>
  private static byte[] _Record(ushort versionAndInstance, ushort type, byte[] data, int checksumBytes = 16) {
    using var memory = new MemoryStream();
    memory.WriteByte((byte)versionAndInstance);
    memory.WriteByte((byte)(versionAndInstance >> 8));
    memory.WriteByte((byte)type);
    memory.WriteByte((byte)(type >> 8));

    var length = checksumBytes + 1 + data.Length;
    memory.WriteByte((byte)length);
    memory.WriteByte((byte)(length >> 8));
    memory.WriteByte((byte)(length >> 16));
    memory.WriteByte((byte)(length >> 24));

    for (var i = 0; i < checksumBytes; ++i)
      memory.WriteByte((byte)i);

    memory.WriteByte(0xFF);
    memory.Write(data, 0, data.Length);
    return memory.ToArray();
  }

  private static byte[] _PngBlip(byte[]? png = null)
    => _Record(PowerPointFile.PngBlipVersionAndInstance, PowerPointFile.PngBlipType, png ?? _Png());

  private static byte[] _JpegBlip() => _Record(PowerPointFile.JpegBlipVersionAndInstance, PowerPointFile.JpegBlipType, _Jpeg());

  /// <summary>A compound document whose data begins with the records given.</summary>
  private static byte[] _Document(params byte[][] records) {
    using var memory = new MemoryStream();
    var header = new byte[PowerPointFile.ScanStart];
    PowerPointFile.Signature.CopyTo(header);
    memory.Write(header, 0, header.Length);
    foreach (var record in records)
      memory.Write(record, 0, record.Length);

    return memory.ToArray();
  }

  /// <summary>A record carrying nothing, which the walk steps over by the length it states.</summary>
  private static byte[] _Filler() => _Record(0x0000, 0x0000, [], 8);

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => PowerPointReader.FromBytes(null!));

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".ppt"));
    Assert.Throws<FileNotFoundException>(() => PowerPointReader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException()
    => Assert.Throws<InvalidDataException>(() => PowerPointReader.FromBytes(new byte[64]));

  /// <summary>A picture on its own is not a presentation, however readable it is.</summary>
  [Test]
  [Category("Unit")]
  public void FromBytes_SomethingThatIsNotACompoundDocumentIsRefused()
    => Assert.Throws<InvalidDataException>(() => PowerPointReader.FromBytes(_Png(64, 64)));

  /// <summary>A presentation of nothing but shapes is refused rather than drawn empty.</summary>
  [Test]
  [Category("Unit")]
  public void FromBytes_ACompoundDocumentWithoutAPictureIsRefused()
    => Assert.Throws<InvalidDataException>(() => PowerPointReader.FromBytes(_Document(_Filler())));

  /// <summary>
  /// A PNG standing at offset 512 with no record around it is not a picture this format holds —
  /// which is what separates a presentation from the compound documents read by looking for a
  /// signature.
  /// </summary>
  [Test]
  [Category("Unit")]
  public void FromBytes_ABarePictureWithNoRecordAroundItIsRefused()
    => Assert.Throws<InvalidDataException>(() => PowerPointReader.FromBytes(_Document(_Png())));

  /// <summary>
  /// The instance decides as much as the record type does: a BLIP carrying two checksums puts its
  /// picture elsewhere, and XnView reads none of those.
  /// </summary>
  [Test]
  [Category("Unit")]
  public void FromBytes_APictureRecordOfAnInstanceNotReadIsRefused(
    [Values((ushort)0x6E10, (ushort)0x6E20)] ushort versionAndInstance)
    => Assert.Throws<InvalidDataException>(
      () => PowerPointReader.FromBytes(_Document(_Record(versionAndInstance, PowerPointFile.PngBlipType, _Png(), 32))));

  /// <summary>A record holding others is stepped over whole, so a picture inside one is not found.</summary>
  [Test]
  [Category("Unit")]
  public void FromBytes_APictureInsideAContainerRecordIsNotReached() {
    var inner = _PngBlip();
    var container = new byte[PowerPointFile.RecordHeaderSize + inner.Length];
    container[0] = 0x0F;                          // version 0xF, which is what a container states
    container[1] = 0x00;
    container[2] = 0x01;                          // 0xF001, the store every picture in a file sits in
    container[3] = 0xF0;
    container[4] = (byte)inner.Length;
    container[5] = (byte)(inner.Length >> 8);
    inner.CopyTo(container, PowerPointFile.RecordHeaderSize);

    Assert.Throws<InvalidDataException>(() => PowerPointReader.FromBytes(_Document(container)));
  }

  [Test]
  [Category("Integration")]
  public void FromBytes_ThePictureIsTheFirstBlipInTheWalk() {
    var read = PowerPointReader.FromBytes(_Document(_Filler(), _PngBlip()));

    Assert.Multiple(() => {
      Assert.That(read.Width, Is.EqualTo(_WIDTH));
      Assert.That(read.Height, Is.EqualTo(_HEIGHT));
    });
  }

  [Test]
  [Category("Integration")]
  public void FromBytes_AJpegBlipIsReadAsWellAsAPngOne() {
    var read = PowerPointReader.FromBytes(_Document(_JpegBlip()));

    Assert.Multiple(() => {
      Assert.That(read.Width, Is.EqualTo(_WIDTH));
      Assert.That(read.Height, Is.EqualTo(_HEIGHT));
    });
  }

  /// <summary>Two pictures in one presentation: the walk takes the one it reaches first.</summary>
  [Test]
  [Category("Integration")]
  public void FromBytes_TheFirstOfSeveralPicturesIsTheOneDrawn() {
    var expected = PixelConverter.Convert(PngFile.ToRawImage(PngReader.FromBytes(_Png(3, 2))), PixelFormat.Rgb24);

    var read = PowerPointReader.FromBytes(_Document(_PngBlip(_Png(3, 2)), _PngBlip()));

    Assert.Multiple(() => {
      Assert.That(read.Width, Is.EqualTo(3));
      Assert.That(read.Height, Is.EqualTo(2));
      Assert.That(read.PixelData, Is.EqualTo(expected.PixelData));
    });
  }

  [Test]
  [Category("Integration")]
  public void ToRawImage_EveryPixelComesBackAsItWasPutIn() {
    var expected = PixelConverter.Convert(PngFile.ToRawImage(PngReader.FromBytes(_Png())), PixelFormat.Rgb24);

    var image = PowerPointFile.ToRawImage(PowerPointReader.FromBytes(_Document(_PngBlip())));

    Assert.Multiple(() => {
      Assert.That(image.Format, Is.EqualTo(PixelFormat.Rgb24));
      Assert.That(image.PixelData, Is.EqualTo(expected.PixelData));
    });
  }
}
