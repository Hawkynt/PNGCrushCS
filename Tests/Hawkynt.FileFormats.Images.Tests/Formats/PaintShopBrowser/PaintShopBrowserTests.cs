using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using FileFormat.Core;
using FileFormat.Jpeg;
using FileFormat.PaintShopBrowser;

namespace FileFormat.PaintShopBrowser.Tests;

/// <summary>
/// No cache was available, so the fixtures are built byte by byte from the record layout that
/// Deark, jbfinspect and jbf2html independently agree on, with the malformed ones each breaking one
/// field.
/// </summary>
[TestFixture]
public sealed class PaintShopBrowserTests {

  private static void _U32(List<byte> into, uint value) => into.AddRange(BitConverter.GetBytes(value));

  /// <summary>A JPEG of a solid colour, to stand in for a cached thumbnail.</summary>
  private static byte[] _Thumbnail(int width, int height, byte red) {
    var pixels = new byte[width * height * 3];
    for (var i = 0; i < pixels.Length; i += 3)
      pixels[i] = red;

    var image = new RawImage { Width = width, Height = height, Format = PixelFormat.Rgb24, PixelData = pixels };

    return JpegWriter.ToBytes(JpegFile.FromRawImage(image));
  }

  /// <summary>One record: the name, the seven numbers about the original, the sentinel, the JPEG.</summary>
  private static byte[] _Record(string name, int width, int height, byte[] jpeg, uint sentinel = PaintShopBrowserFile.ThumbnailSentinel, uint? statedLength = null) {
    var record = new List<byte>();
    var bytes = Encoding.ASCII.GetBytes(name);
    _U32(record, (uint)bytes.Length);
    record.AddRange(bytes);

    record.AddRange(new byte[8]);
    _U32(record, 0x11);
    _U32(record, (uint)width);
    _U32(record, (uint)height);
    _U32(record, 24);
    _U32(record, (uint)(width * height * 3));
    _U32(record, (uint)jpeg.Length);

    _U32(record, 2);
    _U32(record, 1);
    _U32(record, sentinel);
    _U32(record, statedLength ?? (uint)jpeg.Length);
    record.AddRange(jpeg);

    return record.ToArray();
  }

  private static byte[] _Cache(int major, int minor, string directory, params byte[][] records)
    => _Cache(major, minor, directory, (uint)records.Length, records);

  private static byte[] _Cache(int major, int minor, string directory, uint statedCount, params byte[][] records) {
    var header = new byte[PaintShopBrowserFile.HeaderLength];
    Encoding.ASCII.GetBytes(PaintShopBrowserFile.Magic).CopyTo(header, 0);

    // The version is the one thing written most significant byte first.
    header[15] = (byte)(major >> 8);
    header[16] = (byte)major;
    header[17] = (byte)(minor >> 8);
    header[18] = (byte)minor;
    BitConverter.GetBytes(statedCount).CopyTo(header, 19);
    Encoding.ASCII.GetBytes(directory).CopyTo(header, 23);

    return header.Concat(records.SelectMany(r => r)).ToArray();
  }

