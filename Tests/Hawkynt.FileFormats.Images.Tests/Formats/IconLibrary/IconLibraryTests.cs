using System;
using System.Buffers.Binary;
using System.IO;
using System.Reflection.PortableExecutable;
using FileFormat.Core;
using FileFormat.Ico;
using FileFormat.IconLibrary;

namespace FileFormat.IconLibrary.Tests;

[TestFixture]
public sealed class IconLibraryReaderTests {

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => IconLibraryReader.FromFile(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".icl"));
    Assert.Throws<FileNotFoundException>(() => IconLibraryReader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => IconLibraryReader.FromBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException() {
    Assert.Throws<InvalidDataException>(() => IconLibraryReader.FromBytes(new byte[3]));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_IcoFile_ThrowsInvalidDataException() {
    byte[] ico = [0, 0, 1, 0, 1, 0, 16, 16, 0, 0, 1, 0, 32, 0, 0, 0, 0, 0, 22, 0, 0, 0];
    Assert.Throws<InvalidDataException>(() => IconLibraryReader.FromBytes(ico));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_PeLibrary_ExtractsAndDecodesIcon() {
    var source = _CreateSource();
    var bytes = IconLibraryWriter.ToBytes(IconLibraryFile.FromRawImage(source));

    var file = IconLibraryReader.FromBytes(bytes);
    var decoded = IconLibraryFile.ToRawImage(file);

    Assert.Multiple(() => {
      Assert.That(file.Width, Is.EqualTo(2));
      Assert.That(file.Height, Is.EqualTo(2));
      Assert.That(file.Icons, Has.Count.EqualTo(1));
      Assert.That(file.Icons[0].Images, Has.Count.EqualTo(1));
      Assert.That(file.RawData, Is.EqualTo(bytes));
      Assert.That(IconLibraryFile.ImageCount(file), Is.EqualTo(1));
      Assert.That(decoded.Width, Is.EqualTo(source.Width));
      Assert.That(decoded.Height, Is.EqualTo(source.Height));
      Assert.That(decoded.Format, Is.EqualTo(PixelFormat.Bgra32));
      Assert.That(decoded.PixelData, Is.EqualTo(source.PixelData));
    });
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_NeLibrary_ExtractsAndDecodesIcon() {
    var source = _CreateSource();
    var icon = IcoDib.FromRawImage(source);
    var bytes = _BuildNeLibrary(icon);

    var file = IconLibraryReader.FromBytes(bytes);
    var decoded = IconLibraryFile.ToRawImage(file);

    Assert.Multiple(() => {
      Assert.That(file.Width, Is.EqualTo(2));
      Assert.That(file.Height, Is.EqualTo(2));
      Assert.That(file.Icons, Has.Count.EqualTo(1));
      Assert.That(file.Icons[0].Images, Has.Count.EqualTo(1));
      Assert.That(decoded.PixelData, Is.EqualTo(source.PixelData));
    });
  }

  private static RawImage _CreateSource() => new() {
    Width = 2,
    Height = 2,
    Format = PixelFormat.Bgra32,
    PixelData = [
      0x00, 0x00, 0xFF, 0xFF,  0x00, 0xFF, 0x00, 0xFF,
      0xFF, 0x00, 0x00, 0x00,  0xFF, 0xFF, 0xFF, 0xFF,
    ],
  };

  private static byte[] _BuildNeLibrary(IcoImage icon) {
    const int neOffset = 0x40;
    const int resourceTableOffset = 0x80;
    const int alignmentShift = 4;
    const int iconOffset = 0x100;
    var iconStorageLength = _Align(icon.Data.Length, 1 << alignmentShift);
    var groupOffset = checked(iconOffset + iconStorageLength);
    const int groupLength = 20;
    var groupStorageLength = _Align(groupLength, 1 << alignmentShift);
    var result = new byte[checked(groupOffset + groupStorageLength)];

    result[0] = (byte)'M';
    result[1] = (byte)'Z';
    BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(0x3C), neOffset);
    result[neOffset] = (byte)'N';
    result[neOffset + 1] = (byte)'E';
    BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(neOffset + 0x24), resourceTableOffset - neOffset);

    var cursor = resourceTableOffset;
    BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(cursor), alignmentShift);
    cursor += 2;
    cursor = _WriteNeType(result, cursor, 3, iconOffset, iconStorageLength, 1, alignmentShift);
    cursor = _WriteNeType(result, cursor, 14, groupOffset, groupStorageLength, 1, alignmentShift);
    BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(cursor), 0);

    icon.Data.CopyTo(result.AsSpan(iconOffset));
    var group = result.AsSpan(groupOffset, groupLength);
    BinaryPrimitives.WriteUInt16LittleEndian(group, 0);
    BinaryPrimitives.WriteUInt16LittleEndian(group[2..], 1);
    BinaryPrimitives.WriteUInt16LittleEndian(group[4..], 1);
    group[6] = checked((byte)icon.Width);
    group[7] = checked((byte)icon.Height);
    BinaryPrimitives.WriteUInt16LittleEndian(group[10..], 1);
    BinaryPrimitives.WriteUInt16LittleEndian(group[12..], checked((ushort)icon.BitsPerPixel));
    BinaryPrimitives.WriteUInt32LittleEndian(group[14..], checked((uint)icon.Data.Length));
    BinaryPrimitives.WriteUInt16LittleEndian(group[18..], 1);
    return result;
  }

  private static int _WriteNeType(byte[] data, int offset, int type, int resourceOffset, int resourceLength, int id, int shift) {
    BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(offset), checked((ushort)(0x8000 | type)));
    BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(offset + 2), 1);
    offset += 8;
    BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(offset), checked((ushort)(resourceOffset >> shift)));
    BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(offset + 2), checked((ushort)(resourceLength >> shift)));
    BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(offset + 4), 0x0010);
    BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(offset + 6), checked((ushort)(0x8000 | id)));
    return offset + 12;
  }

  private static int _Align(int value, int alignment) => (value + alignment - 1) / alignment * alignment;
}

