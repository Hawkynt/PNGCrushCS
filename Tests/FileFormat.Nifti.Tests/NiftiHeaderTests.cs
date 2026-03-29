using System;
using System.Linq;
using FileFormat.Nifti;
using FileFormat.Core;

namespace FileFormat.Nifti.Tests;

[TestFixture]
public sealed class NiftiHeaderTests {

  [Test]
  [Category("Unit")]
  public void StructSize_Is348() {
    Assert.That(NiftiHeader.StructSize, Is.EqualTo(348));
  }

  [Test]
  [Category("Unit")]
  public void ReadFrom_WriteTo_RoundTrip() {
    var original = new NiftiHeader {
      SizeOfHdr = 348,
      Dim = [3, 64, 64, 20, 0, 0, 0, 0],
      IntentP1 = 1.0f,
      IntentP2 = 2.0f,
      IntentP3 = 3.0f,
      IntentCode = 5,
      Datatype = (short)NiftiDataType.Float32,
      Bitpix = 32,
      SliceStart = 0,
      Pixdim = [1.0f, 2.0f, 2.0f, 3.0f, 0, 0, 0, 0],
      VoxOffset = 352f,
      SclSlope = 1.5f,
      SclInter = -0.5f,
      SliceEnd = 19,
      SliceCode = 1,
      XyztUnits = 10,
      CalMax = 100f,
      CalMin = -50f,
      SliceDuration = 0.5f,
      TOffset = 1.0f,
      Descrip = "Test header",
      AuxFile = "aux.txt",
      QformCode = 1,
      SformCode = 2,
      QuaternB = 0.1f,
      QuaternC = 0.2f,
      QuaternD = 0.3f,
      QoffsetX = -100f,
      QoffsetY = -120f,
      QoffsetZ = -80f,
      SrowX = [2.0f, 0, 0, -100f],
      SrowY = [0, 2.0f, 0, -120f],
      SrowZ = [0, 0, 3.0f, -80f],
      IntentName = "correlation",
      Magic = "n+1\0"
    };

    var buffer = new byte[NiftiHeader.StructSize];
    original.WriteTo(buffer.AsSpan());
    var parsed = NiftiHeader.ReadFrom(buffer.AsSpan());

    Assert.That(parsed.SizeOfHdr, Is.EqualTo(original.SizeOfHdr));
    Assert.That(parsed.Dim, Is.EqualTo(original.Dim));
    Assert.That(parsed.Datatype, Is.EqualTo(original.Datatype));
    Assert.That(parsed.Bitpix, Is.EqualTo(original.Bitpix));
    Assert.That(parsed.VoxOffset, Is.EqualTo(original.VoxOffset));
    Assert.That(parsed.SclSlope, Is.EqualTo(original.SclSlope));
    Assert.That(parsed.SclInter, Is.EqualTo(original.SclInter));
    Assert.That(parsed.Descrip, Is.EqualTo(original.Descrip));
    Assert.That(parsed.IntentP1, Is.EqualTo(original.IntentP1));
    Assert.That(parsed.IntentP2, Is.EqualTo(original.IntentP2));
    Assert.That(parsed.IntentP3, Is.EqualTo(original.IntentP3));
    Assert.That(parsed.IntentCode, Is.EqualTo(original.IntentCode));
    Assert.That(parsed.SliceStart, Is.EqualTo(original.SliceStart));
    Assert.That(parsed.Pixdim, Is.EqualTo(original.Pixdim));
    Assert.That(parsed.SliceEnd, Is.EqualTo(original.SliceEnd));
    Assert.That(parsed.SliceCode, Is.EqualTo(original.SliceCode));
    Assert.That(parsed.XyztUnits, Is.EqualTo(original.XyztUnits));
    Assert.That(parsed.CalMax, Is.EqualTo(original.CalMax));
    Assert.That(parsed.CalMin, Is.EqualTo(original.CalMin));
    Assert.That(parsed.SliceDuration, Is.EqualTo(original.SliceDuration));
    Assert.That(parsed.TOffset, Is.EqualTo(original.TOffset));
    Assert.That(parsed.AuxFile, Is.EqualTo(original.AuxFile));
    Assert.That(parsed.QformCode, Is.EqualTo(original.QformCode));
    Assert.That(parsed.SformCode, Is.EqualTo(original.SformCode));
    Assert.That(parsed.QuaternB, Is.EqualTo(original.QuaternB));
    Assert.That(parsed.QuaternC, Is.EqualTo(original.QuaternC));
    Assert.That(parsed.QuaternD, Is.EqualTo(original.QuaternD));
    Assert.That(parsed.QoffsetX, Is.EqualTo(original.QoffsetX));
    Assert.That(parsed.QoffsetY, Is.EqualTo(original.QoffsetY));
    Assert.That(parsed.QoffsetZ, Is.EqualTo(original.QoffsetZ));
    Assert.That(parsed.SrowX, Is.EqualTo(original.SrowX));
    Assert.That(parsed.SrowY, Is.EqualTo(original.SrowY));
    Assert.That(parsed.SrowZ, Is.EqualTo(original.SrowZ));
    Assert.That(parsed.IntentName, Is.EqualTo(original.IntentName));
    Assert.That(parsed.Magic, Is.EqualTo("n+1"));
  }

  [Test]
  [Category("Unit")]
  public void GetFieldMap_HasEntries() {
    var map = NiftiHeader.GetFieldMap();
    Assert.That(map.Length, Is.GreaterThan(0));
  }

  [Test]
  [Category("Unit")]
  public void GetFieldMap_CoversStructSize() {
    var map = NiftiHeader.GetFieldMap();
    var totalSize = map.Sum(f => f.Size);
    Assert.That(totalSize, Is.EqualTo(NiftiHeader.StructSize));
  }
}
