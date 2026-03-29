using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using FileFormat.OpenRaster;

namespace FileFormat.OpenRaster.Tests;

[TestFixture]
public sealed class OpenRasterWriterTests {

  [Test]
  [Category("Unit")]
  public void ToBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => OpenRasterWriter.ToBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_IsValidZip() {
    var file = _BuildMinimalFile();
    var bytes = OpenRasterWriter.ToBytes(file);

    using var ms = new MemoryStream(bytes);
    using var archive = new ZipArchive(ms, ZipArchiveMode.Read);
    Assert.That(archive.Entries.Count, Is.GreaterThan(0));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_HasMimetype() {
    var file = _BuildMinimalFile();
    var bytes = OpenRasterWriter.ToBytes(file);

    using var ms = new MemoryStream(bytes);
    using var archive = new ZipArchive(ms, ZipArchiveMode.Read);
    var mimetypeEntry = archive.GetEntry("mimetype");
    Assert.That(mimetypeEntry, Is.Not.Null);

    using var stream = mimetypeEntry!.Open();
    using var reader = new StreamReader(stream, Encoding.UTF8);
    var text = reader.ReadToEnd();
    Assert.That(text, Is.EqualTo("image/openraster"));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_HasStackXml() {
    var file = _BuildMinimalFile();
    var bytes = OpenRasterWriter.ToBytes(file);

    using var ms = new MemoryStream(bytes);
    using var archive = new ZipArchive(ms, ZipArchiveMode.Read);
    var stackEntry = archive.GetEntry("stack.xml");
    Assert.That(stackEntry, Is.Not.Null);

    using var stream = stackEntry!.Open();
    using var reader = new StreamReader(stream, Encoding.UTF8);
    var xml = reader.ReadToEnd();
    Assert.That(xml, Does.Contain("<image"));
    Assert.That(xml, Does.Contain("<stack"));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_HasMergedImagePng() {
    var file = _BuildMinimalFile();
    var bytes = OpenRasterWriter.ToBytes(file);

    using var ms = new MemoryStream(bytes);
    using var archive = new ZipArchive(ms, ZipArchiveMode.Read);
    var mergedEntry = archive.GetEntry("mergedimage.png");
    Assert.That(mergedEntry, Is.Not.Null);
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_HasLayerPng() {
    var file = _BuildMinimalFile();
    var bytes = OpenRasterWriter.ToBytes(file);

    using var ms = new MemoryStream(bytes);
    using var archive = new ZipArchive(ms, ZipArchiveMode.Read);
    var layerEntry = archive.GetEntry("data/layer0.png");
    Assert.That(layerEntry, Is.Not.Null);
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_MimetypeIsFirstEntry() {
    var file = _BuildMinimalFile();
    var bytes = OpenRasterWriter.ToBytes(file);

    using var ms = new MemoryStream(bytes);
    using var archive = new ZipArchive(ms, ZipArchiveMode.Read);
    Assert.That(archive.Entries.First().FullName, Is.EqualTo("mimetype"));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_StackXmlContainsLayerName() {
    var file = new OpenRasterFile {
      Width = 1,
      Height = 1,
      PixelData = new byte[4],
      Layers = [
        new OpenRasterLayer {
          Name = "TestLayer",
          Width = 1,
          Height = 1,
          PixelData = new byte[4]
        }
      ]
    };

    var bytes = OpenRasterWriter.ToBytes(file);

    using var ms = new MemoryStream(bytes);
    using var archive = new ZipArchive(ms, ZipArchiveMode.Read);
    var stackEntry = archive.GetEntry("stack.xml")!;
    using var stream = stackEntry.Open();
    using var reader = new StreamReader(stream, Encoding.UTF8);
    var xml = reader.ReadToEnd();
    Assert.That(xml, Does.Contain("TestLayer"));
  }

  private static OpenRasterFile _BuildMinimalFile() => new() {
    Width = 2,
    Height = 2,
    PixelData = new byte[2 * 2 * 4],
    Layers = [
      new OpenRasterLayer {
        Name = "Layer 0",
        Width = 2,
        Height = 2,
        PixelData = new byte[2 * 2 * 4]
      }
    ]
  };
}
