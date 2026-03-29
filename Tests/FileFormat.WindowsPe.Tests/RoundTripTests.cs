using System;
using System.IO;
using FileFormat.WindowsPe;

namespace FileFormat.WindowsPe.Tests;

[TestFixture]
public sealed class RoundTripTests {

  [Test]
  [Category("Integration")]
  public void RoundTrip_Bitmap_ViaBytes() {
    var dib = MinimalPeBuilder.CreateMinimalDib(4, 4);
    var pe = MinimalPeBuilder.BuildWithBitmap(dib);
    var file = PeResourceReader.FromBytes(pe);
    Assert.That(file.ImageResources, Has.Count.EqualTo(1));
    var bmpData = file.ImageResources[0].Data;
    Assert.That(bmpData[0], Is.EqualTo(0x42));
    Assert.That(bmpData[1], Is.EqualTo(0x4D));
    Assert.That(bmpData[14..], Is.EqualTo(dib));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_Bitmap_ViaFile() {
    var dib = MinimalPeBuilder.CreateMinimalDib(8, 8);
    var pe = MinimalPeBuilder.BuildWithBitmap(dib);
    var tempPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".exe");
    try {
      File.WriteAllBytes(tempPath, pe);
      var file = PeResourceReader.FromFile(new FileInfo(tempPath));
      Assert.That(file.ImageResources, Has.Count.EqualTo(1));
      Assert.That(file.ImageResources[0].ResourceType, Is.EqualTo(PeImageResourceType.Bitmap));
    } finally {
      if (File.Exists(tempPath))
        File.Delete(tempPath);
    }
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_Bitmap_ViaStream() {
    var dib = MinimalPeBuilder.CreateMinimalDib(2, 2);
    var pe = MinimalPeBuilder.BuildWithBitmap(dib);
    using var ms = new MemoryStream(pe);
    var file = PeResourceReader.FromStream(ms);
    Assert.That(file.ImageResources, Has.Count.EqualTo(1));
    Assert.That(file.ImageResources[0].ResourceType, Is.EqualTo(PeImageResourceType.Bitmap));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_IconGroup_ViaBytes() {
    var iconEntry = MinimalPeBuilder.CreateMinimalIconEntry();
    var pe = MinimalPeBuilder.BuildWithIconGroup(new[] { iconEntry });
    var file = PeResourceReader.FromBytes(pe);
    Assert.Multiple(() => {
      Assert.That(file.IconGroups, Has.Count.EqualTo(1));
      Assert.That(file.IconGroups[0].IsCursor, Is.False);
      Assert.That(file.IconGroups[0].GroupId, Is.EqualTo(1));
    });
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_MultipleIcons_ViaBytes() {
    var icon1 = MinimalPeBuilder.CreateMinimalIconEntry();
    var icon2 = MinimalPeBuilder.CreateMinimalIconEntry();
    var pe = MinimalPeBuilder.BuildWithIconGroup(new[] { icon1, icon2 });
    var file = PeResourceReader.FromBytes(pe);
    Assert.That(file.IconGroups, Has.Count.EqualTo(1));
    var icoData = file.IconGroups[0].IcoData;
    var count = icoData[4] | (icoData[5] << 8);
    Assert.That(count, Is.EqualTo(2));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_EmbeddedBmp_ViaBytes() {
    var bmpData = new byte[60];
    bmpData[0] = 0x42;
    bmpData[1] = 0x4D;
    for (var i = 2; i < bmpData.Length; ++i)
      bmpData[i] = (byte)(i * 3 % 256);
    var pe = MinimalPeBuilder.BuildWithEmbeddedImage(bmpData);
    var file = PeResourceReader.FromBytes(pe);
    Assert.That(file.ImageResources, Has.Count.EqualTo(1));
    Assert.That(file.ImageResources[0].Data, Is.EqualTo(bmpData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_Empty_ViaBytes() {
    var pe = MinimalPeBuilder.BuildEmpty();
    var file = PeResourceReader.FromBytes(pe);
    Assert.Multiple(() => {
      Assert.That(file.ImageResources, Is.Empty);
      Assert.That(file.IconGroups, Is.Empty);
    });
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_Bitmap_ResourceIdPreserved() {
    var dib = MinimalPeBuilder.CreateMinimalDib(2, 2);
    var pe = MinimalPeBuilder.BuildWithBitmap(dib, resourceId: 7);
    var file = PeResourceReader.FromBytes(pe);
    Assert.That(file.ImageResources[0].ResourceId, Is.EqualTo(7));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_IconGroup_GroupIdPreserved() {
    var iconEntry = MinimalPeBuilder.CreateMinimalIconEntry();
    var pe = MinimalPeBuilder.BuildWithIconGroup(new[] { iconEntry }, groupId: 42);
    var file = PeResourceReader.FromBytes(pe);
    Assert.That(file.IconGroups[0].GroupId, Is.EqualTo(42));
  }
}
