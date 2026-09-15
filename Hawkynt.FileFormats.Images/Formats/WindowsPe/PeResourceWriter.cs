using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace FileFormat.WindowsPe;

/// <summary>Writes Windows PE files whose resources carry the images represented by <see cref="PeResourceFile"/>.</summary>
public static class PeResourceWriter {

  private const int _RT_CURSOR = 1;
  private const int _RT_BITMAP = 2;
  private const int _RT_ICON = 3;
  private const int _RT_RCDATA = 10;
  private const int _RT_GROUP_CURSOR = 12;
  private const int _RT_GROUP_ICON = 14;

  private const int _DOS_HEADER_SIZE = 0x80;
  private const int _COFF_HEADER_SIZE = 20;
  private const int _OPTIONAL_HEADER_SIZE = 0xE0;
  private const int _SECTION_HEADER_SIZE = 40;
  private const int _FILE_ALIGNMENT = 0x200;
  private const int _SECTION_ALIGNMENT = 0x1000;
  private const uint _DIRECTORY_FLAG = 0x80000000u;

  private const ushort _IMAGE_FILE_MACHINE_I386 = 0x014C;
  private const ushort _IMAGE_FILE_EXECUTABLE_IMAGE = 0x0002;
  private const ushort _IMAGE_FILE_32BIT_MACHINE = 0x0100;
  private const ushort _IMAGE_FILE_DLL = 0x2000;

  private const uint _IMAGE_SCN_CNT_CODE = 0x00000020;
  private const uint _IMAGE_SCN_CNT_INITIALIZED_DATA = 0x00000040;
  private const uint _IMAGE_SCN_MEM_EXECUTE = 0x20000000;
  private const uint _IMAGE_SCN_MEM_READ = 0x40000000;

  private readonly record struct ResourceEntry(int TypeId, int ResourceId, byte[] Data);

  public static byte[] ToBytes(PeResourceFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var resourceEntries = _CollectResources(file);
    if (resourceEntries.Count == 0)
      throw new InvalidDataException("PE resource file contains no image resources to write.");

    var resourceRva = file.ModuleKind == PeResourceModuleKind.Executable ? 0x2000u : 0x1000u;
    var resourceSection = _BuildResourceSection(resourceEntries, resourceRva);
    return _BuildPe(resourceSection, resourceRva, file.ModuleKind);
  }

  private static List<ResourceEntry> _CollectResources(PeResourceFile file) {
    var images = new List<PeImageResource>(file.ImageResources);
    foreach (var group in file.IconGroups) {
      var type = group.IsCursor ? PeImageResourceType.Cursor : PeImageResourceType.Icon;
      if (images.Any(resource => resource.ResourceType == type && resource.ResourceId == group.GroupId))
        continue;

      images.Add(new PeImageResource {
        ResourceType = type,
        ResourceId = group.GroupId,
        Data = group.IcoData,
      });
    }

    var result = new List<ResourceEntry>();
    var used = new HashSet<(int TypeId, int ResourceId)>();
    var nextIconId = 1;
    var nextCursorId = 1;

    foreach (var resource in images) {
      _ValidateResourceId(resource.ResourceId);

      switch (resource.ResourceType) {
        case PeImageResourceType.Icon:
          _AddIconGroup(result, used, resource, isCursor: false, ref nextIconId);
          break;

        case PeImageResourceType.Cursor:
          _AddIconGroup(result, used, resource, isCursor: true, ref nextCursorId);
          break;

        case PeImageResourceType.Bitmap:
          if (resource.Data.Length < 14 || resource.Data[0] != (byte)'B' || resource.Data[1] != (byte)'M')
            throw new InvalidDataException($"RT_BITMAP resource {resource.ResourceId} must contain a complete BMP file.");

          _AddUnique(result, used, new ResourceEntry(_RT_BITMAP, resource.ResourceId, resource.Data[14..]));
          break;

        case PeImageResourceType.EmbeddedImage:
          if (resource.Data.Length == 0)
            throw new InvalidDataException($"Embedded image resource {resource.ResourceId} contains no data.");

          _AddUnique(result, used, new ResourceEntry(_RT_RCDATA, resource.ResourceId, resource.Data[..]));
          break;

        default:
          throw new ArgumentOutOfRangeException(nameof(resource.ResourceType), resource.ResourceType, "Unknown PE image resource type.");
      }
    }

    return result;
  }