  private static byte[] _Two() => _Cache(2, 0, @"C:\pictures",
    _Record("first.jpg", 640, 480, _Thumbnail(16, 12, 200)),
    _Record("second.png", 100, 100, _Thumbnail(8, 8, 40))
  );

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => PaintShopBrowserReader.FromBytes(null!));

  [Test]
  [Category("Unit")]
  public void FromBytes_WithoutTheOpeningLettersIsRefused()
    => Assert.Throws<InvalidDataException>(() => PaintShopBrowserReader.FromBytes(new byte[2048]));

  [Test]
  [Category("Unit")]
  public void FromBytes_ReadsEveryRecordAndWhatItSaysAboutTheOriginal() {
    var file = PaintShopBrowserReader.FromBytes(_Two());

    Assert.Multiple(() => {
      Assert.That(file.Version, Is.EqualTo((2, 0)));
      Assert.That(file.Directory, Is.EqualTo(@"C:\pictures"));
      Assert.That(file.Thumbnails, Has.Count.EqualTo(2));
      Assert.That(file.Thumbnails[0].Name, Is.EqualTo("first.jpg"));
      Assert.That(file.Thumbnails[0].Width, Is.EqualTo(640), "the size the cache records is the original's, not the thumbnail's");
      Assert.That(file.Thumbnails[1].Name, Is.EqualTo("second.png"));
    });
  }

  /// <summary>The cache is a folder of pictures, so every thumbnail in it is reachable.</summary>
  [Test]
  [Category("Unit")]
  public void ToRawImage_EachThumbnailIsAPictureOfItsOwn() {
    var file = PaintShopBrowserReader.FromBytes(_Two());

    Assert.Multiple(() => {
      Assert.That(PaintShopBrowserFile.ImageCount(file), Is.EqualTo(2));
      Assert.That(PaintShopBrowserFile.ToRawImage(file, 0).Width, Is.EqualTo(16));
      Assert.That(PaintShopBrowserFile.ToRawImage(file, 1).Width, Is.EqualTo(8));
      Assert.That(PaintShopBrowserFile.ToRawImage(file).Width, Is.EqualTo(16), "and the first is the one the file shows");
    });
  }

  /// <summary>
  /// The version numbers are the one thing in the file written the other way round. Read as little
  /// endian, a version 2 cache would say it was version 512.
  /// </summary>
  [Test]
  [Category("Unit")]
  public void FromBytes_TheVersionIsMostSignificantByteFirst() {
    var file = PaintShopBrowserReader.FromBytes(_Cache(2, 3, "d", _Record("a.jpg", 4, 4, _Thumbnail(4, 4, 9))));

    Assert.That(file.Version, Is.EqualTo((2, 3)));
  }

  /// <summary>
  /// A version 1 cache stores a bitmap coded against a palette that lives in the reader, and no
  /// file of that version was available to settle the layout of. Saying so beats reading it.
  /// </summary>
  [Test]
  [Category("Unit")]
  public void FromBytes_AVersionOneCacheIsRefusedByName()
    => Assert.That(
      Assert.Throws<InvalidDataException>(() => PaintShopBrowserReader.FromBytes(
        _Cache(1, 3, "d", _Record("a.jpg", 4, 4, _Thumbnail(4, 4, 9)))
      ))!.Message,
      Does.Contain("version 1.3")
    );

  [Test]
  [Category("Unit")]
  public void FromBytes_AVersionNeitherOneNorTwoIsRefused()
    => Assert.Throws<InvalidDataException>(() => PaintShopBrowserReader.FromBytes(
      _Cache(7, 0, "d", _Record("a.jpg", 4, 4, _Thumbnail(4, 4, 9)))
    ));

  /// <summary>
  /// One record follows another with nothing between them, so the four set bytes before a
  /// thumbnail are the only thing that says the last record ended where the reader thought it did.
  /// </summary>
  [Test]
  [Category("Unit")]
  public void FromBytes_ARecordWithoutItsSentinelIsRefused()
    => Assert.Throws<InvalidDataException>(() => PaintShopBrowserReader.FromBytes(
      _Cache(2, 0, "d", _Record("a.jpg", 4, 4, _Thumbnail(4, 4, 9), sentinel: 0x12345678))
    ));

  /// <summary>A length that runs past the end of the file was read from the wrong place.</summary>
  [Test]
  [Category("Unit")]
  public void FromBytes_AThumbnailLongerThanTheFileIsRefused()
    => Assert.Throws<InvalidDataException>(() => PaintShopBrowserReader.FromBytes(
      _Cache(2, 0, "d", _Record("a.jpg", 4, 4, _Thumbnail(4, 4, 9), statedLength: 1 << 20))
    ));

  /// <summary>
  /// The payload is a whole JPEG file. One that does not start as a JPEG does means the length was
  /// read from somewhere other than where the length is.
  /// </summary>
  [Test]
  [Category("Unit")]
  public void FromBytes_APayloadThatIsNotAJpegIsRefused() {
    var record = _Record("a.jpg", 4, 4, [1, 2, 3, 4, 5, 6, 7, 8]);

    Assert.That(
      Assert.Throws<InvalidDataException>(() => PaintShopBrowserReader.FromBytes(_Cache(2, 0, "d", record)))!.Message,
      Does.Contain("JPEG")
    );
  }

  /// <summary>The header says how many records follow, and a file that runs out before them is cut.</summary>
  [Test]
  [Category("Unit")]
  public void FromBytes_ACacheWithFewerRecordsThanItStatesIsRefused()
    => Assert.Throws<InvalidDataException>(() => PaintShopBrowserReader.FromBytes(
      _Cache(2, 0, "d", 4, _Record("a.jpg", 4, 4, _Thumbnail(4, 4, 9)))
    ));

  [Test]
  [Category("Unit")]
  public void FromBytes_ACacheOfAFolderWithNoPicturesInItIsNotAPicture()
    => Assert.Throws<InvalidDataException>(() => PaintShopBrowserReader.FromBytes(_Cache(2, 0, "d", 0)));
}