[TestFixture]
public sealed class IconLibraryWriterTests {

  [Test]
  [Category("Unit")]
  public void ToBytes_ParsedLibrary_PreservesOriginalBytes() {
    byte[] rawData = [0x4D, 0x5A, 1, 2, 3, 4, 5, 6];
    var original = new IconLibraryFile { RawData = rawData, Width = 32, Height = 32 };

    var bytes = IconLibraryWriter.ToBytes(original);

    Assert.That(bytes, Is.EqualTo(rawData));
    Assert.That(bytes, Is.Not.SameAs(rawData));
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_WritesResourceOnlyPeWithIconGroup() {
    var source = new RawImage {
      Width = 2,
      Height = 2,
      Format = PixelFormat.Bgra32,
      PixelData = [
        0x00, 0x00, 0xFF, 0xFF,  0x00, 0xFF, 0x00, 0xFF,
        0xFF, 0x00, 0x00, 0x00,  0xFF, 0xFF, 0xFF, 0xFF,
      ],
    };

    var bytes = IconLibraryWriter.ToBytes(IconLibraryFile.FromRawImage(source));

    using var pe = new PEReader(new MemoryStream(bytes, writable: false));
    Assert.Multiple(() => {
      Assert.That(pe.HasMetadata, Is.False, "an ICL is a native resource library, not a managed assembly");
      Assert.That(pe.PEHeaders.PEHeader, Is.Not.Null);
      Assert.That(pe.PEHeaders.SectionHeaders.Length, Is.EqualTo(1));
      Assert.That(pe.PEHeaders.SectionHeaders[0].Name, Is.EqualTo(".rsrc"));
    });

    var icon = _ReadResource(bytes, 3);
    var group = _ReadResource(bytes, 14);

    Assert.Multiple(() => {
      Assert.That(BinaryPrimitives.ReadInt32LittleEndian(icon), Is.EqualTo(40));
      Assert.That(BinaryPrimitives.ReadInt32LittleEndian(icon.AsSpan(4)), Is.EqualTo(2));
      Assert.That(BinaryPrimitives.ReadInt32LittleEndian(icon.AsSpan(8)), Is.EqualTo(4), "icon DIB height includes colour and mask planes");
      Assert.That(BinaryPrimitives.ReadUInt16LittleEndian(icon.AsSpan(12)), Is.EqualTo(1));
      Assert.That(BinaryPrimitives.ReadUInt16LittleEndian(icon.AsSpan(14)), Is.EqualTo(32));
      Assert.That(icon.Length, Is.EqualTo(64));
      Assert.That(icon[56], Is.EqualTo(0x80), "the transparent lower-left pixel is set in the AND mask");

      Assert.That(BinaryPrimitives.ReadUInt16LittleEndian(group), Is.Zero);
      Assert.That(BinaryPrimitives.ReadUInt16LittleEndian(group.AsSpan(2)), Is.EqualTo(1));
      Assert.That(BinaryPrimitives.ReadUInt16LittleEndian(group.AsSpan(4)), Is.EqualTo(1));
      Assert.That(group[6], Is.EqualTo(2));
      Assert.That(group[7], Is.EqualTo(2));
      Assert.That(BinaryPrimitives.ReadUInt16LittleEndian(group.AsSpan(10)), Is.EqualTo(1));
      Assert.That(BinaryPrimitives.ReadUInt16LittleEndian(group.AsSpan(12)), Is.EqualTo(32));
      Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(group.AsSpan(14)), Is.EqualTo((uint)icon.Length));
      Assert.That(BinaryPrimitives.ReadUInt16LittleEndian(group.AsSpan(18)), Is.EqualTo(1), "the group points at RT_ICON #1");
    });
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_RoundTripsThroughRegisteredContracts() {
    var source = new RawImage {
      Width = 1,
      Height = 1,
      Format = PixelFormat.Bgra32,
      PixelData = [0x21, 0x43, 0x65, 0xFF],
    };

    var encoded = FormatIO.Write(IconLibraryFile.FromRawImage(source));
    var parsed = IconLibraryReader.FromBytes(encoded);
    var decoded = IconLibraryFile.ToRawImage(parsed);

    Assert.That(decoded.PixelData, Is.EqualTo(source.PixelData));
  }

  private static byte[] _ReadResource(byte[] file, int resourceType) {
    using var pe = new PEReader(new MemoryStream(file, writable: false));
    var sections = pe.PEHeaders.SectionHeaders;
    if (sections.Length != 1 || sections[0].Name != ".rsrc")
      throw new InvalidDataException("Expected one .rsrc section.");

    var section = sections[0];
    var resourceBase = section.PointerToRawData;
    var typeTarget = _FindIdTarget(file, resourceBase, 0, resourceType);
    var nameTarget = _FindIdTarget(file, resourceBase, _DirectoryOffset(typeTarget), 1);
    var languageDirectory = _DirectoryOffset(nameTarget);
    var languageTarget = _FirstTarget(file, resourceBase, languageDirectory);
    if ((languageTarget & 0x80000000u) != 0)
      throw new InvalidDataException("Resource language entry points at a directory instead of data.");

    var dataEntry = checked(resourceBase + (int)languageTarget);
    var dataRva = BinaryPrimitives.ReadUInt32LittleEndian(file.AsSpan(dataEntry));
    var size = BinaryPrimitives.ReadUInt32LittleEndian(file.AsSpan(dataEntry + 4));
    var fileOffset = checked(section.PointerToRawData + (int)dataRva - section.VirtualAddress);
    return file.AsSpan(fileOffset, checked((int)size)).ToArray();
  }

  private static uint _FindIdTarget(byte[] file, int resourceBase, int directoryOffset, int id) {
    var directory = checked(resourceBase + directoryOffset);
    var named = BinaryPrimitives.ReadUInt16LittleEndian(file.AsSpan(directory + 12));
    var ids = BinaryPrimitives.ReadUInt16LittleEndian(file.AsSpan(directory + 14));
    for (var i = 0; i < named + ids; ++i) {
      var entry = checked(directory + 16 + i * 8);
      var name = BinaryPrimitives.ReadUInt32LittleEndian(file.AsSpan(entry));
      if ((name & 0x80000000u) == 0 && name == (uint)id)
        return BinaryPrimitives.ReadUInt32LittleEndian(file.AsSpan(entry + 4));
    }

    throw new InvalidDataException($"Resource id {id} was not found.");
  }

  private static uint _FirstTarget(byte[] file, int resourceBase, int directoryOffset) {
    var directory = checked(resourceBase + directoryOffset);
    var named = BinaryPrimitives.ReadUInt16LittleEndian(file.AsSpan(directory + 12));
    var ids = BinaryPrimitives.ReadUInt16LittleEndian(file.AsSpan(directory + 14));
    if (named + ids == 0)
      throw new InvalidDataException("Resource directory is empty.");

    return BinaryPrimitives.ReadUInt32LittleEndian(file.AsSpan(directory + 20));
  }

  private static int _DirectoryOffset(uint target) {
    if ((target & 0x80000000u) == 0)
      throw new InvalidDataException("Resource entry points at data instead of a directory.");
    return checked((int)(target & 0x7FFFFFFFu));
  }
}
