using System;
using System.Buffers.Binary;
using FileFormat.Ico;

namespace FileFormat.IconLibrary;

/// <summary>Assembles Windows Icon Library bytes from an <see cref="IconLibraryFile"/>.</summary>
/// <remarks>
/// Modern ICL files are resource-only PE DLLs. Windows stores an icon as one
/// <c>RT_GROUP_ICON</c> resource pointing at one or more <c>RT_ICON</c> resources. A newly encoded
/// image becomes one group with one icon; a library that was read keeps the byte-preserving path
/// because the reader does not yet expose its resource tree.
/// </remarks>
public static class IconLibraryWriter {

  private const int _PeOffset = 0x80;
  private const int _OptionalHeaderSize = 0xE0;
  private const int _FileAlignment = 0x200;
  private const int _SectionAlignment = 0x1000;
  private const int _HeadersSize = 0x200;
  private const int _ResourceRva = 0x1000;

  // Four IMAGE_RESOURCE_DIRECTORY tables and their entries, followed by two data entries.
  private const int _ResourceDirectorySize = 0xA0;
  private const int _RtIcon = 3;
  private const int _RtGroupIcon = 14;
  private const int _ResourceId = 1;
  private const int _LanguageNeutral = 0;

  /// <summary>Serializes an icon library.</summary>
  public static byte[] ToBytes(IconLibraryFile file) {
    if (file.EncodedImage is not { } icon)
      return file.RawData.AsSpan().ToArray();

    var resource = _BuildResourceSection(icon);
    var resourceRawSize = _Align(resource.Length, _FileAlignment);
    var result = new byte[_HeadersSize + resourceRawSize];

    _WriteHeaders(result, resource.Length, resourceRawSize);
    resource.CopyTo(result.AsSpan(_HeadersSize));
    return result;
  }

  private static byte[] _BuildResourceSection(IcoImage icon) {
    if (icon.Width is <= 0 or > IcoDib.MaximumSide || icon.Height is <= 0 or > IcoDib.MaximumSide)
      throw new NotSupportedException($"An ICL icon must be between 1 and {IcoDib.MaximumSide} pixels on each side.");
    if (icon.BitsPerPixel is <= 0 or > 32)
      throw new NotSupportedException($"An ICL icon cannot state {icon.BitsPerPixel} bits per pixel.");
    if (icon.Data.Length == 0)
      throw new ArgumentException("An ICL icon cannot have an empty image resource.", nameof(icon));

    var group = _BuildGroupIcon(icon);
    var iconOffset = _ResourceDirectorySize;
    var groupOffset = _Align(iconOffset + icon.Data.Length, 4);
    var result = new byte[groupOffset + group.Length];
    var span = result.AsSpan();

    // PE resources are a three-level tree: type -> name/id -> language. The high bit on an entry's
    // target marks another directory; a clear high bit marks an IMAGE_RESOURCE_DATA_ENTRY.
    _WriteDirectory(span, 0x00, 2);
    _WriteDirectoryEntry(span, 0x10, _RtIcon, 0x20, isDirectory: true);
    _WriteDirectoryEntry(span, 0x18, _RtGroupIcon, 0x38, isDirectory: true);

    _WriteDirectory(span, 0x20, 1);
    _WriteDirectoryEntry(span, 0x30, _ResourceId, 0x50, isDirectory: true);

    _WriteDirectory(span, 0x38, 1);
    _WriteDirectoryEntry(span, 0x48, _ResourceId, 0x68, isDirectory: true);

    _WriteDirectory(span, 0x50, 1);
    _WriteDirectoryEntry(span, 0x60, _LanguageNeutral, 0x80, isDirectory: false);

    _WriteDirectory(span, 0x68, 1);
    _WriteDirectoryEntry(span, 0x78, _LanguageNeutral, 0x90, isDirectory: false);

    _WriteDataEntry(span, 0x80, _ResourceRva + iconOffset, icon.Data.Length);
    _WriteDataEntry(span, 0x90, _ResourceRva + groupOffset, group.Length);

    icon.Data.AsSpan().CopyTo(span[iconOffset..]);
    group.AsSpan().CopyTo(span[groupOffset..]);
    return result;
  }

  private static byte[] _BuildGroupIcon(IcoImage icon) {
    // NEWHEADER (6 bytes) followed by one GRPICONDIRENTRY/RESDIR entry (14 bytes). Unlike an ICO
    // directory, the last field is a WORD resource id rather than a DWORD file offset.
    var result = new byte[20];
    var span = result.AsSpan();

    BinaryPrimitives.WriteUInt16LittleEndian(span, 0);
    BinaryPrimitives.WriteUInt16LittleEndian(span[2..], 1);
    BinaryPrimitives.WriteUInt16LittleEndian(span[4..], 1);
    span[6] = icon.Width == IcoDib.MaximumSide ? (byte)0 : checked((byte)icon.Width);
    span[7] = icon.Height == IcoDib.MaximumSide ? (byte)0 : checked((byte)icon.Height);
    span[8] = 0;
    span[9] = 0;
    BinaryPrimitives.WriteUInt16LittleEndian(span[10..], 1);
    BinaryPrimitives.WriteUInt16LittleEndian(span[12..], checked((ushort)icon.BitsPerPixel));
    BinaryPrimitives.WriteUInt32LittleEndian(span[14..], checked((uint)icon.Data.Length));
    BinaryPrimitives.WriteUInt16LittleEndian(span[18..], _ResourceId);
    return result;
  }

