using System;
using System.Buffers.Binary;
using System.IO;
using System.Text;
using FileFormat.Icns;
using FileFormat.Png;

namespace FileFormat.Icns.Tests;

[TestFixture]
public sealed class IcnsReaderTests {

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => IcnsReader.FromBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => IcnsReader.FromFile(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".icns"));
    Assert.Throws<FileNotFoundException>(() => IcnsReader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => IcnsReader.FromStream(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException() {
    var tooSmall = new byte[4];
    Assert.Throws<InvalidDataException>(() => IcnsReader.FromBytes(tooSmall));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_InvalidMagic_ThrowsInvalidDataException() {
    var bad = new byte[8];
    bad[0] = (byte)'X';
    bad[1] = (byte)'Y';
    bad[2] = (byte)'Z';
    bad[3] = (byte)'W';
    BinaryPrimitives.WriteUInt32BigEndian(bad.AsSpan(4), 8);
    Assert.Throws<InvalidDataException>(() => IcnsReader.FromBytes(bad));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidSinglePngEntry_ParsesCorrectly() {
    var data = _BuildIcnsWithPngEntry("ic07", 128, 128);
    var result = IcnsReader.FromBytes(data);

    Assert.That(result.Entries, Has.Count.EqualTo(1));
    Assert.That(result.Entries[0].OsType, Is.EqualTo("ic07"));
    Assert.That(result.Entries[0].Width, Is.EqualTo(128));
    Assert.That(result.Entries[0].Height, Is.EqualTo(128));
    Assert.That(result.Entries[0].IsPng, Is.True);
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_MultipleEntries_ParsesAll() {
    var entry1 = _CreatePngEntryData("ic07", 128, 128);
    var entry2 = _CreatePngEntryData("ic08", 256, 256);

    var totalSize = 8 + entry1.Length + entry2.Length;
    var data = new byte[totalSize];
    data[0] = (byte)'i';
    data[1] = (byte)'c';
    data[2] = (byte)'n';
    data[3] = (byte)'s';
    BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(4), (uint)totalSize);
    Array.Copy(entry1, 0, data, 8, entry1.Length);
    Array.Copy(entry2, 0, data, 8 + entry1.Length, entry2.Length);

    var result = IcnsReader.FromBytes(data);

    Assert.That(result.Entries, Has.Count.EqualTo(2));
    Assert.That(result.Entries[0].OsType, Is.EqualTo("ic07"));
    Assert.That(result.Entries[1].OsType, Is.EqualTo("ic08"));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_LegacyRgbEntry_ParsesDimensions() {
    var entryData = new byte[16 * 16]; // dummy data
    var data = _BuildIcnsWithRawEntry("is32", entryData);
    var result = IcnsReader.FromBytes(data);

    Assert.That(result.Entries, Has.Count.EqualTo(1));
    Assert.That(result.Entries[0].OsType, Is.EqualTo("is32"));
    Assert.That(result.Entries[0].Width, Is.EqualTo(16));
    Assert.That(result.Entries[0].Height, Is.EqualTo(16));
    Assert.That(result.Entries[0].IsLegacyRgb, Is.True);
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_MaskEntry_ParsesDimensions() {
    var entryData = new byte[16 * 16];
    var data = _BuildIcnsWithRawEntry("s8mk", entryData);
    var result = IcnsReader.FromBytes(data);

    Assert.That(result.Entries[0].OsType, Is.EqualTo("s8mk"));
    Assert.That(result.Entries[0].Width, Is.EqualTo(16));
    Assert.That(result.Entries[0].Height, Is.EqualTo(16));
    Assert.That(result.Entries[0].IsLegacyMask, Is.True);
  }

  [Test]
  [Category("Unit")]
  public void FromStream_ValidData_ParsesCorrectly() {
    var data = _BuildIcnsWithPngEntry("ic07", 128, 128);
    using var ms = new MemoryStream(data);
    var result = IcnsReader.FromStream(ms);

    Assert.That(result.Entries, Has.Count.EqualTo(1));
    Assert.That(result.Entries[0].OsType, Is.EqualTo("ic07"));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_InvalidFileLengthTooSmall_ThrowsInvalidDataException() {
    var data = new byte[8];
    data[0] = (byte)'i';
    data[1] = (byte)'c';
    data[2] = (byte)'n';
    data[3] = (byte)'s';
    BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(4), 4); // length < 8
    Assert.Throws<InvalidDataException>(() => IcnsReader.FromBytes(data));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_EntryWithInvalidLength_ThrowsInvalidDataException() {
    // Build an ICNS with one entry whose length < 8
    var totalSize = 8 + 8; // header + one entry header
    var data = new byte[totalSize];
    data[0] = (byte)'i';
    data[1] = (byte)'c';
    data[2] = (byte)'n';
    data[3] = (byte)'s';
    BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(4), (uint)totalSize);
    Encoding.ASCII.GetBytes("ic07").CopyTo(data, 8);
    BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(12), 4); // entry length < 8

    Assert.Throws<InvalidDataException>(() => IcnsReader.FromBytes(data));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_EmptyFileNoEntries_ReturnsEmptyEntries() {
    var data = new byte[8];
    data[0] = (byte)'i';
    data[1] = (byte)'c';
    data[2] = (byte)'n';
    data[3] = (byte)'s';
    BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(4), 8);

    var result = IcnsReader.FromBytes(data);
    Assert.That(result.Entries, Has.Count.EqualTo(0));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_UnknownOsType_ParsesWithZeroDimensions() {
    var entryData = new byte[10];
    var data = _BuildIcnsWithRawEntry("ZZZZ", entryData);
    var result = IcnsReader.FromBytes(data);

    Assert.That(result.Entries[0].OsType, Is.EqualTo("ZZZZ"));
    Assert.That(result.Entries[0].Width, Is.EqualTo(0));
    Assert.That(result.Entries[0].Height, Is.EqualTo(0));
  }

  private static byte[] _BuildIcnsWithPngEntry(string osType, int width, int height) {
    var pngBytes = _CreateMinimalPng(width, height);
    return _BuildIcnsWithRawEntry(osType, pngBytes);
  }

  private static byte[] _BuildIcnsWithRawEntry(string osType, byte[] entryData) {
    var entryLength = 8 + entryData.Length;
    var totalSize = 8 + entryLength;
    var data = new byte[totalSize];

    data[0] = (byte)'i';
    data[1] = (byte)'c';
    data[2] = (byte)'n';
    data[3] = (byte)'s';
    BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(4), (uint)totalSize);

    Encoding.ASCII.GetBytes(osType).CopyTo(data, 8);
    BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(12), (uint)entryLength);
    Array.Copy(entryData, 0, data, 16, entryData.Length);

    return data;
  }

  private static byte[] _CreatePngEntryData(string osType, int width, int height) {
    var pngBytes = _CreateMinimalPng(width, height);
    var entryLength = 8 + pngBytes.Length;
    var entry = new byte[entryLength];
    Encoding.ASCII.GetBytes(osType).CopyTo(entry, 0);
    BinaryPrimitives.WriteUInt32BigEndian(entry.AsSpan(4), (uint)entryLength);
    Array.Copy(pngBytes, 0, entry, 8, pngBytes.Length);
    return entry;
  }

  private static byte[] _CreateMinimalPng(int width, int height) {
    var pixelData = new byte[width * height * 4];
    for (var i = 0; i < pixelData.Length; i += 4) {
      pixelData[i] = (byte)(i % 256);
      pixelData[i + 1] = (byte)((i + 64) % 256);
      pixelData[i + 2] = (byte)((i + 128) % 256);
      pixelData[i + 3] = 255;
    }

    var raw = new FileFormat.Core.RawImage {
      Width = width,
      Height = height,
      Format = FileFormat.Core.PixelFormat.Rgba32,
      PixelData = pixelData,
    };

    var pngFile = PngFile.FromRawImage(raw);
    return PngWriter.ToBytes(pngFile);
  }
}
