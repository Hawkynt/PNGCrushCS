using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using FileFormat.Core;
using FileFormat.Ico;

namespace FileFormat.WindowsPe;

/// <summary>Managed editor for resources already present in a PE image.</summary>
/// <remarks>
/// The resource directory itself is left intact. A replacement that fits in the old leaf overwrites
/// only that leaf; a larger replacement is appended to the resource section and the corresponding
/// IMAGE_RESOURCE_DATA_ENTRY is redirected to it. If the section has to grow on disk, later raw data
/// is shifted and every PE field that contains a file offset is adjusted. RVAs outside the resource
/// section never move.
/// </remarks>
internal static class PeResourceEditor {

  private const uint _HighBit = 0x80000000u;
  private const int _SectionHeaderSize = 40;
  private const int _ResourceDirectorySize = 16;
  private const int _ResourceDirectoryEntrySize = 8;
  private const int _ResourceDataEntrySize = 16;
  private const int _DebugDirectoryEntrySize = 28;

  private readonly record struct ResourceIdentifier(int? Id, string? Name);

  private readonly record struct Section(
    int HeaderOffset,
    uint VirtualSize,
    uint VirtualAddress,
    uint RawSize,
    uint RawOffset
  );

  private sealed class ResourceLeaf {
    public required ResourceIdentifier Type { get; init; }
    public required ResourceIdentifier Name { get; init; }
    public required ResourceIdentifier Language { get; init; }
    public required int DataEntryOffset { get; init; }
    public required int DataOffset { get; init; }
    public required int DataSize { get; init; }
    public required uint CodePage { get; init; }
  }

  private sealed class Layout {
    public required int CoffOffset { get; init; }
    public required int OptionalOffset { get; init; }
    public required int OptionalSize { get; init; }
    public required int DataDirectoryOffset { get; init; }
    public required uint DataDirectoryCount { get; init; }
    public required int ResourceDirectoryEntryOffset { get; init; }
    public required uint ResourceRva { get; init; }
    public required uint ResourceSize { get; init; }
    public required int ResourceSectionIndex { get; init; }
    public required uint FileAlignment { get; init; }
    public required uint SectionAlignment { get; init; }
    public required Section[] Sections { get; init; }
    public required List<ResourceLeaf> Resources { get; init; }
  }

  internal static IReadOnlyList<PeResourceInfo> GetResources(byte[] source) {
    ArgumentNullException.ThrowIfNull(source);
    var layout = _Parse(source);
    return layout.Resources.Select(static resource => new PeResourceInfo {
      TypeId = resource.Type.Id,
      TypeName = resource.Type.Name,
      ResourceId = resource.Name.Id,
      ResourceName = resource.Name.Name,
      LanguageId = resource.Language.Id,
      LanguageName = resource.Language.Name,
      Size = resource.DataSize,
    }).ToArray();
  }

  internal static byte[] ReplaceResource(
    byte[] source,
    int typeId,
    int resourceId,
    int? languageId,
    ReadOnlySpan<byte> replacement
  ) {
    ArgumentNullException.ThrowIfNull(source);
    if (typeId < 0)
      throw new ArgumentOutOfRangeException(nameof(typeId));
    if (resourceId < 0)
      throw new ArgumentOutOfRangeException(nameof(resourceId));
    if (languageId < 0)
      throw new ArgumentOutOfRangeException(nameof(languageId));

    var layout = _Parse(source);
    var leaf = _FindNumeric(layout, typeId, resourceId, languageId);
    return _ReplaceLeaf(source, layout, leaf, replacement);
  }

