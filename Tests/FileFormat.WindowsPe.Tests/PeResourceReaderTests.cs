using System;
using System.IO;
using FileFormat.WindowsPe;

namespace FileFormat.WindowsPe.Tests;

[TestFixture]
public sealed class PeResourceReaderTests {

  [Test]
  [Category("Unit")]
  public void FromFile_NullFile_Throws()
    => Assert.Throws<ArgumentNullException>(() => PeResourceReader.FromFile(null!));

  [Test]
  [Category("Unit")]
  public void FromFile_MissingFile_Throws()
    => Assert.Throws<FileNotFoundException>(() => PeResourceReader.FromFile(new FileInfo("nonexistent.exe")));

  [Test]
  [Category("Unit")]
  public void FromBytes_NullData_Throws()
    => Assert.Throws<ArgumentNullException>(() => PeResourceReader.FromBytes(null!));

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_Throws()
    => Assert.Throws<InvalidDataException>(() => PeResourceReader.FromBytes(new byte[32]));

  [Test]
  [Category("Unit")]
  public void FromBytes_InvalidMzSignature_Throws()
    => Assert.Throws<InvalidDataException>(() => PeResourceReader.FromBytes(new byte[128]));

  [Test]
  [Category("Unit")]
  public void FromBytes_MzOnly_InvalidPeSignature_Throws() {
    var data = new byte[256];
    data[0] = 0x4D;
    data[1] = 0x5A;
    data[60] = 0x80;
    Assert.Throws<InvalidDataException>(() => PeResourceReader.FromBytes(data));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_NullStream_Throws()
    => Assert.Throws<ArgumentNullException>(() => PeResourceReader.FromStream(null!));

  [Test]
  [Category("Integration")]
  public void FromBytes_EmptyPe_ReturnsNoResources() {
    var pe = MinimalPeBuilder.BuildEmpty();
    var file = PeResourceReader.FromBytes(pe);
    Assert.Multiple(() => {
      Assert.That(file.ImageResources, Is.Empty);
      Assert.That(file.IconGroups, Is.Empty);
    });
  }

  [Test]
  [Category("Integration")]
  public void FromBytes_WithBitmap_ReturnsBitmapResource() {
    var dib = MinimalPeBuilder.CreateMinimalDib(4, 4);
    var pe = MinimalPeBuilder.BuildWithBitmap(dib);
    var file = PeResourceReader.FromBytes(pe);
    Assert.That(file.ImageResources, Has.Count.EqualTo(1));
    Assert.That(file.ImageResources[0].ResourceType, Is.EqualTo(PeImageResourceType.Bitmap));
  }

  [Test]
  [Category("Integration")]
  public void FromBytes_WithBitmap_PrependsBitmapFileHeader() {
    var dib = MinimalPeBuilder.CreateMinimalDib(4, 4);
    var pe = MinimalPeBuilder.BuildWithBitmap(dib);
    var file = PeResourceReader.FromBytes(pe);
    var bmpData = file.ImageResources[0].Data;
    Assert.Multiple(() => {
      Assert.That(bmpData[0], Is.EqualTo(0x42));
      Assert.That(bmpData[1], Is.EqualTo(0x4D));
      Assert.That(bmpData.Length, Is.EqualTo(14 + dib.Length));
    });
  }

  [Test]
  [Category("Integration")]
  public void FromBytes_WithBitmap_DibDataPreserved() {
    var dib = MinimalPeBuilder.CreateMinimalDib(4, 4);
    var pe = MinimalPeBuilder.BuildWithBitmap(dib);
    var file = PeResourceReader.FromBytes(pe);
    var bmpData = file.ImageResources[0].Data;
    var extractedDib = bmpData[14..];
    Assert.That(extractedDib, Is.EqualTo(dib));
  }

  [Test]
  [Category("Integration")]
  public void FromBytes_WithIconGroup_ReturnsIconResource() {
    var iconEntry = MinimalPeBuilder.CreateMinimalIconEntry();
    var pe = MinimalPeBuilder.BuildWithIconGroup(new[] { iconEntry });
    var file = PeResourceReader.FromBytes(pe);
    Assert.That(file.ImageResources.Count, Is.GreaterThanOrEqualTo(1));
    Assert.That(file.ImageResources[0].ResourceType, Is.EqualTo(PeImageResourceType.Icon));
  }

  [Test]
  [Category("Integration")]
  public void FromBytes_WithIconGroup_AssemblesValidIcoHeader() {
    var iconEntry = MinimalPeBuilder.CreateMinimalIconEntry();
    var pe = MinimalPeBuilder.BuildWithIconGroup(new[] { iconEntry });
    var file = PeResourceReader.FromBytes(pe);
    var icoData = file.IconGroups[0].IcoData;
    Assert.Multiple(() => {
      Assert.That(icoData[0], Is.EqualTo(0));
      Assert.That(icoData[1], Is.EqualTo(0));
      Assert.That(icoData[2], Is.EqualTo(1));
      Assert.That(icoData[3], Is.EqualTo(0));
      Assert.That(icoData[4], Is.EqualTo(1));
      Assert.That(icoData[5], Is.EqualTo(0));
    });
  }

  [Test]
  [Category("Integration")]
  public void FromBytes_WithMultipleIcons_AssemblesMultipleEntries() {
    var icon1 = MinimalPeBuilder.CreateMinimalIconEntry();
    var icon2 = MinimalPeBuilder.CreateMinimalIconEntry();
    var pe = MinimalPeBuilder.BuildWithIconGroup(new[] { icon1, icon2 });
    var file = PeResourceReader.FromBytes(pe);
    var icoData = file.IconGroups[0].IcoData;
    var count = icoData[4] | (icoData[5] << 8);
    Assert.That(count, Is.EqualTo(2));
  }

  [Test]
  [Category("Integration")]
  public void FromBytes_WithEmbeddedBmp_DetectsFormatHint() {
    var bmpData = new byte[] { 0x42, 0x4D, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 };
    var pe = MinimalPeBuilder.BuildWithEmbeddedImage(bmpData);
    var file = PeResourceReader.FromBytes(pe);
    Assert.That(file.ImageResources, Has.Count.EqualTo(1));
    Assert.Multiple(() => {
      Assert.That(file.ImageResources[0].ResourceType, Is.EqualTo(PeImageResourceType.EmbeddedImage));
      Assert.That(file.ImageResources[0].FormatHint, Is.EqualTo("bmp"));
    });
  }

  [Test]
  [Category("Integration")]
  public void FromStream_SeekableStream_ParsesCorrectly() {
    var pe = MinimalPeBuilder.BuildEmpty();
    using var ms = new MemoryStream(pe);
    var file = PeResourceReader.FromStream(ms);
    Assert.That(file.ImageResources, Is.Empty);
  }

  [Test]
  [Category("Integration")]
  public void FromFile_ValidPeFile_ParsesCorrectly() {
    var dib = MinimalPeBuilder.CreateMinimalDib(2, 2);
    var pe = MinimalPeBuilder.BuildWithBitmap(dib);
    var tempPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".exe");
    try {
      File.WriteAllBytes(tempPath, pe);
      var file = PeResourceReader.FromFile(new FileInfo(tempPath));
      Assert.That(file.ImageResources, Has.Count.EqualTo(1));
    } finally {
      if (File.Exists(tempPath))
        File.Delete(tempPath);
    }
  }
}
