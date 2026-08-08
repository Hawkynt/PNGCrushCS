using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using FileFormat.ClipArtCatalog;
using FileFormat.CorelGallery.Tests;

namespace FileFormat.ClipArtCatalog.Tests;

[TestFixture]
public sealed class ClipArtCatalogTests {

  private static void _Chunk(List<byte> target, string tag, byte[] body) {
    target.AddRange(Encoding.ASCII.GetBytes(tag));
    target.AddRange(BitConverter.GetBytes(body.Length));
    target.AddRange(body);
    if ((target.Count & 1) != 0)
      target.Add(0);
  }

  private static byte[] _Catalogue(params string[] names) {
    var body = new List<byte>();
    body.AddRange(Encoding.ASCII.GetBytes("CLIP"));

    foreach (var name in names) {
      var form = new List<byte>();
      _Chunk(form, "CLIPINFO", Encoding.ASCII.GetBytes(name + "\0"));
      _Chunk(form, "PATH", "."u8.ToArray());
      _Chunk(form, "DIB ", CorelGalleryTests.Dib(8, 6));
      _Chunk(body, "FORM", form.ToArray());
    }

    var file = new List<byte>();
    file.AddRange(Encoding.ASCII.GetBytes("CAT "));
    file.AddRange(BitConverter.GetBytes(body.Count));
    file.AddRange(body);
    return file.ToArray();
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => ClipArtCatalogReader.FromBytes(null!));

  [Test]
  [Category("Unit")]
  public void FromBytes_WrongMagic_ThrowsInvalidDataException()
    => Assert.Throws<InvalidDataException>(() => ClipArtCatalogReader.FromBytes(new byte[64]));

  [Test]
  [Category("Unit")]
  public void FromBytes_ReadsOneThumbnailPerDrawingWithItsName() {
    var catalogue = ClipArtCatalogReader.FromBytes(_Catalogue("ape.pcx", "bird.pcx"));

    Assert.Multiple(() => {
      Assert.That(ClipArtCatalogFile.ImageCount(catalogue), Is.EqualTo(2));
      Assert.That(catalogue.Entries[0].Name, Is.EqualTo("ape.pcx"));
      Assert.That(catalogue.Entries[1].Name, Is.EqualTo("bird.pcx"));
      Assert.That(ClipArtCatalogFile.ToRawImage(catalogue, 1).Width, Is.EqualTo(8));
      Assert.That(ClipArtCatalogFile.ToRawImage(catalogue).Height, Is.EqualTo(6));
    });
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TheStatedLengthMustBeTheFilesLength() {
    var data = _Catalogue("ape.pcx");
    Array.Resize(ref data, data.Length + 1);

    Assert.Throws<InvalidDataException>(() => ClipArtCatalogReader.FromBytes(data));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_AChunkThatOverrunsItsCatalogue_ThrowsInvalidDataException() {
    var data = _Catalogue("ape.pcx");
    // The outermost FORM claims more than the catalogue holds.
    BitConverter.GetBytes(data.Length).CopyTo(data, 16);

    Assert.Throws<InvalidDataException>(() => ClipArtCatalogReader.FromBytes(data));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ACatalogueWithNoThumbnails_ThrowsInvalidDataException()
    => Assert.Throws<InvalidDataException>(() => ClipArtCatalogReader.FromBytes(_Catalogue()));
}