  internal static byte[] ReplaceGroupImage(
    byte[] source,
    bool isCursor,
    int groupId,
    int? languageId,
    int? groupImageIndex,
    RawImage image
  ) {
    ArgumentNullException.ThrowIfNull(source);
    ArgumentNullException.ThrowIfNull(image);

    var groupType = isCursor ? 12 : 14;
    var componentType = isCursor ? 1 : 3;
    var layout = _Parse(source);
    var group = _FindNumeric(layout, groupType, groupId, languageId);
    var groupData = source.AsSpan(group.DataOffset, group.DataSize);

    if (groupData.Length < 6)
      throw new InvalidDataException($"PE resource {groupType}/{groupId} has a truncated group directory.");

    var expectedType = isCursor ? 2 : 1;
    var actualType = BinaryPrimitives.ReadUInt16LittleEndian(groupData[2..]);
    var count = BinaryPrimitives.ReadUInt16LittleEndian(groupData[4..]);
    if (BinaryPrimitives.ReadUInt16LittleEndian(groupData) != 0 || actualType != expectedType || count == 0)
      throw new InvalidDataException($"PE resource {groupType}/{groupId} is not a valid {(isCursor ? "cursor" : "icon")} group.");

    var index = groupImageIndex ?? (count == 1
      ? 0
      : throw new InvalidOperationException(
        $"PE {(isCursor ? "cursor" : "icon")} group {groupId} contains {count} images; specify the group image index to replace."));
    if ((uint)index >= count)
      throw new ArgumentOutOfRangeException(nameof(groupImageIndex));

    var groupEntryOffset = checked(6 + index * 14);
    if (groupEntryOffset > groupData.Length - 14)
      throw new InvalidDataException($"PE resource {groupType}/{groupId} has a truncated group directory.");

    var componentId = BinaryPrimitives.ReadUInt16LittleEndian(groupData[(groupEntryOffset + 12)..]);
    var componentLanguage = group.Language.Id;
    var component = _FindNumeric(layout, componentType, componentId, componentLanguage);
    var replacementImage = IcoDib.FromRawImage(image);

    byte[] replacementPayload;
    if (isCursor) {
      if (component.DataSize < 4)
        throw new InvalidDataException($"RT_CURSOR resource {componentId} is too short to contain a hotspot.");

      replacementPayload = new byte[checked(4 + replacementImage.Data.Length)];
      source.AsSpan(component.DataOffset, 4).CopyTo(replacementPayload);
      replacementImage.Data.CopyTo(replacementPayload.AsSpan(4));
    } else
      replacementPayload = replacementImage.Data[..];

    var updated = _ReplaceLeaf(source, layout, component, replacementPayload);

    // The first edit may have grown .rsrc, so locate the group again before touching its entry.
    layout = _Parse(updated);
    group = _FindNumeric(layout, groupType, groupId, languageId);
    var updatedGroupData = updated.AsSpan(group.DataOffset, group.DataSize).ToArray();
    groupEntryOffset = checked(6 + index * 14);

    if (isCursor) {
      BinaryPrimitives.WriteUInt16LittleEndian(updatedGroupData.AsSpan(groupEntryOffset), checked((ushort)replacementImage.Width));
      BinaryPrimitives.WriteUInt16LittleEndian(updatedGroupData.AsSpan(groupEntryOffset + 2), checked((ushort)replacementImage.Height));
      BinaryPrimitives.WriteUInt16LittleEndian(updatedGroupData.AsSpan(groupEntryOffset + 4), 1);
      BinaryPrimitives.WriteUInt16LittleEndian(updatedGroupData.AsSpan(groupEntryOffset + 6), checked((ushort)replacementImage.BitsPerPixel));
      BinaryPrimitives.WriteUInt32LittleEndian(updatedGroupData.AsSpan(groupEntryOffset + 8), checked((uint)replacementPayload.Length));
    } else {
      updatedGroupData[groupEntryOffset] = replacementImage.Width == 256 ? (byte)0 : checked((byte)replacementImage.Width);
      updatedGroupData[groupEntryOffset + 1] = replacementImage.Height == 256 ? (byte)0 : checked((byte)replacementImage.Height);
      updatedGroupData[groupEntryOffset + 2] = 0;
      updatedGroupData[groupEntryOffset + 3] = 0;
      BinaryPrimitives.WriteUInt16LittleEndian(updatedGroupData.AsSpan(groupEntryOffset + 4), 1);
      BinaryPrimitives.WriteUInt16LittleEndian(updatedGroupData.AsSpan(groupEntryOffset + 6), checked((ushort)replacementImage.BitsPerPixel));
      BinaryPrimitives.WriteUInt32LittleEndian(updatedGroupData.AsSpan(groupEntryOffset + 8), checked((uint)replacementPayload.Length));
    }

    return _ReplaceLeaf(updated, layout, group, updatedGroupData);
  }

