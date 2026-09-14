using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using FileFormat.Core;

namespace FileFormat.WindowsPe.Tests;

[TestFixture]
public sealed class PeResourceSafetyTests {

  private static readonly byte[] _PngPrefix = [0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A];

  [Test]
  public void Reader_EmbeddedImages_PreserveTypeNameAndLanguageSelectors() {
    var english = _PngPayload(0x11);
    var german = _PngPayload(0x22);
    var pe = _BuildPe(
      new ResourceFixture(10, 100, 0x0409, english),
      new ResourceFixture(10, 100, 0x0407, german)
    );

    var images = PeResourceReader.FromBytes(pe).ImageResources
      .Where(static image => image.ResourceType == PeImageResourceType.EmbeddedImage)
      .OrderBy(static image => image.LanguageId)
      .ToArray();

    Assert.Multiple(() => {
      Assert.That(images, Has.Length.EqualTo(2));
      Assert.That(images.Select(static image => image.ResourceTypeId), Is.All.EqualTo(10));
      Assert.That(images.Select(static image => image.ResourceId), Is.All.EqualTo(100));
      Assert.That(images.Select(static image => image.LanguageId), Is.EqualTo(new int?[] { 0x0407, 0x0409 }));
      Assert.That(images.Select(static image => image.ResourceTypeName), Is.All.Null);
      Assert.That(images.Select(static image => image.ResourceName), Is.All.Null);
      Assert.That(images[0].Data, Is.EqualTo(german));
      Assert.That(images[1].Data, Is.EqualTo(english));
    });
  }

  [Test]
  public void ReplaceResource_WithoutLanguage_RejectsAmbiguousLanguageVariants() {
    var pe = _BuildPe(
      new ResourceFixture(10, 100, 0x0409, _PngPayload(0x11)),
      new ResourceFixture(10, 100, 0x0407, _PngPayload(0x22))
    );
    var file = PeResourceFile.ReadEditable(pe);

    var error = Assert.Throws<InvalidOperationException>(() => file.ReplaceResource(10, 100, _PngPayload(0x33)));

    Assert.That(error!.Message, Does.Contain("multiple language variants"));
  }

  [Test]
  public void ReplaceImage_ByIndex_UsesRetainedEmbeddedImageLanguage() {
    var english = _PngPayload(0x11);
    var german = _PngPayload(0x22);
    var pe = _BuildPe(
      new ResourceFixture(10, 100, 0x0409, english),
      new ResourceFixture(10, 100, 0x0407, german)
    );
    var file = PeResourceFile.ReadEditable(pe);
    var germanIndex = file.ImageResources
      .Select((image, index) => (image, index))
      .Single(pair => pair.image.ResourceType == PeImageResourceType.EmbeddedImage && pair.image.LanguageId == 0x0407)
      .index;

    // The tiny synthetic PNG is intentionally not decodable, so exercise the retained selector through
    // raw replacement: the image-index metadata must still identify one language unambiguously.
    var replacement = _PngPayload(0x44);
    var edited = file.ReplaceResource(10, 100, 0x0407, replacement);
    var images = edited.ImageResources
      .Where(static image => image.ResourceType == PeImageResourceType.EmbeddedImage)
      .ToArray();

    Assert.Multiple(() => {
      Assert.That(file.ImageResources[germanIndex].LanguageId, Is.EqualTo(0x0407));
      Assert.That(images.Single(static image => image.LanguageId == 0x0407).Data, Is.EqualTo(replacement));
      Assert.That(images.Single(static image => image.LanguageId == 0x0409).Data, Is.EqualTo(english));
    });
  }

