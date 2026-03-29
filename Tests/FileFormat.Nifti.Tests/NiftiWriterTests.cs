using System;
using System.Buffers.Binary;
using System.Text;
using FileFormat.Nifti;

namespace FileFormat.Nifti.Tests;

[TestFixture]
public sealed class NiftiWriterTests {

  [Test]
  [Category("Unit")]
  public void ToBytes_SizeOfHdr_Is348() {
    var file = _MakeMinimalFile();
    var bytes = NiftiWriter.ToBytes(file);
    var sizeOfHdr = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(0));
    Assert.That(sizeOfHdr, Is.EqualTo(348));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_Magic_IsNPlus1() {
    var file = _MakeMinimalFile();
    var bytes = NiftiWriter.ToBytes(file);
    var magic = Encoding.ASCII.GetString(bytes, 344, 4);
    Assert.That(magic, Is.EqualTo("n+1\0"));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_Null_Throws() {
    Assert.Throws<ArgumentNullException>(() => NiftiWriter.ToBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_HasPixelData() {
    var pixelData = new byte[] { 0xAA, 0xBB, 0xCC, 0xDD };
    var file = new NiftiFile {
      Width = 2,
      Height = 2,
      Depth = 1,
      Datatype = NiftiDataType.UInt8,
      Bitpix = 8,
      VoxOffset = 352f,
      PixelData = pixelData
    };

    var bytes = NiftiWriter.ToBytes(file);

    Assert.That(bytes.Length, Is.EqualTo(352 + 4));
    Assert.That(bytes[352], Is.EqualTo(0xAA));
    Assert.That(bytes[353], Is.EqualTo(0xBB));
    Assert.That(bytes[354], Is.EqualTo(0xCC));
    Assert.That(bytes[355], Is.EqualTo(0xDD));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_WritesDimensions() {
    var file = new NiftiFile {
      Width = 64,
      Height = 32,
      Depth = 1,
      Datatype = NiftiDataType.Int16,
      Bitpix = 16,
      VoxOffset = 352f,
      PixelData = new byte[64 * 32 * 2]
    };

    var bytes = NiftiWriter.ToBytes(file);

    var ndims = BinaryPrimitives.ReadInt16LittleEndian(bytes.AsSpan(40));
    var width = BinaryPrimitives.ReadInt16LittleEndian(bytes.AsSpan(42));
    var height = BinaryPrimitives.ReadInt16LittleEndian(bytes.AsSpan(44));
    Assert.That(ndims, Is.EqualTo(2));
    Assert.That(width, Is.EqualTo(64));
    Assert.That(height, Is.EqualTo(32));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_DefaultVoxOffset_Is352() {
    var file = new NiftiFile {
      Width = 1,
      Height = 1,
      Depth = 1,
      Datatype = NiftiDataType.UInt8,
      Bitpix = 8,
      VoxOffset = 0f,
      PixelData = [42]
    };

    var bytes = NiftiWriter.ToBytes(file);

    var voxOffset = BinaryPrimitives.ReadSingleLittleEndian(bytes.AsSpan(108));
    Assert.That(voxOffset, Is.EqualTo(352f));
    Assert.That(bytes.Length, Is.EqualTo(353));
  }

  private static NiftiFile _MakeMinimalFile() => new() {
    Width = 1,
    Height = 1,
    Depth = 1,
    Datatype = NiftiDataType.UInt8,
    Bitpix = 8,
    VoxOffset = 352f,
    PixelData = [0]
  };
}