  private static ResourceLeaf _FindNumeric(Layout layout, int typeId, int resourceId, int? languageId) {
    var matches = layout.Resources.Where(resource =>
      resource.Type.Id == typeId
      && resource.Name.Id == resourceId
      && (!languageId.HasValue || resource.Language.Id == languageId.Value)).ToArray();

    if (matches.Length == 0)
      throw new KeyNotFoundException(
        languageId.HasValue
          ? $"PE resource type {typeId}, ID {resourceId}, language {languageId.Value} was not found."
          : $"PE resource type {typeId}, ID {resourceId} was not found.");

    if (matches.Length > 1) {
      var languages = string.Join(", ", matches.Select(static resource => resource.Language.Id?.ToString() ?? resource.Language.Name ?? "<named>"));
      throw new InvalidOperationException(
        $"PE resource type {typeId}, ID {resourceId} has multiple language variants ({languages}); specify the language ID.");
    }

    return matches[0];
  }

  private static byte[] _ReplaceLeaf(byte[] source, Layout layout, ResourceLeaf leaf, ReadOnlySpan<byte> replacement) {
    if (leaf.DataEntryOffset < 0 || leaf.DataEntryOffset > source.Length - _ResourceDataEntrySize)
      throw new InvalidDataException("PE resource data entry points outside the file.");
    if (leaf.DataOffset < 0 || leaf.DataSize < 0 || leaf.DataOffset > source.Length - leaf.DataSize)
      throw new InvalidDataException("PE resource payload points outside the file.");

    if (replacement.Length <= leaf.DataSize) {
      var result = source[..];
      replacement.CopyTo(result.AsSpan(leaf.DataOffset));
      result.AsSpan(leaf.DataOffset + replacement.Length, leaf.DataSize - replacement.Length).Clear();
      BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(leaf.DataEntryOffset + 4), checked((uint)replacement.Length));
      _UpdateChecksum(result, layout.OptionalOffset + 64);
      return result;
    }

    var section = layout.Sections[layout.ResourceSectionIndex];
    if (section.RawOffset > int.MaxValue || section.RawSize > int.MaxValue)
      throw new InvalidDataException("The PE resource section is too large to edit in memory.");

    var rawOffset = (int)section.RawOffset;
    var oldRawSize = (int)section.RawSize;
    if (rawOffset < 0 || oldRawSize < 0 || rawOffset > source.Length - oldRawSize)
      throw new InvalidDataException("The PE resource section points outside the file.");

    var appendWithinSection = oldRawSize;
    var requiredWithinSection = checked(appendWithinSection + replacement.Length);
    var newRawSize = _Align(requiredWithinSection, layout.FileAlignment);
    var delta = checked(newRawSize - oldRawSize);
    var newVirtualSize = Math.Max((ulong)section.VirtualSize, (ulong)requiredWithinSection);
    if (newVirtualSize > uint.MaxValue)
      throw new InvalidDataException("The replacement would make the PE resource section exceed 4 GiB.");

    var nextVirtualAddress = layout.Sections
      .Where(candidate => candidate.VirtualAddress > section.VirtualAddress)
      .Select(static candidate => candidate.VirtualAddress)
      .DefaultIfEmpty(uint.MaxValue)
      .Min();
    if (nextVirtualAddress != uint.MaxValue
        && (ulong)section.VirtualAddress + newVirtualSize > nextVirtualAddress)
      throw new InvalidDataException(
        "The replacement does not fit before the next PE section in virtual address space; the executable cannot be safely edited without relocating sections.");

    var insertionOffset = checked(rawOffset + oldRawSize);
    var resultWithGrowth = new byte[checked(source.Length + delta)];
    source.AsSpan(0, insertionOffset).CopyTo(resultWithGrowth);
    source.AsSpan(insertionOffset).CopyTo(resultWithGrowth.AsSpan(insertionOffset + delta));

    _ShiftFileOffsets(resultWithGrowth, layout, insertionOffset, delta);

    var resourceSectionHeader = section.HeaderOffset;
    BinaryPrimitives.WriteUInt32LittleEndian(resultWithGrowth.AsSpan(resourceSectionHeader + 8), checked((uint)newVirtualSize));
    BinaryPrimitives.WriteUInt32LittleEndian(resultWithGrowth.AsSpan(resourceSectionHeader + 16), checked((uint)newRawSize));

