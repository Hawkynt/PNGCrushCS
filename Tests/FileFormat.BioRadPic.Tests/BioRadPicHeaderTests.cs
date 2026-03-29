using System;
using System.Linq;
using FileFormat.BioRadPic;
using FileFormat.Core;

namespace FileFormat.BioRadPic.Tests;

[TestFixture]
public sealed class BioRadPicHeaderTests {

  [Test]
  [Category("Unit")]
  public void StructSize_Is76() {
    Assert.That(BioRadPicHeader.StructSize, Is.EqualTo(76));
  }

  [Test]
  [Category("Unit")]
  public void RoundTrip_PreservesAllFields() {
    var original = new BioRadPicHeader(
      Nx: 512,
      Ny: 256,
      Npic: 3,
      Ramp1Min: -100,
      Ramp1Max: 4000,
      Notes: 1,
      ByteFormat: 1,
      ImageNumber: 0,
      Name: "sample.pic",
      Merged: 2,
      Color1: 7,
      FileId: BioRadPicHeader.MagicFileId,
      Ramp2Min: -50,
      Ramp2Max: 3000,
      Color2: 14,
      Edited: 1,
      Lens: 40,
      MagFactor: 1.5f
    );

    var buffer = new byte[BioRadPicHeader.StructSize];
    original.WriteTo(buffer.AsSpan());
    var parsed = BioRadPicHeader.ReadFrom(buffer.AsSpan());

    Assert.That(parsed.Nx, Is.EqualTo(original.Nx));
    Assert.That(parsed.Ny, Is.EqualTo(original.Ny));
    Assert.That(parsed.Npic, Is.EqualTo(original.Npic));
    Assert.That(parsed.Ramp1Min, Is.EqualTo(original.Ramp1Min));
    Assert.That(parsed.Ramp1Max, Is.EqualTo(original.Ramp1Max));
    Assert.That(parsed.Notes, Is.EqualTo(original.Notes));
    Assert.That(parsed.ByteFormat, Is.EqualTo(original.ByteFormat));
    Assert.That(parsed.Name, Is.EqualTo(original.Name));
    Assert.That(parsed.Merged, Is.EqualTo(original.Merged));
    Assert.That(parsed.Color1, Is.EqualTo(original.Color1));
    Assert.That(parsed.FileId, Is.EqualTo(BioRadPicHeader.MagicFileId));
    Assert.That(parsed.Ramp2Min, Is.EqualTo(original.Ramp2Min));
    Assert.That(parsed.Ramp2Max, Is.EqualTo(original.Ramp2Max));
    Assert.That(parsed.Color2, Is.EqualTo(original.Color2));
    Assert.That(parsed.Edited, Is.EqualTo(original.Edited));
    Assert.That(parsed.Lens, Is.EqualTo(original.Lens));
    Assert.That(parsed.MagFactor, Is.EqualTo(original.MagFactor));
  }

  [Test]
  [Category("Unit")]
  public void GetFieldMap_CoversStructSize() {
    var map = BioRadPicHeader.GetFieldMap();
    var totalSize = map.Sum(f => f.Size);
    Assert.That(totalSize, Is.GreaterThanOrEqualTo(BioRadPicHeader.StructSize - 6)); // minus reserved
  }

  [Test]
  [Category("Unit")]
  public void GetFieldMap_HasEntries() {
    var map = BioRadPicHeader.GetFieldMap();
    Assert.That(map.Length, Is.GreaterThan(0));
  }

  [Test]
  [Category("Unit")]
  public void MagicFileId_Is12345() {
    Assert.That(BioRadPicHeader.MagicFileId, Is.EqualTo(12345));
  }
}