  [TestCase(false)]
  [TestCase(true)]
  public void ReplaceImage_SharedGroupComponent_IsRejected(bool isCursor) {
    var imageData = MinimalPeBuilder.CreateMinimalIconEntry(16, 16);
    var componentData = isCursor ? _CursorComponent(imageData, 3, 7) : imageData;
    var groupData = isCursor
      ? _CursorGroup(componentId: 1, width: 16, height: 16, componentData.Length)
      : _IconGroup(componentId: 1, width: 16, height: 16, componentData.Length);
    var componentType = isCursor ? 1 : 3;
    var groupType = isCursor ? 12 : 14;
    var pe = _BuildPe(
      new ResourceFixture(componentType, 1, 0x0409, componentData),
      new ResourceFixture(groupType, 10, 0x0409, groupData),
      new ResourceFixture(groupType, 20, 0x0409, groupData)
    );
    var file = PeResourceFile.ReadEditable(pe);
    var resourceType = isCursor ? PeImageResourceType.Cursor : PeImageResourceType.Icon;

    var error = Assert.Throws<InvalidOperationException>(() =>
      file.ReplaceImage(resourceType, 10, 0, _CreateImage(9, 11), 0x0409));

    Assert.That(error!.Message, Does.Contain("referenced by multiple"));
  }

  [Test]
  public void Reader_OversizedAssembledIco_ReturnsNoGroupInsteadOfOverflowing() {
    const ushort count = ushort.MaxValue;
    var component = new byte[32768];
    var group = new byte[6 + count * 14];
    BinaryPrimitives.WriteUInt16LittleEndian(group.AsSpan(2), 1);
    BinaryPrimitives.WriteUInt16LittleEndian(group.AsSpan(4), count);
    for (var i = 0; i < count; ++i) {
      var offset = 6 + i * 14;
      group[offset] = 16;
      group[offset + 1] = 16;
      BinaryPrimitives.WriteUInt16LittleEndian(group.AsSpan(offset + 4), 1);
      BinaryPrimitives.WriteUInt16LittleEndian(group.AsSpan(offset + 6), 32);
      BinaryPrimitives.WriteInt32LittleEndian(group.AsSpan(offset + 8), component.Length);
      BinaryPrimitives.WriteUInt16LittleEndian(group.AsSpan(offset + 12), 1);
    }

    var pe = _BuildPe(
      new ResourceFixture(3, 1, 0x0409, component),
      new ResourceFixture(14, 1, 0x0409, group)
    );

    PeResourceFile? file = null;
    Assert.DoesNotThrow(() => file = PeResourceReader.FromBytes(pe));
    Assert.Multiple(() => {
      Assert.That(file!.IconGroups, Is.Empty);
      Assert.That(file.ImageResources.Any(static image => image.ResourceType == PeImageResourceType.Icon), Is.False);
    });
  }

  [Test]
  public void DetectImageSignature_OverflowingRange_ReturnsNull()
    => Assert.That(PeResourceReader._DetectImageSignature(new byte[8], int.MaxValue, 8), Is.Null);

  private static byte[] _PngPayload(byte marker) => [.. _PngPrefix, marker];

