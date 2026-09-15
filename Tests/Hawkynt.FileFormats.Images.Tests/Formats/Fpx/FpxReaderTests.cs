using System;
using System.Buffers.Binary;
using System.IO;
using System.Text;
using FileFormat.Fpx;

namespace FileFormat.Fpx.Tests;

[TestFixture]
public sealed class FpxReaderTests {

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => FpxReader.FromBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => FpxReader.FromFile(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".fpx"));
    Assert.Throws<FileNotFoundException>(() => FpxReader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => FpxReader.FromStream(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException() {
    Assert.Throws<InvalidDataException>(() => FpxReader.FromBytes(new byte[10]));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TheOldInventedHeaderIsNotAFlashPixFile() {
    var data = new byte[19];
    Encoding.ASCII.GetBytes("FPX\0").CopyTo(data, 0);
    BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(8), 1);
    BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(12), 1);

    Assert.Throws<InvalidDataException>(() => FpxReader.FromBytes(data));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ACompoundFileWithoutImageContentsIsRefused() {
    var data = FpxFixture.Document();
    FpxFixture.Rename(data, "Image Contents", "Workbook      ");

    Assert.Throws<InvalidDataException>(() => FpxReader.FromBytes(data));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ATileCountThatIsNotTheGridIsRefused() {
    var thrown = Assert.Throws<InvalidDataException>(() => FpxReader.FromBytes(FpxFixture.Document(tileCount: 2)));
    Assert.That(thrown!.Message, Does.Contain("tiles"));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ATileWidthOtherThanSixtyFourIsRefused() {
    Assert.Throws<InvalidDataException>(() => FpxReader.FromBytes(FpxFixture.Document(tileWidth: 128)));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ATileHeightOtherThanSixtyFourIsRefused() {
    Assert.Throws<InvalidDataException>(() => FpxReader.FromBytes(FpxFixture.Document(tileHeight: 128)));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_AnUndefinedCompressionIsRefused() {
    var thrown = Assert.Throws<InvalidDataException>(() => FpxReader.FromBytes(FpxFixture.Document(compression: 7)));
    Assert.That(thrown!.Message, Does.Contain("compression 7"));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_AWrongSubimageClassIsRefused() {
    var data = FpxFixture.Document();
    FpxFixture.ReplaceGuid(data, new("00010000-C154-11CE-8553-00AA00A1F95B"), Guid.Empty);

    var thrown = Assert.Throws<InvalidDataException>(() => FpxReader.FromBytes(data));
    Assert.That(thrown!.Message, Does.Contain("class ID"));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ASingleNifRgbColourTileFillsTheSubimage() {
    var read = FpxReader.FromBytes(FpxFixture.Document(subtype: 0x00112233));

    Assert.That(read.Width, Is.EqualTo(3));
    Assert.That(read.Height, Is.EqualTo(2));
    Assert.That(read.PixelData, Is.EqualTo(new byte[] {
      0x33, 0x22, 0x11, 0x33, 0x22, 0x11, 0x33, 0x22, 0x11,
      0x33, 0x22, 0x11, 0x33, 0x22, 0x11, 0x33, 0x22, 0x11,
    }));
  }
}

/// <summary>Builds focused synthetic FlashPix-in-CFB documents for malformed-reader cases.</summary>
internal static class FpxFixture {

  private const int _SectorSize = 512;
  private const int _HeaderSize = 512;
  private const uint _EndOfChain = 0xFFFFFFFE;
  private const uint _FatSector = 0xFFFFFFFD;
  private const uint _Free = 0xFFFFFFFF;

  private static readonly Guid _ImageContentsClass = new("56616400-C154-11CE-8553-00AA00A1F95B");
  private static readonly Guid _SubimageHeaderClass = new("00010000-C154-11CE-8553-00AA00A1F95B");
  private static readonly Guid _SubimageDataClass = new("00010100-C154-11CE-8553-00AA00A1F95B");

  internal static byte[] Document(
    int tileCount = 1,
    int tileWidth = 64,
    int tileHeight = 64,
    int compression = 1,
    uint subtype = 0x00FFFFFF) {

    var contents = _BuildImageContents();

    var headerPayload = new byte[36 + tileCount * 16];
    BinaryPrimitives.WriteUInt32LittleEndian(headerPayload.AsSpan(0), 36);
    BinaryPrimitives.WriteUInt32LittleEndian(headerPayload.AsSpan(4), 3);
    BinaryPrimitives.WriteUInt32LittleEndian(headerPayload.AsSpan(8), 2);
    BinaryPrimitives.WriteUInt32LittleEndian(headerPayload.AsSpan(12), (uint)tileCount);
    BinaryPrimitives.WriteUInt32LittleEndian(headerPayload.AsSpan(16), (uint)tileWidth);
    BinaryPrimitives.WriteUInt32LittleEndian(headerPayload.AsSpan(20), (uint)tileHeight);
    BinaryPrimitives.WriteUInt32LittleEndian(headerPayload.AsSpan(24), 3);
    BinaryPrimitives.WriteUInt32LittleEndian(headerPayload.AsSpan(28), 36);
    BinaryPrimitives.WriteUInt32LittleEndian(headerPayload.AsSpan(32), 16);

    for (var i = 0; i < tileCount; ++i) {
      var at = 36 + i * 16;
      BinaryPrimitives.WriteUInt32LittleEndian(headerPayload.AsSpan(at), 0);
      BinaryPrimitives.WriteUInt32LittleEndian(headerPayload.AsSpan(at + 4), 0);
      BinaryPrimitives.WriteUInt32LittleEndian(headerPayload.AsSpan(at + 8), (uint)compression);
      BinaryPrimitives.WriteUInt32LittleEndian(headerPayload.AsSpan(at + 12), subtype);
    }

    var header = _BuildFlashPixStream(_SubimageHeaderClass, headerPayload);
    var tiles = _BuildFlashPixStream(_SubimageDataClass, []);

    return _Build([
      ("Data Object Store 000001", 1, []),
      ("Image Contents", 2, contents),
      ("Resolution 0000", 1, []),
      ("Subimage 0000 Header", 2, header),
      ("Subimage 0000 Data", 2, tiles),
    ]);
  }

  internal static void Rename(byte[] document, string from, string to) {
    var wanted = Encoding.Unicode.GetBytes(from);
    for (var at = _HeaderSize + _SectorSize; at + wanted.Length <= document.Length; at += 128) {
      if (!document.AsSpan(at, wanted.Length).SequenceEqual(wanted))
        continue;

      Encoding.Unicode.GetBytes(to).CopyTo(document.AsSpan(at));
      return;
    }

    throw new InvalidOperationException($"No directory entry named {from}.");
  }

  internal static void ReplaceGuid(byte[] document, Guid from, Guid to) {
    Span<byte> wanted = stackalloc byte[16];
    Span<byte> replacement = stackalloc byte[16];
    from.TryWriteBytes(wanted);
    to.TryWriteBytes(replacement);

    for (var at = _HeaderSize; at + wanted.Length <= document.Length; ++at) {
      if (!document.AsSpan(at, wanted.Length).SequenceEqual(wanted))
        continue;

      replacement.CopyTo(document.AsSpan(at));
      return;
    }

    throw new InvalidOperationException($"No GUID {from:B} found in fixture.");
  }

  private static byte[] _BuildImageContents() {
    const int sectionAt = 48;
    const int propertyCount = 2;
    const int tableSize = 8 + propertyCount * 8;
    const int codePageSize = 8;
    const int colorSize = 28;
    var sectionSize = tableSize + codePageSize + colorSize;
    var result = new byte[sectionAt + sectionSize];

    BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(0), 0xFFFE);
    BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(24), 1);
    _ImageContentsClass.TryWriteBytes(result.AsSpan(28, 16));
    BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(44), sectionAt);
    BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(sectionAt), (uint)sectionSize);
    BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(sectionAt + 4), propertyCount);

    var codePageAt = tableSize;
    BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(sectionAt + 8), 1);
    BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(sectionAt + 12), (uint)codePageAt);
    BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(sectionAt + codePageAt), 2);
    BinaryPrimitives.WriteInt16LittleEndian(result.AsSpan(sectionAt + codePageAt + 4), 1200);

    var colorAt = codePageAt + codePageSize;
    BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(sectionAt + 16), 0x02000002);
    BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(sectionAt + 20), (uint)colorAt);
    BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(sectionAt + colorAt), 65);
    BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(sectionAt + colorAt + 4), 20);
    BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(sectionAt + colorAt + 8), 1);
    BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(sectionAt + colorAt + 12), 3);
    BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(sectionAt + colorAt + 16), 0x00030000);
    BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(sectionAt + colorAt + 20), 0x00030001);
    BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(sectionAt + colorAt + 24), 0x00030002);
    return result;
  }

  private static byte[] _BuildFlashPixStream(Guid classId, ReadOnlySpan<byte> payload) {
    var result = new byte[28 + payload.Length];
    BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(0), 0xFFFE);
    BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(2), 0);
    classId.TryWriteBytes(result.AsSpan(8, 16));
    BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(24), 1);
    payload.CopyTo(result.AsSpan(28));
    return result;
  }

  private static byte[] _Build((string Name, byte Type, byte[] Data)[] entries) {
    const int directorySectors = 2;
    var streamSectors = 0;
    foreach (var entry in entries)
      if (entry.Type == 2)
        ++streamSectors;

    var sectors = 1 + directorySectors + streamSectors;
    var file = new byte[_HeaderSize + sectors * _SectorSize];

    ReadOnlySpan<byte> signature = [0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1];
    signature.CopyTo(file);
    BinaryPrimitives.WriteUInt16LittleEndian(file.AsSpan(26), 3);
    BinaryPrimitives.WriteUInt16LittleEndian(file.AsSpan(28), 0xFFFE);
    BinaryPrimitives.WriteUInt16LittleEndian(file.AsSpan(30), 9);
    BinaryPrimitives.WriteUInt16LittleEndian(file.AsSpan(32), 6);
    BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(44), 1);
    BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(48), 1);
    BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(56), 0);
    BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(60), _EndOfChain);
    BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(64), 0);
    BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(68), _EndOfChain);
    BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(72), 0);
    BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(76), 0);
    for (var i = 1; i < 109; ++i)
      BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(76 + i * 4), _Free);

    var fat = _HeaderSize;
    for (var i = 0; i < _SectorSize / 4; ++i)
      BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(fat + i * 4), _Free);

    BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(fat), _FatSector);
    BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(fat + 4), 2);
    BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(fat + 8), _EndOfChain);
    for (var i = 0; i < streamSectors; ++i)
      BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(fat + (3 + i) * 4), _EndOfChain);

    var directory = _HeaderSize + _SectorSize;
    _WriteEntry(file, directory, "Root Entry", 5, 0, 0, _Free, _Free, 1);

    var nextSector = 3u;
    for (var i = 0; i < entries.Length; ++i) {
      var (name, type, data) = entries[i];
      var at = directory + (i + 1) * 128;
      var start = _Free;

      if (type == 2) {
        start = nextSector;
        data.CopyTo(file.AsSpan(_HeaderSize + (int)nextSector * _SectorSize));
        ++nextSector;
      }

      var right = i + 1 < entries.Length && _ParentOf(entries, i) == _ParentOf(entries, i + 1)
        ? (uint)(i + 2)
        : _Free;
      var child = type == 1 ? (uint)(i + 2) : _Free;

      _WriteEntry(file, at, name, type, start, data.Length, _Free, right, child);
    }

    return file;
  }

  private static int _ParentOf((string Name, byte Type, byte[] Data)[] entries, int index) {
    var parent = -1;
    for (var i = 0; i < index; ++i)
      if (entries[i].Type == 1)
        parent = i;

    return parent;
  }

  private static void _WriteEntry(
    byte[] file, int at, string name, byte type, uint start, long size, uint left, uint right, uint child) {

    var bytes = Encoding.Unicode.GetBytes(name);
    bytes.CopyTo(file.AsSpan(at));
    BinaryPrimitives.WriteUInt16LittleEndian(file.AsSpan(at + 64), (ushort)(bytes.Length + 2));
    file[at + 66] = type;
    file[at + 67] = 1;
    BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(at + 68), left);
    BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(at + 72), right);
    BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(at + 76), child);
    BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(at + 116), start);
    BinaryPrimitives.WriteUInt64LittleEndian(file.AsSpan(at + 120), (ulong)size);
  }
}