  private static void _AddIconGroup(
    List<ResourceEntry> result,
    HashSet<(int TypeId, int ResourceId)> used,
    PeImageResource resource,
    bool isCursor,
    ref int nextComponentId
  ) {
    var data = resource.Data;
    if (data.Length < 6)
      throw new InvalidDataException($"Resource {resource.ResourceId} is too short to be an {(isCursor ? "CUR" : "ICO")} file.");

    var expectedType = isCursor ? 2 : 1;
    var reserved = BinaryPrimitives.ReadUInt16LittleEndian(data);
    var type = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(2));
    var count = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(4));
    if (reserved != 0 || type != expectedType || count == 0)
      throw new InvalidDataException($"Resource {resource.ResourceId} is not a valid {(isCursor ? "CUR" : "ICO")} directory.");

    var directoryEnd = checked(6 + count * 16);
    if (directoryEnd > data.Length)
      throw new InvalidDataException($"Resource {resource.ResourceId} has a truncated {(isCursor ? "CUR" : "ICO")} directory.");

    var groupDirectory = new byte[checked(6 + count * 14)];
    BinaryPrimitives.WriteUInt16LittleEndian(groupDirectory.AsSpan(2), (ushort)expectedType);
    BinaryPrimitives.WriteUInt16LittleEndian(groupDirectory.AsSpan(4), count);

    for (var i = 0; i < count; ++i) {
      var sourceOffset = 6 + i * 16;
      var bytesInRes = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(sourceOffset + 8));
      var imageOffset = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(sourceOffset + 12));
      if (bytesInRes == 0 || imageOffset > int.MaxValue || bytesInRes > int.MaxValue
          || imageOffset + bytesInRes > (uint)data.Length)
        throw new InvalidDataException($"Resource {resource.ResourceId} has an out-of-range image directory entry.");

      if (nextComponentId > ushort.MaxValue)
        throw new InvalidDataException("A PE icon/cursor resource cannot reference more than 65,535 component IDs.");

      var componentId = nextComponentId++;
      var imageData = data.AsSpan((int)imageOffset, (int)bytesInRes).ToArray();
      var groupOffset = 6 + i * 14;

      if (isCursor) {
        var width = data[sourceOffset] == 0 ? 256 : data[sourceOffset];
        var height = data[sourceOffset + 1] == 0 ? 256 : data[sourceOffset + 1];
        var hotspotX = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(sourceOffset + 4));
        var hotspotY = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(sourceOffset + 6));
        var (planes, bitCount) = _ReadCursorPixelLayout(imageData);

        var cursorData = new byte[checked(4 + imageData.Length)];
        BinaryPrimitives.WriteUInt16LittleEndian(cursorData, hotspotX);
        BinaryPrimitives.WriteUInt16LittleEndian(cursorData.AsSpan(2), hotspotY);
        imageData.CopyTo(cursorData.AsSpan(4));

        BinaryPrimitives.WriteUInt16LittleEndian(groupDirectory.AsSpan(groupOffset), (ushort)width);
        BinaryPrimitives.WriteUInt16LittleEndian(groupDirectory.AsSpan(groupOffset + 2), (ushort)height);
        BinaryPrimitives.WriteUInt16LittleEndian(groupDirectory.AsSpan(groupOffset + 4), planes);
        BinaryPrimitives.WriteUInt16LittleEndian(groupDirectory.AsSpan(groupOffset + 6), bitCount);
        BinaryPrimitives.WriteUInt32LittleEndian(groupDirectory.AsSpan(groupOffset + 8), (uint)cursorData.Length);
        BinaryPrimitives.WriteUInt16LittleEndian(groupDirectory.AsSpan(groupOffset + 12), (ushort)componentId);

        _AddUnique(result, used, new ResourceEntry(_RT_CURSOR, componentId, cursorData));
      } else {
        data.AsSpan(sourceOffset, 4).CopyTo(groupDirectory.AsSpan(groupOffset, 4));
        data.AsSpan(sourceOffset + 4, 8).CopyTo(groupDirectory.AsSpan(groupOffset + 4, 8));
        BinaryPrimitives.WriteUInt16LittleEndian(groupDirectory.AsSpan(groupOffset + 12), (ushort)componentId);

        _AddUnique(result, used, new ResourceEntry(_RT_ICON, componentId, imageData));
      }
    }

    _AddUnique(
      result,
      used,
      new ResourceEntry(isCursor ? _RT_GROUP_CURSOR : _RT_GROUP_ICON, resource.ResourceId, groupDirectory)
    );
  }

  private static (ushort Planes, ushort BitCount) _ReadCursorPixelLayout(ReadOnlySpan<byte> data) {
    if (data.Length >= 16) {
      var dibHeaderSize = BinaryPrimitives.ReadUInt32LittleEndian(data);
      if (dibHeaderSize >= 16 && dibHeaderSize <= data.Length) {
        var planes = BinaryPrimitives.ReadUInt16LittleEndian(data[12..]);
        var bitCount = BinaryPrimitives.ReadUInt16LittleEndian(data[14..]);
        if (planes != 0 && bitCount != 0)
          return (planes, bitCount);
      }
    }

    if (data.Length >= 26
        && data[0] == 0x89 && data[1] == (byte)'P' && data[2] == (byte)'N' && data[3] == (byte)'G'
        && data[4] == 0x0D && data[5] == 0x0A && data[6] == 0x1A && data[7] == 0x0A) {
      var bitDepth = data[24];
      var channels = data[25] switch {
        0 => 1,
        2 => 3,
        3 => 1,
        4 => 2,
        6 => 4,
        _ => 0,
      };
      if (channels != 0)
        return (1, checked((ushort)(channels * bitDepth)));
    }

    throw new InvalidDataException("CUR image data must contain a DIB or PNG header so its resource group can state planes and bit depth.");
  }

  private static void _AddUnique(
    List<ResourceEntry> result,
    HashSet<(int TypeId, int ResourceId)> used,
    ResourceEntry entry
  ) {
    if (!used.Add((entry.TypeId, entry.ResourceId)))
      throw new InvalidDataException($"Duplicate PE resource type {entry.TypeId}, ID {entry.ResourceId}.");

    result.Add(entry);
  }

  private static void _ValidateResourceId(int resourceId) {
    if (resourceId < 0)
      throw new InvalidDataException($"PE resource IDs must be non-negative, not {resourceId}.");
  }

  private static byte[] _BuildResourceSection(List<ResourceEntry> resources, uint resourceRva) {
    var groups = resources
      .OrderBy(resource => resource.TypeId)
      .ThenBy(resource => resource.ResourceId)
      .GroupBy(resource => resource.TypeId)
      .Select(group => (TypeId: group.Key, Resources: group.ToArray()))
      .ToArray();

    if (groups.Length > ushort.MaxValue)
      throw new InvalidDataException("PE resource directory contains too many resource types.");

    foreach (var group in groups)
      if (group.Resources.Length > ushort.MaxValue)
        throw new InvalidDataException($"PE resource type {group.TypeId} contains too many resources.");

    var resourceCount = resources.Count;
    var topLevelSize = checked(16 + groups.Length * 8);
    var secondLevelSize = groups.Sum(group => checked(16 + group.Resources.Length * 8));
    var thirdLevelSize = checked(resourceCount * 24);
    var dataEntrySize = checked(resourceCount * 16);
    var dataStart = _Align4(checked(topLevelSize + secondLevelSize + thirdLevelSize + dataEntrySize));

    var dataSize = 0;
    foreach (var resource in resources)
      dataSize = checked(dataSize + _Align4(resource.Data.Length));

    var section = new byte[checked(dataStart + dataSize)];
    BinaryPrimitives.WriteUInt16LittleEndian(section.AsSpan(14), (ushort)groups.Length);

    var topEntryOffset = 16;
    var secondLevelOffset = topLevelSize;
    var thirdLevelOffset = checked(topLevelSize + secondLevelSize);
    var dataEntryOffset = checked(thirdLevelOffset + thirdLevelSize);
    var resourceDataOffset = dataStart;

    foreach (var group in groups) {
      BinaryPrimitives.WriteUInt32LittleEndian(section.AsSpan(topEntryOffset), checked((uint)group.TypeId));
      BinaryPrimitives.WriteUInt32LittleEndian(
        section.AsSpan(topEntryOffset + 4),
        _DIRECTORY_FLAG | checked((uint)secondLevelOffset)
      );
      topEntryOffset += 8;

      BinaryPrimitives.WriteUInt16LittleEndian(section.AsSpan(secondLevelOffset + 14), (ushort)group.Resources.Length);
      var secondEntryOffset = secondLevelOffset + 16;

      foreach (var resource in group.Resources) {
        BinaryPrimitives.WriteUInt32LittleEndian(section.AsSpan(secondEntryOffset), checked((uint)resource.ResourceId));
        BinaryPrimitives.WriteUInt32LittleEndian(
          section.AsSpan(secondEntryOffset + 4),
          _DIRECTORY_FLAG | checked((uint)thirdLevelOffset)
        );
        secondEntryOffset += 8;

        BinaryPrimitives.WriteUInt16LittleEndian(section.AsSpan(thirdLevelOffset + 14), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(section.AsSpan(thirdLevelOffset + 16), 0);
        BinaryPrimitives.WriteUInt32LittleEndian(section.AsSpan(thirdLevelOffset + 20), checked((uint)dataEntryOffset));
        thirdLevelOffset += 24;

        BinaryPrimitives.WriteUInt32LittleEndian(
          section.AsSpan(dataEntryOffset),
          checked(resourceRva + (uint)resourceDataOffset)
        );
        BinaryPrimitives.WriteUInt32LittleEndian(section.AsSpan(dataEntryOffset + 4), checked((uint)resource.Data.Length));
        dataEntryOffset += 16;

        resource.Data.CopyTo(section.AsSpan(resourceDataOffset));
        resourceDataOffset = checked(resourceDataOffset + _Align4(resource.Data.Length));
      }

      secondLevelOffset = checked(secondLevelOffset + 16 + group.Resources.Length * 8);
    }

    return section;
  }

  private static byte[] _BuildPe(byte[] resourceSection, uint resourceRva, PeResourceModuleKind kind) {
    var isExecutable = kind == PeResourceModuleKind.Executable;
    var sectionCount = isExecutable ? 2 : 1;
    var headersSize = _Align(
      checked(_DOS_HEADER_SIZE + 4 + _COFF_HEADER_SIZE + _OPTIONAL_HEADER_SIZE + sectionCount * _SECTION_HEADER_SIZE),
      _FILE_ALIGNMENT
    );

    var textRawSize = isExecutable ? _FILE_ALIGNMENT : 0;
    var textRawOffset = headersSize;
    var resourceRawOffset = checked(headersSize + textRawSize);
    var resourceRawSize = _Align(Math.Max(resourceSection.Length, 1), _FILE_ALIGNMENT);
    var file = new byte[checked(resourceRawOffset + resourceRawSize)];

    file[0] = (byte)'M';
    file[1] = (byte)'Z';
    BinaryPrimitives.WriteInt32LittleEndian(file.AsSpan(60), _DOS_HEADER_SIZE);

    file[_DOS_HEADER_SIZE] = (byte)'P';
    file[_DOS_HEADER_SIZE + 1] = (byte)'E';

    var coffOffset = _DOS_HEADER_SIZE + 4;
    BinaryPrimitives.WriteUInt16LittleEndian(file.AsSpan(coffOffset), _IMAGE_FILE_MACHINE_I386);
    BinaryPrimitives.WriteUInt16LittleEndian(file.AsSpan(coffOffset + 2), (ushort)sectionCount);
    BinaryPrimitives.WriteUInt16LittleEndian(file.AsSpan(coffOffset + 16), _OPTIONAL_HEADER_SIZE);

    var characteristics = (ushort)(_IMAGE_FILE_EXECUTABLE_IMAGE | _IMAGE_FILE_32BIT_MACHINE);
    if (!isExecutable)
      characteristics |= _IMAGE_FILE_DLL;
    BinaryPrimitives.WriteUInt16LittleEndian(file.AsSpan(coffOffset + 18), characteristics);

    var optionalOffset = coffOffset + _COFF_HEADER_SIZE;
    BinaryPrimitives.WriteUInt16LittleEndian(file.AsSpan(optionalOffset), 0x10B);
    BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(optionalOffset + 4), (uint)textRawSize);
    BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(optionalOffset + 8), checked((uint)resourceRawSize));
    BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(optionalOffset + 16), isExecutable ? 0x1000u : 0u);
    BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(optionalOffset + 20), isExecutable ? 0x1000u : 0u);
    BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(optionalOffset + 24), resourceRva);
    BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(optionalOffset + 28), isExecutable ? 0x00400000u : 0x10000000u);
    BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(optionalOffset + 32), _SECTION_ALIGNMENT);
    BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(optionalOffset + 36), _FILE_ALIGNMENT);
    BinaryPrimitives.WriteUInt16LittleEndian(file.AsSpan(optionalOffset + 40), 6);
    BinaryPrimitives.WriteUInt16LittleEndian(file.AsSpan(optionalOffset + 48), 6);

    var lastSectionEnd = checked(resourceRva + (uint)Math.Max(resourceSection.Length, 1));
    BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(optionalOffset + 56), (uint)_Align((int)lastSectionEnd, _SECTION_ALIGNMENT));
    BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(optionalOffset + 60), checked((uint)headersSize));
    BinaryPrimitives.WriteUInt16LittleEndian(file.AsSpan(optionalOffset + 68), 3);
    BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(optionalOffset + 72), 0x00100000u);
    BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(optionalOffset + 76), 0x00001000u);
    BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(optionalOffset + 80), 0x00100000u);
    BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(optionalOffset + 84), 0x00001000u);
    BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(optionalOffset + 92), 16);

    var resourceDirectoryOffset = optionalOffset + 96 + 2 * 8;
    BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(resourceDirectoryOffset), resourceRva);
    BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(resourceDirectoryOffset + 4), checked((uint)resourceSection.Length));

    var sectionOffset = optionalOffset + _OPTIONAL_HEADER_SIZE;
    if (isExecutable) {
      _WriteSectionHeader(
        file.AsSpan(sectionOffset),
        ".text",
        virtualSize: 3,
        virtualAddress: 0x1000,
        rawSize: _FILE_ALIGNMENT,
        rawOffset: textRawOffset,
        characteristics: _IMAGE_SCN_CNT_CODE | _IMAGE_SCN_MEM_EXECUTE | _IMAGE_SCN_MEM_READ
      );
      file[textRawOffset] = 0x31;     // xor eax,eax
      file[textRawOffset + 1] = 0xC0;
      file[textRawOffset + 2] = 0xC3; // ret
      sectionOffset += _SECTION_HEADER_SIZE;
    }

    _WriteSectionHeader(
      file.AsSpan(sectionOffset),
      ".rsrc",
      virtualSize: resourceSection.Length,
      virtualAddress: checked((int)resourceRva),
      rawSize: resourceRawSize,
      rawOffset: resourceRawOffset,
      characteristics: _IMAGE_SCN_CNT_INITIALIZED_DATA | _IMAGE_SCN_MEM_READ
    );

    resourceSection.CopyTo(file.AsSpan(resourceRawOffset));
    return file;
  }

  private static void _WriteSectionHeader(
    Span<byte> destination,
    string name,
    int virtualSize,
    int virtualAddress,
    int rawSize,
    int rawOffset,
    uint characteristics
  ) {
    for (var i = 0; i < name.Length && i < 8; ++i)
      destination[i] = (byte)name[i];

    BinaryPrimitives.WriteUInt32LittleEndian(destination[8..], checked((uint)virtualSize));
    BinaryPrimitives.WriteUInt32LittleEndian(destination[12..], checked((uint)virtualAddress));
    BinaryPrimitives.WriteUInt32LittleEndian(destination[16..], checked((uint)rawSize));
    BinaryPrimitives.WriteUInt32LittleEndian(destination[20..], checked((uint)rawOffset));
    BinaryPrimitives.WriteUInt32LittleEndian(destination[36..], characteristics);
  }

  private static int _Align4(int value) => _Align(value, 4);

  private static int _Align(int value, int alignment) {
    if (value < 0)
      throw new OverflowException();
    return checked((value + alignment - 1) / alignment * alignment);
  }
}