    var newDataOffset = checked(rawOffset + appendWithinSection);
    replacement.CopyTo(resultWithGrowth.AsSpan(newDataOffset));
    var newDataRva = checked(section.VirtualAddress + (uint)appendWithinSection);
    BinaryPrimitives.WriteUInt32LittleEndian(resultWithGrowth.AsSpan(leaf.DataEntryOffset), newDataRva);
    BinaryPrimitives.WriteUInt32LittleEndian(resultWithGrowth.AsSpan(leaf.DataEntryOffset + 4), checked((uint)replacement.Length));

    var resourceExtent = checked((ulong)newDataRva + (uint)replacement.Length - layout.ResourceRva);
    if (resourceExtent > layout.ResourceSize) {
      if (resourceExtent > uint.MaxValue)
        throw new InvalidDataException("The replacement makes the PE resource directory exceed 4 GiB.");
      BinaryPrimitives.WriteUInt32LittleEndian(
        resultWithGrowth.AsSpan(layout.ResourceDirectoryEntryOffset + 4),
        (uint)resourceExtent
      );
    }

    var initializedData = BinaryPrimitives.ReadUInt32LittleEndian(resultWithGrowth.AsSpan(layout.OptionalOffset + 8));
    BinaryPrimitives.WriteUInt32LittleEndian(
      resultWithGrowth.AsSpan(layout.OptionalOffset + 8),
      checked(initializedData + (uint)delta)
    );

