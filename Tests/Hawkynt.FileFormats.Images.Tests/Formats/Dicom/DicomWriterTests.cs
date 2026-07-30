using System;
using System.Buffers.Binary;
using System.Text;
using FileFormat.Dicom;

namespace FileFormat.Dicom.Tests;

[TestFixture]
public sealed class DicomWriterTests {

  [Test]
  [Category("Unit")]
  public void ToBytes_Has128BytePreamble() {
    var file = new DicomFile {
      Width = 2,
      Height = 2,
      BitsAllocated = 8,
      BitsStored = 8,
      SamplesPerPixel = 1,
      PhotometricInterpretation = DicomPhotometricInterpretation.Monochrome2,
      PixelData = new byte[4]
    };

    var bytes = DicomWriter.ToBytes(file);

    for (var i = 0; i < 128; ++i)
      Assert.That(bytes[i], Is.EqualTo(0), $"Preamble byte at offset {i} should be 0");
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_HasDicmMagic() {
    var file = new DicomFile {
      Width = 2,
      Height = 2,
      BitsAllocated = 8,
      BitsStored = 8,
      SamplesPerPixel = 1,
      PhotometricInterpretation = DicomPhotometricInterpretation.Monochrome2,
      PixelData = new byte[4]
    };

    var bytes = DicomWriter.ToBytes(file);

    var magic = Encoding.ASCII.GetString(bytes, 128, 4);
    Assert.That(magic, Is.EqualTo("DICM"));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_HasRowsTag() {
    var file = new DicomFile {
      Width = 16,
      Height = 32,
      BitsAllocated = 8,
      BitsStored = 8,
      SamplesPerPixel = 1,
      PhotometricInterpretation = DicomPhotometricInterpretation.Monochrome2,
      PixelData = new byte[16 * 32]
    };

    var bytes = DicomWriter.ToBytes(file);

    // Search for Rows tag (0028,0010)
    var found = _FindTag(bytes, 0x0028, 0x0010);
    Assert.That(found, Is.True, "Rows tag (0028,0010) should be present");
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_HasPixelDataTag() {
    var file = new DicomFile {
      Width = 2,
      Height = 2,
      BitsAllocated = 8,
      BitsStored = 8,
      SamplesPerPixel = 1,
      PhotometricInterpretation = DicomPhotometricInterpretation.Monochrome2,
      PixelData = new byte[4]
    };

    var bytes = DicomWriter.ToBytes(file);

    // Search for PixelData tag (7FE0,0010)
    var found = _FindTag(bytes, 0x7FE0, 0x0010);
    Assert.That(found, Is.True, "PixelData tag (7FE0,0010) should be present");
  }

  private static bool _FindTag(byte[] data, ushort group, ushort element) {
    for (var i = 132; i + 3 < data.Length; ++i) {
      var g = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(i));
      var e = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(i + 2));
      if (g == group && e == element)
        return true;
    }

    return false;
  }
}