  private static byte[] _CursorComponent(byte[] dib, ushort hotspotX, ushort hotspotY) {
    var result = new byte[4 + dib.Length];
    BinaryPrimitives.WriteUInt16LittleEndian(result, hotspotX);
    BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(2), hotspotY);
    dib.CopyTo(result.AsSpan(4));
    return result;
  }

  private static byte[] _IconGroup(ushort componentId, byte width, byte height, int bytesInResource) {
    var result = new byte[20];
    BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(2), 1);
    BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(4), 1);
    result[6] = width;
    result[7] = height;
    BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(10), 1);
    BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(12), 32);
    BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(14), bytesInResource);
    BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(18), componentId);
    return result;
  }

  private static byte[] _CursorGroup(ushort componentId, ushort width, ushort height, int bytesInResource) {
    var result = new byte[20];
    BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(2), 2);
    BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(4), 1);
    BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(6), width);
    BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(8), height);
    BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(10), 1);
    BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(12), 32);
    BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(14), bytesInResource);
    BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(18), componentId);
    return result;
  }

  private static RawImage _CreateImage(int width, int height) {
    var pixels = new byte[checked(width * height * 3)];
    for (var i = 0; i < pixels.Length; ++i)
      pixels[i] = (byte)(i * 29 + i / 7 * 13);
    return new RawImage {
      Width = width,
      Height = height,
      Format = PixelFormat.Rgb24,
      PixelData = pixels,
    };
  }

  private readonly record struct ResourceFixture(int TypeId, int ResourceId, int LanguageId, byte[] Data);

  private static byte[] _BuildPe(params ResourceFixture[] resources) {
    ArgumentNullException.ThrowIfNull(resources);
    if (resources.Length == 0)
      throw new ArgumentException("At least one resource is required.", nameof(resources));

    var types = resources
      .GroupBy(static resource => resource.TypeId)
      .OrderBy(static group => group.Key)
      .Select(type => new {
        TypeId = type.Key,
        Names = type
          .GroupBy(static resource => resource.ResourceId)
          .OrderBy(static group => group.Key)
          .Select(name => new {
            ResourceId = name.Key,
            Languages = name.OrderBy(static resource => resource.LanguageId).ToArray(),
          })
          .ToArray(),
      })
      .ToArray();

    var rootSize = 16 + types.Length * 8;
    var typeDirectoryOffsets = new Dictionary<int, int>();
    var nameDirectoryOffsets = new Dictionary<(int TypeId, int ResourceId), int>();
    var directoryCursor = rootSize;

    foreach (var type in types) {
      typeDirectoryOffsets[type.TypeId] = directoryCursor;
      directoryCursor += 16 + type.Names.Length * 8;
    }

    foreach (var type in types)
      foreach (var name in type.Names) {
        nameDirectoryOffsets[(type.TypeId, name.ResourceId)] = directoryCursor;
        directoryCursor += 16 + name.Languages.Length * 8;
      }

    var dataEntryOffset = directoryCursor;
    directoryCursor += resources.Length * 16;
    var dataOffset = _Align4(directoryCursor);
    var rawSize = dataOffset + resources.Sum(static resource => _Align4(resource.Data.Length));
    var section = new byte[rawSize];

    BinaryPrimitives.WriteUInt16LittleEndian(section.AsSpan(14), checked((ushort)types.Length));
    var rootEntryOffset = 16;
    foreach (var type in types) {
      BinaryPrimitives.WriteUInt32LittleEndian(section.AsSpan(rootEntryOffset), checked((uint)type.TypeId));
      BinaryPrimitives.WriteUInt32LittleEndian(section.AsSpan(rootEntryOffset + 4), 0x80000000u | checked((uint)typeDirectoryOffsets[type.TypeId]));
      rootEntryOffset += 8;

      var typeDirectory = typeDirectoryOffsets[type.TypeId];
      BinaryPrimitives.WriteUInt16LittleEndian(section.AsSpan(typeDirectory + 14), checked((ushort)type.Names.Length));
      var nameEntryOffset = typeDirectory + 16;
      foreach (var name in type.Names) {
        BinaryPrimitives.WriteUInt32LittleEndian(section.AsSpan(nameEntryOffset), checked((uint)name.ResourceId));
        BinaryPrimitives.WriteUInt32LittleEndian(section.AsSpan(nameEntryOffset + 4), 0x80000000u | checked((uint)nameDirectoryOffsets[(type.TypeId, name.ResourceId)]));
        nameEntryOffset += 8;
      }
    }

    var currentDataEntry = dataEntryOffset;
    var currentData = dataOffset;
    foreach (var type in types)
      foreach (var name in type.Names) {
        var languageDirectory = nameDirectoryOffsets[(type.TypeId, name.ResourceId)];
        BinaryPrimitives.WriteUInt16LittleEndian(section.AsSpan(languageDirectory + 14), checked((ushort)name.Languages.Length));
        var languageEntryOffset = languageDirectory + 16;

        foreach (var resource in name.Languages) {
          BinaryPrimitives.WriteUInt32LittleEndian(section.AsSpan(languageEntryOffset), checked((uint)resource.LanguageId));
          BinaryPrimitives.WriteUInt32LittleEndian(section.AsSpan(languageEntryOffset + 4), checked((uint)currentDataEntry));
          languageEntryOffset += 8;

          BinaryPrimitives.WriteUInt32LittleEndian(section.AsSpan(currentDataEntry), checked((uint)(0x1000 + currentData)));
          BinaryPrimitives.WriteUInt32LittleEndian(section.AsSpan(currentDataEntry + 4), checked((uint)resource.Data.Length));
          currentDataEntry += 16;

          resource.Data.CopyTo(section.AsSpan(currentData));
          currentData += _Align4(resource.Data.Length);
        }
      }

    const int peOffset = 0x80;
    const int optionalHeaderSize = 0xE0;
    const int fileAlignment = 0x200;
    const int sectionAlignment = 0x1000;
    const int resourceRva = 0x1000;
    const int resourceFileOffset = 0x200;
    var alignedRawSize = (rawSize + fileAlignment - 1) & ~(fileAlignment - 1);
    var pe = new byte[resourceFileOffset + alignedRawSize];

    pe[0] = (byte)'M';
    pe[1] = (byte)'Z';
    BinaryPrimitives.WriteInt32LittleEndian(pe.AsSpan(60), peOffset);
    pe[peOffset] = (byte)'P';
    pe[peOffset + 1] = (byte)'E';

    var coffOffset = peOffset + 4;
    BinaryPrimitives.WriteUInt16LittleEndian(pe.AsSpan(coffOffset), 0x014C);
    BinaryPrimitives.WriteUInt16LittleEndian(pe.AsSpan(coffOffset + 2), 1);
    BinaryPrimitives.WriteUInt16LittleEndian(pe.AsSpan(coffOffset + 16), optionalHeaderSize);
    BinaryPrimitives.WriteUInt16LittleEndian(pe.AsSpan(coffOffset + 18), 0x2102);

    var optionalOffset = coffOffset + 20;
    BinaryPrimitives.WriteUInt16LittleEndian(pe.AsSpan(optionalOffset), 0x10B);
    BinaryPrimitives.WriteUInt32LittleEndian(pe.AsSpan(optionalOffset + 32), sectionAlignment);
    BinaryPrimitives.WriteUInt32LittleEndian(pe.AsSpan(optionalOffset + 36), fileAlignment);
    BinaryPrimitives.WriteUInt32LittleEndian(pe.AsSpan(optionalOffset + 56), 0x2000);
    BinaryPrimitives.WriteUInt32LittleEndian(pe.AsSpan(optionalOffset + 60), resourceFileOffset);
    BinaryPrimitives.WriteUInt32LittleEndian(pe.AsSpan(optionalOffset + 92), 16);
    var resourceDirectory = optionalOffset + 96 + 2 * 8;
    BinaryPrimitives.WriteUInt32LittleEndian(pe.AsSpan(resourceDirectory), resourceRva);
    BinaryPrimitives.WriteUInt32LittleEndian(pe.AsSpan(resourceDirectory + 4), checked((uint)rawSize));

    var sectionOffset = optionalOffset + optionalHeaderSize;
    ".rsrc"u8.CopyTo(pe.AsSpan(sectionOffset));
    BinaryPrimitives.WriteUInt32LittleEndian(pe.AsSpan(sectionOffset + 8), checked((uint)rawSize));
    BinaryPrimitives.WriteUInt32LittleEndian(pe.AsSpan(sectionOffset + 12), resourceRva);
    BinaryPrimitives.WriteUInt32LittleEndian(pe.AsSpan(sectionOffset + 16), checked((uint)alignedRawSize));
    BinaryPrimitives.WriteUInt32LittleEndian(pe.AsSpan(sectionOffset + 20), resourceFileOffset);
    BinaryPrimitives.WriteUInt32LittleEndian(pe.AsSpan(sectionOffset + 36), 0x40000040);

    section.CopyTo(pe.AsSpan(resourceFileOffset));
    return pe;
  }

  private static int _Align4(int value) => checked((value + 3) & ~3);
}