    _UpdateSizeOfImage(resultWithGrowth, layout);
    _UpdateChecksum(resultWithGrowth, layout.OptionalOffset + 64);
    return resultWithGrowth;
  }

  private static void _ShiftFileOffsets(byte[] result, Layout layout, int insertionOffset, int delta) {
    static uint Shift(uint value, int insertion, int amount)
      => value != 0 && value >= insertion ? checked(value + (uint)amount) : value;

    var symbolTable = BinaryPrimitives.ReadUInt32LittleEndian(result.AsSpan(layout.CoffOffset + 8));
    BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(layout.CoffOffset + 8), Shift(symbolTable, insertionOffset, delta));

    foreach (var section in layout.Sections) {
      var raw = BinaryPrimitives.ReadUInt32LittleEndian(result.AsSpan(section.HeaderOffset + 20));
      var relocations = BinaryPrimitives.ReadUInt32LittleEndian(result.AsSpan(section.HeaderOffset + 24));
      var lineNumbers = BinaryPrimitives.ReadUInt32LittleEndian(result.AsSpan(section.HeaderOffset + 28));
      BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(section.HeaderOffset + 20), Shift(raw, insertionOffset, delta));
      BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(section.HeaderOffset + 24), Shift(relocations, insertionOffset, delta));
      BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(section.HeaderOffset + 28), Shift(lineNumbers, insertionOffset, delta));
    }

    // IMAGE_DIRECTORY_ENTRY_SECURITY is exceptional: its first field is a file offset, not an RVA.
    if (layout.DataDirectoryCount > 4) {
      var securityOffset = layout.DataDirectoryOffset + 4 * 8;
      if (securityOffset <= layout.OptionalOffset + layout.OptionalSize - 8) {
        var certificate = BinaryPrimitives.ReadUInt32LittleEndian(result.AsSpan(securityOffset));
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(securityOffset), Shift(certificate, insertionOffset, delta));
      }
    }

    // IMAGE_DEBUG_DIRECTORY carries both an RVA and a raw-file pointer. Only the latter moves.
    if (layout.DataDirectoryCount <= 6)
      return;

    var debugDirectoryEntry = layout.DataDirectoryOffset + 6 * 8;
    if (debugDirectoryEntry > layout.OptionalOffset + layout.OptionalSize - 8)
      return;

    var debugRva = BinaryPrimitives.ReadUInt32LittleEndian(result.AsSpan(debugDirectoryEntry));
    var debugSize = BinaryPrimitives.ReadUInt32LittleEndian(result.AsSpan(debugDirectoryEntry + 4));
    if (debugRva == 0 || debugSize < _DebugDirectoryEntrySize)
      return;

    var originalDebugOffset = _MapRva(layout.Sections, debugRva, checked((int)debugSize));
    if (originalDebugOffset < 0)
      return;
    var debugOffset = originalDebugOffset >= insertionOffset ? checked(originalDebugOffset + delta) : originalDebugOffset;
    var entryCount = debugSize / _DebugDirectoryEntrySize;
    for (var i = 0u; i < entryCount; ++i) {
      var entryOffset = checked(debugOffset + (int)i * _DebugDirectoryEntrySize);
      if (entryOffset > result.Length - _DebugDirectoryEntrySize)
        break;
      var pointer = BinaryPrimitives.ReadUInt32LittleEndian(result.AsSpan(entryOffset + 24));
      BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(entryOffset + 24), Shift(pointer, insertionOffset, delta));
    }
  }

  private static void _UpdateSizeOfImage(byte[] result, Layout layout) {
    ulong maximum = 0;
    foreach (var section in layout.Sections) {
      var virtualSize = BinaryPrimitives.ReadUInt32LittleEndian(result.AsSpan(section.HeaderOffset + 8));
      var virtualAddress = BinaryPrimitives.ReadUInt32LittleEndian(result.AsSpan(section.HeaderOffset + 12));
      maximum = Math.Max(maximum, (ulong)virtualAddress + virtualSize);
    }

    if (maximum > int.MaxValue)
      throw new InvalidDataException("The PE image is too large to describe with the current managed editor.");
    BinaryPrimitives.WriteUInt32LittleEndian(
      result.AsSpan(layout.OptionalOffset + 56),
      checked((uint)_Align((int)maximum, layout.SectionAlignment))
    );
  }

  private static Layout _Parse(byte[] source) {
    if (source.Length < 64 || source[0] != (byte)'M' || source[1] != (byte)'Z')
      throw new InvalidDataException("Not a PE image: the MZ header is missing.");

    var peOffset = BinaryPrimitives.ReadInt32LittleEndian(source.AsSpan(60));
    if (peOffset < 0 || peOffset > source.Length - 24
        || source[peOffset] != (byte)'P' || source[peOffset + 1] != (byte)'E'
        || source[peOffset + 2] != 0 || source[peOffset + 3] != 0)
      throw new InvalidDataException("Not a PE image: the PE header is missing or out of range.");

    var coffOffset = peOffset + 4;
    var sectionCount = BinaryPrimitives.ReadUInt16LittleEndian(source.AsSpan(coffOffset + 2));
    var optionalSize = BinaryPrimitives.ReadUInt16LittleEndian(source.AsSpan(coffOffset + 16));
    var optionalOffset = coffOffset + 20;
    if (optionalSize < 68 || optionalOffset > source.Length - optionalSize)
      throw new InvalidDataException("PE optional header is missing or truncated.");

    var magic = BinaryPrimitives.ReadUInt16LittleEndian(source.AsSpan(optionalOffset));
    var dataDirectoryOffset = magic switch {
      0x10B => optionalOffset + 96,
      0x20B => optionalOffset + 112,
      _ => throw new InvalidDataException($"Unknown PE optional-header magic 0x{magic:X4}."),
    };
    var countOffset = magic == 0x10B ? optionalOffset + 92 : optionalOffset + 108;
    if (countOffset > optionalOffset + optionalSize - 4)
      throw new InvalidDataException("PE optional header is too short for NumberOfRvaAndSizes.");

    var directoryCount = BinaryPrimitives.ReadUInt32LittleEndian(source.AsSpan(countOffset));
    var fileAlignment = BinaryPrimitives.ReadUInt32LittleEndian(source.AsSpan(optionalOffset + 36));
    var sectionAlignment = BinaryPrimitives.ReadUInt32LittleEndian(source.AsSpan(optionalOffset + 32));
    if (fileAlignment == 0 || sectionAlignment == 0)
      throw new InvalidDataException("PE section/file alignment must be non-zero.");

    var sectionTableOffset = optionalOffset + optionalSize;
    if ((long)sectionTableOffset + sectionCount * _SectionHeaderSize > source.Length)
      throw new InvalidDataException("PE section table is truncated.");

    var sections = new Section[sectionCount];
    for (var i = 0; i < sectionCount; ++i) {
      var header = sectionTableOffset + i * _SectionHeaderSize;
      sections[i] = new Section(
        header,
        BinaryPrimitives.ReadUInt32LittleEndian(source.AsSpan(header + 8)),
        BinaryPrimitives.ReadUInt32LittleEndian(source.AsSpan(header + 12)),
        BinaryPrimitives.ReadUInt32LittleEndian(source.AsSpan(header + 16)),
        BinaryPrimitives.ReadUInt32LittleEndian(source.AsSpan(header + 20))
      );
    }

    if (directoryCount <= 2 || dataDirectoryOffset > optionalOffset + optionalSize - 24)
      throw new InvalidOperationException("The PE image contains no resource directory.");

    var resourceDirectoryEntryOffset = dataDirectoryOffset + 2 * 8;
    var resourceRva = BinaryPrimitives.ReadUInt32LittleEndian(source.AsSpan(resourceDirectoryEntryOffset));
    var resourceSize = BinaryPrimitives.ReadUInt32LittleEndian(source.AsSpan(resourceDirectoryEntryOffset + 4));
    if (resourceRva == 0 || resourceSize == 0)
      throw new InvalidOperationException("The PE image contains no resource directory.");

    var resourceSectionIndex = _FindSectionIndexForRva(sections, resourceRva);
    if (resourceSectionIndex < 0)
      throw new InvalidDataException("The PE resource directory RVA is not covered by a section.");

    var resourceBaseOffset = _MapRva(sections, resourceRva, 16);
    if (resourceBaseOffset < 0)
      throw new InvalidDataException("The PE resource directory does not map to file data.");

    var resources = new List<ResourceLeaf>();
    var visited = new HashSet<(int Level, uint Offset)>();
    _ReadDirectory(source, sections, resourceBaseOffset, 0, 0, default, default, resources, visited);

    return new Layout {
      CoffOffset = coffOffset,
      OptionalOffset = optionalOffset,
      OptionalSize = optionalSize,
      DataDirectoryOffset = dataDirectoryOffset,
      DataDirectoryCount = directoryCount,
      ResourceDirectoryEntryOffset = resourceDirectoryEntryOffset,
      ResourceRva = resourceRva,
      ResourceSize = resourceSize,
      ResourceSectionIndex = resourceSectionIndex,
      FileAlignment = fileAlignment,
      SectionAlignment = sectionAlignment,
      Sections = sections,
      Resources = resources,
    };
  }

  private static void _ReadDirectory(
    byte[] source,
    Section[] sections,
    int resourceBaseOffset,
    uint relativeOffset,
    int level,
    ResourceIdentifier type,
    ResourceIdentifier name,
    List<ResourceLeaf> resources,
    HashSet<(int Level, uint Offset)> visited
  ) {
    if (level > 2 || !visited.Add((level, relativeOffset)) || relativeOffset > int.MaxValue)
      return;

    var directoryOffset = (long)resourceBaseOffset + relativeOffset;
    if (directoryOffset < 0 || directoryOffset > source.Length - _ResourceDirectorySize)
      return;

    var absoluteDirectoryOffset = (int)directoryOffset;
    var namedCount = BinaryPrimitives.ReadUInt16LittleEndian(source.AsSpan(absoluteDirectoryOffset + 12));
    var idCount = BinaryPrimitives.ReadUInt16LittleEndian(source.AsSpan(absoluteDirectoryOffset + 14));
    var entryCount = namedCount + idCount;
    var entriesOffset = absoluteDirectoryOffset + _ResourceDirectorySize;
    if ((long)entriesOffset + entryCount * _ResourceDirectoryEntrySize > source.Length)
      return;

    for (var i = 0; i < entryCount; ++i) {
      var entryOffset = entriesOffset + i * _ResourceDirectoryEntrySize;
      var nameOrId = BinaryPrimitives.ReadUInt32LittleEndian(source.AsSpan(entryOffset));
      var child = BinaryPrimitives.ReadUInt32LittleEndian(source.AsSpan(entryOffset + 4));
      var identifier = _ReadIdentifier(source, resourceBaseOffset, nameOrId);

      if (level < 2) {
        if ((child & _HighBit) == 0)
          continue;
        var childOffset = child & ~_HighBit;
        if (level == 0)
          _ReadDirectory(source, sections, resourceBaseOffset, childOffset, 1, identifier, default, resources, visited);
        else
          _ReadDirectory(source, sections, resourceBaseOffset, childOffset, 2, type, identifier, resources, visited);
        continue;
      }

      if ((child & _HighBit) != 0)
        continue;
      var dataEntryRelative = child;
      if (dataEntryRelative > int.MaxValue)
        continue;
      var dataEntryLong = (long)resourceBaseOffset + dataEntryRelative;
      if (dataEntryLong < 0 || dataEntryLong > source.Length - _ResourceDataEntrySize)
        continue;
      var dataEntryOffset = (int)dataEntryLong;
      var dataRva = BinaryPrimitives.ReadUInt32LittleEndian(source.AsSpan(dataEntryOffset));
      var dataSize = BinaryPrimitives.ReadUInt32LittleEndian(source.AsSpan(dataEntryOffset + 4));
      var codePage = BinaryPrimitives.ReadUInt32LittleEndian(source.AsSpan(dataEntryOffset + 8));
      if (dataSize > int.MaxValue)
        continue;
      var dataOffset = _MapRva(sections, dataRva, (int)dataSize);
      if (dataOffset < 0)
        continue;

      resources.Add(new ResourceLeaf {
        Type = type,
        Name = name,
        Language = identifier,
        DataEntryOffset = dataEntryOffset,
        DataOffset = dataOffset,
        DataSize = (int)dataSize,
        CodePage = codePage,
      });
    }
  }

  private static ResourceIdentifier _ReadIdentifier(byte[] source, int resourceBaseOffset, uint value) {
    if ((value & _HighBit) == 0)
      return new ResourceIdentifier(checked((int)value), null);

    var relativeOffset = value & ~_HighBit;
    if (relativeOffset > int.MaxValue)
      return new ResourceIdentifier(null, null);
    var stringOffset = (long)resourceBaseOffset + relativeOffset;
    if (stringOffset < 0 || stringOffset > source.Length - 2)
      return new ResourceIdentifier(null, null);

    var absoluteOffset = (int)stringOffset;
    var length = BinaryPrimitives.ReadUInt16LittleEndian(source.AsSpan(absoluteOffset));
    var byteLength = checked(length * 2);
    if (absoluteOffset + 2 > source.Length - byteLength)
      return new ResourceIdentifier(null, null);

    return new ResourceIdentifier(null, Encoding.Unicode.GetString(source.AsSpan(absoluteOffset + 2, byteLength)));
  }

  private static int _FindSectionIndexForRva(Section[] sections, uint rva) {
    for (var i = 0; i < sections.Length; ++i) {
      var section = sections[i];
      var span = Math.Max(section.VirtualSize, section.RawSize);
      if (rva >= section.VirtualAddress && (ulong)(rva - section.VirtualAddress) < span)
        return i;
    }
    return -1;
  }

  private static int _MapRva(Section[] sections, uint rva, int size) {
    if (size < 0)
      return -1;
    foreach (var section in sections) {
      if (rva < section.VirtualAddress)
        continue;
      var relative = (ulong)(rva - section.VirtualAddress);
      if (relative > section.RawSize || (ulong)size > section.RawSize - relative)
        continue;
      var offset = (ulong)section.RawOffset + relative;
      if (offset > int.MaxValue)
        return -1;
      return (int)offset;
    }
    return -1;
  }

  private static int _Align(int value, uint alignment) {
    if (value < 0 || alignment == 0 || alignment > int.MaxValue)
      throw new InvalidDataException("Invalid PE alignment.");
    var a = (int)alignment;
    return checked((value + a - 1) / a * a);
  }

  private static void _UpdateChecksum(byte[] data, int checksumOffset) {
    if (checksumOffset < 0 || checksumOffset > data.Length - 4)
      return;

    BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(checksumOffset), 0);
    ulong sum = 0;
    var i = 0;
    for (; i <= data.Length - 2; i += 2) {
      sum += BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(i));
      sum = (sum & 0xFFFF) + (sum >> 16);
    }
    if (i < data.Length)
      sum += data[i];
    sum = (sum & 0xFFFF) + (sum >> 16);
    sum += sum >> 16;
    sum = (sum & 0xFFFF) + (uint)data.Length;
    BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(checksumOffset), checked((uint)sum));
  }
}