  private static void _WriteHeaders(Span<byte> data, int resourceSize, int resourceRawSize) {
    data[0] = (byte)'M';
    data[1] = (byte)'Z';
    BinaryPrimitives.WriteInt32LittleEndian(data[0x3C..], _PeOffset);

    var signature = data[_PeOffset..];
    signature[0] = (byte)'P';
    signature[1] = (byte)'E';

    var coff = signature[4..];
    BinaryPrimitives.WriteUInt16LittleEndian(coff, 0x014C);
    BinaryPrimitives.WriteUInt16LittleEndian(coff[2..], 1);
    BinaryPrimitives.WriteUInt16LittleEndian(coff[16..], _OptionalHeaderSize);
    BinaryPrimitives.WriteUInt16LittleEndian(coff[18..], 0x2103); // executable, relocations stripped, 32-bit, DLL

    var optional = coff[20..];
    BinaryPrimitives.WriteUInt16LittleEndian(optional, 0x010B);
    BinaryPrimitives.WriteUInt32LittleEndian(optional[8..], checked((uint)resourceRawSize));
    BinaryPrimitives.WriteUInt32LittleEndian(optional[24..], _ResourceRva);
    BinaryPrimitives.WriteUInt32LittleEndian(optional[28..], 0x10000000);
    BinaryPrimitives.WriteUInt32LittleEndian(optional[32..], _SectionAlignment);
    BinaryPrimitives.WriteUInt32LittleEndian(optional[36..], _FileAlignment);
    BinaryPrimitives.WriteUInt16LittleEndian(optional[40..], 4);
    BinaryPrimitives.WriteUInt16LittleEndian(optional[48..], 4);
    BinaryPrimitives.WriteUInt32LittleEndian(optional[56..], checked((uint)_Align(_ResourceRva + resourceSize, _SectionAlignment)));
    BinaryPrimitives.WriteUInt32LittleEndian(optional[60..], _HeadersSize);
    BinaryPrimitives.WriteUInt16LittleEndian(optional[68..], 2);
    BinaryPrimitives.WriteUInt32LittleEndian(optional[72..], 0x00100000);
    BinaryPrimitives.WriteUInt32LittleEndian(optional[76..], 0x00001000);
    BinaryPrimitives.WriteUInt32LittleEndian(optional[80..], 0x00100000);
    BinaryPrimitives.WriteUInt32LittleEndian(optional[84..], 0x00001000);
    BinaryPrimitives.WriteUInt32LittleEndian(optional[92..], 16);

    BinaryPrimitives.WriteUInt32LittleEndian(optional[112..], _ResourceRva);
    BinaryPrimitives.WriteUInt32LittleEndian(optional[116..], checked((uint)resourceSize));

    var section = optional[_OptionalHeaderSize..];
    ".rsrc"u8.CopyTo(section);
    BinaryPrimitives.WriteUInt32LittleEndian(section[8..], checked((uint)resourceSize));
    BinaryPrimitives.WriteUInt32LittleEndian(section[12..], _ResourceRva);
    BinaryPrimitives.WriteUInt32LittleEndian(section[16..], checked((uint)resourceRawSize));
    BinaryPrimitives.WriteUInt32LittleEndian(section[20..], _HeadersSize);
    BinaryPrimitives.WriteUInt32LittleEndian(section[36..], 0x40000040);
  }

  private static void _WriteDirectory(Span<byte> data, int offset, ushort idEntries)
    => BinaryPrimitives.WriteUInt16LittleEndian(data[(offset + 14)..], idEntries);

  private static void _WriteDirectoryEntry(Span<byte> data, int offset, int id, int targetOffset, bool isDirectory) {
    BinaryPrimitives.WriteUInt32LittleEndian(data[offset..], checked((uint)id));
    var target = checked((uint)targetOffset);
    if (isDirectory)
      target |= 0x80000000u;
    BinaryPrimitives.WriteUInt32LittleEndian(data[(offset + 4)..], target);
  }

  private static void _WriteDataEntry(Span<byte> data, int offset, int dataRva, int size) {
    BinaryPrimitives.WriteUInt32LittleEndian(data[offset..], checked((uint)dataRva));
    BinaryPrimitives.WriteUInt32LittleEndian(data[(offset + 4)..], checked((uint)size));
  }

  private static int _Align(int value, int alignment)
    => checked((value + alignment - 1) / alignment * alignment);
}
