using System;
using System.Buffers.Binary;
using FileFormat.Vtf;

namespace FileFormat.Vtf.Tests;

[TestFixture]
public sealed class VtfWriterTests {

  [Test]
  [Category("Unit")]
  public void ToBytes_ValidFile_StartsWithVtfSignature() {
    var file = new VtfFile {
      Width = 4,
      Height = 4,
      MipmapCount = 1,
      Format = VtfFormat.Rgba8888,
      Flags = VtfFlags.None,
      Frames = 1,
      VersionMajor = 7,
      VersionMinor = 2,
      Surfaces = [
        new VtfSurface {
          Width = 4,
          Height = 4,
          MipLevel = 0,
          Frame = 0,
          Data = new byte[4 * 4 * 4]
        }
      ]
    };

    var bytes = VtfWriter.ToBytes(file);

    Assert.That(bytes[0], Is.EqualTo((byte)'V'));
    Assert.That(bytes[1], Is.EqualTo((byte)'T'));
    Assert.That(bytes[2], Is.EqualTo((byte)'F'));
    Assert.That(bytes[3], Is.EqualTo(0));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_ValidFile_WritesCorrectVersion() {
    var file = new VtfFile {
      Width = 4,
      Height = 4,
      MipmapCount = 1,
      Format = VtfFormat.Rgb888,
      Flags = VtfFlags.None,
      Frames = 1,
      VersionMajor = 7,
      VersionMinor = 2,
      Surfaces = [
        new VtfSurface {
          Width = 4,
          Height = 4,
          MipLevel = 0,
          Frame = 0,
          Data = new byte[4 * 4 * 3]
        }
      ]
    };

    var bytes = VtfWriter.ToBytes(file);

    var major = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(4));
    var minor = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(8));
    Assert.That(major, Is.EqualTo(7));
    Assert.That(minor, Is.EqualTo(2));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_ValidFile_WritesCorrectDimensions() {
    var file = new VtfFile {
      Width = 16,
      Height = 32,
      MipmapCount = 1,
      Format = VtfFormat.Rgba8888,
      Flags = VtfFlags.None,
      Frames = 1,
      VersionMajor = 7,
      VersionMinor = 2,
      Surfaces = [
        new VtfSurface {
          Width = 16,
          Height = 32,
          MipLevel = 0,
          Frame = 0,
          Data = new byte[16 * 32 * 4]
        }
      ]
    };

    var bytes = VtfWriter.ToBytes(file);

    var w = BinaryPrimitives.ReadInt16LittleEndian(bytes.AsSpan(16));
    var h = BinaryPrimitives.ReadInt16LittleEndian(bytes.AsSpan(18));
    Assert.That(w, Is.EqualTo(16));
    Assert.That(h, Is.EqualTo(32));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => VtfWriter.ToBytes(null!));
  }
}
