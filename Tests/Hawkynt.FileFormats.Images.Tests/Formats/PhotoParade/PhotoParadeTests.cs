using System;
using System.IO;
using System.Text;
using FileFormat.Core;
using FileFormat.PhotoParade;
using FileFormat.Png;

namespace FileFormat.PhotoParade.Tests;

[TestFixture]
public sealed class PhotoParadeTests {

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => PhotoParadeReader.FromBytes(null!));

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => PhotoParadeReader.FromFile(null!));

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException()
    => Assert.Throws<FileNotFoundException>(
      () => PhotoParadeReader.FromFile(new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".php"))));

  [Test]
  [Category("Unit")]
  public void FromStream_Null_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => PhotoParadeReader.FromStream(null!));

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException()
    => Assert.Throws<InvalidDataException>(() => PhotoParadeReader.FromBytes(new byte[8]));

  [Test]
  [Category("Unit")]
  public void FromBytes_ForeignFile_ThrowsInvalidDataException()
    => Assert.Throws<InvalidDataException>(() => PhotoParadeReader.FromBytes(_Png(2, 2)));

  [Test]
  [Category("Unit")]
  public void FromBytes_ScriptRatherThanSlideShow_ThrowsInvalidDataException() {
    // A .php that is a web page under the same name, which is what the extension usually means.
    var script = Encoding.ASCII.GetBytes("<?php echo 'not a slide show'; ?>\n");
    Assert.Throws<InvalidDataException>(() => PhotoParadeReader.FromBytes(script));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_DescribesNoPhotographs_ThrowsInvalidDataException()
    => Assert.Throws<InvalidDataException>(() => PhotoParadeReader.FromBytes(_Header()));

  [Test]
  [Category("Unit")]
  public void FromBytes_StatedCountDisagrees_ThrowsInvalidDataException() {
    var data = _Build([_Png(4, 3)], statedCount: 2);
    Assert.Throws<InvalidDataException>(() => PhotoParadeReader.FromBytes(data));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TakesThePhotographTheBlockPointsBackAt_NotTheThemeArtwork() {
    var theme = _Png(2, 2);
    var photograph = _Png(9, 7);

    var file = PhotoParadeReader.FromBytes(_Build([photograph], leading: theme));

    Assert.That(PhotoParadeFile.ImageCount(file), Is.EqualTo(1));
    Assert.That(file.Photographs[0].Embedded, Is.EqualTo(photograph));

    var raw = PhotoParadeFile.ToRawImage(file);
    Assert.That(raw.Width, Is.EqualTo(9));
    Assert.That(raw.Height, Is.EqualTo(7));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ReadsEveryPhotographAndItsTitle() {
    var file = PhotoParadeReader.FromBytes(_Build([_Png(4, 3), _Png(6, 5)]));

    Assert.That(PhotoParadeFile.ImageCount(file), Is.EqualTo(2));
    Assert.That(file.Photographs[0].Title, Is.EqualTo("Photograph 0"));
    Assert.That(file.Photographs[1].Title, Is.EqualTo("Photograph 1"));
    Assert.That(PhotoParadeFile.ToRawImage(file, 1).Width, Is.EqualTo(6));
  }

  private static byte[] _Header() {
    using var ms = new MemoryStream();
    _WriteBigEndian(ms, PhotoParadeFile.HeaderSize);
    ms.Write(Encoding.ASCII.GetBytes("XPB!"));
    _WriteBigEndian(ms, 3);
    ms.WriteByte(0);
    ms.WriteByte(3);
    ms.WriteByte(0x01);
    ms.WriteByte(0x97);
    ms.Write(Encoding.ASCII.GetBytes("PhP2"));
    _WriteBigEndian(ms, 0);
    return ms.ToArray();
  }

  /// <summary>
  /// The header, optional theme artwork, then each photograph followed by the block describing it,
  /// and the album block at the end stating how many there were.
  /// </summary>
  private static byte[] _Build(byte[][] photographs, byte[]? leading = null, int? statedCount = null) {
    using var ms = new MemoryStream();
    var header = _Header();
    ms.Write(header, 0, header.Length);

    if (leading != null)
      ms.Write(leading, 0, leading.Length);

    for (var index = 0; index < photographs.Length; ++index) {
      ms.Write(photographs[index], 0, photographs[index].Length);

      ms.Write(Encoding.ASCII.GetBytes("PNFO"));
      _WriteBigEndian(ms, 3);
      _WriteChunk(ms, "TITL", Encoding.ASCII.GetBytes($"Photograph {index}"));
      _WriteChunk(ms, "fini", []);
    }

    ms.Write(Encoding.ASCII.GetBytes("LBUM"));
    _WriteBigEndian(ms, 3);
    _WriteChunk(ms, "NUMP", _BigEndian(statedCount ?? photographs.Length));
    _WriteChunk(ms, "fini", []);

    return ms.ToArray();
  }

  private static void _WriteChunk(Stream stream, string tag, byte[] body) {
    stream.Write(Encoding.ASCII.GetBytes(tag));
    _WriteBigEndian(stream, body.Length);
    stream.Write(body, 0, body.Length);
  }

  private static byte[] _BigEndian(int value) => [(byte)(value >> 24), (byte)(value >> 16), (byte)(value >> 8), (byte)value];

  private static void _WriteBigEndian(Stream stream, int value) {
    var bytes = _BigEndian(value);
    stream.Write(bytes, 0, bytes.Length);
  }

  private static byte[] _Png(int width, int height) {
    var pixels = new byte[width * height * 3];
    for (var i = 0; i < pixels.Length; ++i)
      pixels[i] = (byte)(i * 7 % 256);

    return PngWriter.ToBytes(PngFile.FromRawImage(new RawImage {
      Width = width,
      Height = height,
      Format = PixelFormat.Rgb24,
      PixelData = pixels,
    }));
  }
}
