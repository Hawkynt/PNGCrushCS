using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using FileFormat.Krita;

namespace FileFormat.Krita.Tests;

[TestFixture]
public sealed class KritaWriterTests {

  [Test]
  [Category("Unit")]
  public void ToBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => KritaWriter.ToBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_IsValidZip() {
    var file = _BuildMinimalFile();
    var bytes = KritaWriter.ToBytes(file);

    using var ms = new MemoryStream(bytes);
    using var archive = new ZipArchive(ms, ZipArchiveMode.Read);
    Assert.That(archive.Entries.Count, Is.GreaterThan(0));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_HasMimetype() {
    var file = _BuildMinimalFile();
    var bytes = KritaWriter.ToBytes(file);

    using var ms = new MemoryStream(bytes);
    using var archive = new ZipArchive(ms, ZipArchiveMode.Read);
    var mimetypeEntry = archive.GetEntry("mimetype");
    Assert.That(mimetypeEntry, Is.Not.Null);

    using var stream = mimetypeEntry!.Open();
    using var reader = new StreamReader(stream, Encoding.UTF8);
    var text = reader.ReadToEnd();
    Assert.That(text, Is.EqualTo("application/x-krita"));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_HasMergedImagePng() {
    var file = _BuildMinimalFile();
    var bytes = KritaWriter.ToBytes(file);

    using var ms = new MemoryStream(bytes);
    using var archive = new ZipArchive(ms, ZipArchiveMode.Read);
    var mergedEntry = archive.GetEntry("mergedimage.png");
    Assert.That(mergedEntry, Is.Not.Null);
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_HasMaindocXml() {
    var file = _BuildMinimalFile();
    var bytes = KritaWriter.ToBytes(file);

    using var ms = new MemoryStream(bytes);
    using var archive = new ZipArchive(ms, ZipArchiveMode.Read);
    var maindocEntry = archive.GetEntry("maindoc.xml");
    Assert.That(maindocEntry, Is.Not.Null);

    using var stream = maindocEntry!.Open();
    using var reader = new StreamReader(stream, Encoding.UTF8);
    var xml = reader.ReadToEnd();
    Assert.That(xml, Does.Contain("<DOC"));
    Assert.That(xml, Does.Contain("<IMAGE"));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_MimetypeIsFirstEntry() {
    var file = _BuildMinimalFile();
    var bytes = KritaWriter.ToBytes(file);

    using var ms = new MemoryStream(bytes);
    using var archive = new ZipArchive(ms, ZipArchiveMode.Read);
    Assert.That(archive.Entries.First().FullName, Is.EqualTo("mimetype"));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_MaindocXmlContainsDimensions() {
    var file = new KritaFile {
      Width = 123,
      Height = 456,
      PixelData = new byte[123 * 456 * 4]
    };

    var bytes = KritaWriter.ToBytes(file);

    using var ms = new MemoryStream(bytes);
    using var archive = new ZipArchive(ms, ZipArchiveMode.Read);
    var maindocEntry = archive.GetEntry("maindoc.xml")!;
    using var stream = maindocEntry.Open();
    using var reader = new StreamReader(stream, Encoding.UTF8);
    var xml = reader.ReadToEnd();
    Assert.That(xml, Does.Contain("width=\"123\""));
    Assert.That(xml, Does.Contain("height=\"456\""));
  }

  private static KritaFile _BuildMinimalFile() => new() {
    Width = 2,
    Height = 2,
    PixelData = new byte[2 * 2 * 4]
  };
}
