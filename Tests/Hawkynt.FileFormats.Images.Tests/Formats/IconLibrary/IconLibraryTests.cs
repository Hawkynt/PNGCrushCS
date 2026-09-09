using System;
using System.Buffers.Binary;
using System.IO;
using System.Reflection.PortableExecutable;
using FileFormat.Core;
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
  public void FromBytes_ValidData_Parses() {
    var data = new byte[22];
    data[0] = 0; data[1] = 0; // reserved
    data[2] = 1; data[3] = 0; // type = 1 (icon)
    data[4] = 1; data[5] = 0; // count = 1
    data[6] = 16; // width
    data[7] = 16; // height

    var result = IconLibraryReader.FromBytes(data);

    Assert.That(result.Width, Is.EqualTo(16));
    Assert.That(result.Height, Is.EqualTo(16));
    Assert.That(result.RawData.Length, Is.EqualTo(22));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_DefaultDimensions_WhenNoIcoHeader() {
    var data = new byte[10];
    data[0] = 0xFF; // not a valid ICO header

    var result = IconLibraryReader.FromBytes(data);

    Assert.That(result.Width, Is.EqualTo(32));
    Assert.That(result.Height, Is.EqualTo(32));
  }
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
