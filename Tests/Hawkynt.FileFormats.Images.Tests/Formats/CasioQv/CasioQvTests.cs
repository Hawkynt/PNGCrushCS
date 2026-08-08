using System;
using System.IO;
using FileFormat.CasioQv;

namespace FileFormat.CasioQv.Tests;

[TestFixture]
public sealed class CasioQvTests {

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => CasioQvReader.FromBytes(null!));

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => CasioQvReader.FromFile(null!));

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException()
    => Assert.Throws<FileNotFoundException>(
      () => CasioQvReader.FromFile(new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".cam"))));

  [Test]
  [Category("Unit")]
  public void FromStream_Null_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => CasioQvReader.FromStream(null!));

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException()
    => Assert.Throws<InvalidDataException>(() => CasioQvReader.FromBytes(new byte[4]));

  [Test]
  [Category("Unit")]
  public void FromBytes_ForeignFile_ThrowsInvalidDataException() {
    var jpeg = new byte[64];
    jpeg[0] = 0xFF;
    jpeg[1] = 0xD8;
    jpeg[2] = 0xFF;
    Assert.Throws<InvalidDataException>(() => CasioQvReader.FromBytes(jpeg));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_AreasDoNotAccountForTheFile_ThrowsInvalidDataException() {
    var data = _Build(CasioQvFile.AreaWholeJpeg, [0xFF, 0xD8, 0xFF, 0xD9], slack: 16);
    Assert.Throws<InvalidDataException>(() => CasioQvReader.FromBytes(data));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_AreaLongerThanTheFile_ThrowsInvalidDataException() {
    var data = _Build(CasioQvFile.AreaWholeJpeg, [0xFF, 0xD8, 0xFF, 0xD9]);
    data[9] = 0xFF;
    Assert.Throws<InvalidDataException>(() => CasioQvReader.FromBytes(data));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_NoPictureArea_ThrowsInvalidDataException() {
    var data = _Build(1, [1, 2, 3, 4]);
    Assert.Throws<InvalidDataException>(() => CasioQvReader.FromBytes(data));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_StrippedAreaThatDoesNotAddUp_ThrowsInvalidDataException() {
    // The area's own arithmetic — header, two tables, three scans — has to be the whole of it.
    var payload = new byte[CasioQvFile.StrippedHeaderSize + 2 * CasioQvFile.QuantTableSize + 10];
    payload[1] = CasioQvFile.AreaStrippedJpeg;
    payload[3] = 4;
    payload[5] = 4;
    payload[7] = 4;

    var data = _Build(CasioQvFile.AreaStrippedJpeg, payload);
    Assert.Throws<InvalidDataException>(() => CasioQvReader.FromBytes(data));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_WholeStream_IsHandedOverUntouched() {
    var jpeg = _MinimalJpeg();
    var file = CasioQvReader.FromBytes(_Build(CasioQvFile.AreaWholeJpeg, jpeg));

    Assert.That(file.WasReassembled, Is.False);
    Assert.That(file.Jpeg, Is.EqualTo(jpeg));
    Assert.That(file.Width, Is.EqualTo(16));
    Assert.That(file.Height, Is.EqualTo(8));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_StrippedStream_IsPutBackTogether() {
    var payload = new byte[CasioQvFile.StrippedHeaderSize + 2 * CasioQvFile.QuantTableSize + 3];
    payload[1] = CasioQvFile.AreaStrippedJpeg;
    payload[3] = 1;
    payload[5] = 1;
    payload[7] = 1;

    var file = CasioQvReader.FromBytes(_Build(CasioQvFile.AreaStrippedJpeg, payload));

    Assert.That(file.WasReassembled, Is.True);
    Assert.That(file.Width, Is.EqualTo(480));
    Assert.That(file.Height, Is.EqualTo(240));
    Assert.That(file.Jpeg[0], Is.EqualTo(0xFF));
    Assert.That(file.Jpeg[1], Is.EqualTo(0xD8));
    Assert.That(file.Jpeg[^2], Is.EqualTo(0xFF));
    Assert.That(file.Jpeg[^1], Is.EqualTo(0xD9));
  }

  /// <summary>A file of one area: the magic, a count of one, one descriptor, then the payload.</summary>
  private static byte[] _Build(int area, byte[] payload, int slack = 0) {
    using var ms = new MemoryStream();
    ms.Write(CasioQvFile.Magic);
    ms.WriteByte(0);
    ms.WriteByte(1);

    ms.WriteByte((byte)(area >> 8));
    ms.WriteByte((byte)area);
    ms.WriteByte((byte)(payload.Length >> 24));
    ms.WriteByte((byte)(payload.Length >> 16));
    ms.WriteByte((byte)(payload.Length >> 8));
    ms.WriteByte((byte)payload.Length);
    ms.Write(new byte[10], 0, 10);

    ms.Write(payload, 0, payload.Length);
    if (slack > 0)
      ms.Write(new byte[slack], 0, slack);

    return ms.ToArray();
  }

  /// <summary>A baseline grey JPEG frame of 16 by 8, enough to state a size.</summary>
  private static byte[] _MinimalJpeg() => [
    0xFF, 0xD8,
    0xFF, 0xC0, 0x00, 0x0B, 0x08, 0x00, 0x08, 0x00, 0x10, 0x01, 0x01, 0x11, 0x00,
    0xFF, 0xD9
  ];
}
