using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FileFormat.Core;
using FileFormat.Png;

namespace FileFormat.WindowsPe.Tests;

[TestFixture]
public sealed class PeResourceSafetyTests {

  [Test]
  public void ReadEditable_EmbeddedImageRetainsOriginalTypeNameAndLanguageSelectors() {
    byte[] png = [0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3, 4];
    var pe = _BuildPe(new ResourceLeaf(10, 17, 0x0411, png));

    var resource = PeResourceFile.ReadEditable(pe).ImageResources.Single();

    Assert.Multiple(() => {
      Assert.That(resource.ResourceType, Is.EqualTo(PeImageResourceType.EmbeddedImage));
      Assert.That(resource.ResourceTypeId, Is.EqualTo(10));
      Assert.That(resource.ResourceTypeName, Is.Null);
      Assert.That(resource.ResourceId, Is.EqualTo(17));
      Assert.That(resource.ResourceName, Is.Null);
      Assert.That(resource.LanguageId, Is.EqualTo(0x0411));
      Assert.That(resource.LanguageName, Is.Null);
      Assert.That(resource.FormatHint, Is.EqualTo("png"));
    });
  }

  [Test]
  public void ReadEditable_EmbeddedImage_RetainsEveryLanguageVariant() {
    var german = _Png(0x22);
    var english = _Png(0x11);
    var pe = _BuildPe(
      new ResourceLeaf(10, 100, 0x0407, german),
      new ResourceLeaf(10, 100, 0x0409, english)
    );

    var images = PeResourceFile.ReadEditable(pe).ImageResources
      .Where(static image => image.ResourceType == PeImageResourceType.EmbeddedImage)
      .OrderBy(static image => image.LanguageId)
      .ToArray();

    Assert.Multiple(() => {
      Assert.That(images, Has.Length.EqualTo(2));
      Assert.That(images.Select(static image => image.ResourceTypeId), Is.All.EqualTo(10));
      Assert.That(images.Select(static image => image.ResourceId), Is.All.EqualTo(100));
      Assert.That(images.Select(static image => image.ResourceTypeName), Is.All.Null);
      Assert.That(images.Select(static image => image.ResourceName), Is.All.Null);
      Assert.That(images.Select(static image => image.LanguageName), Is.All.Null);
      Assert.That(images.Select(static image => image.LanguageId), Is.EqualTo(new int?[] { 0x0407, 0x0409 }));
      Assert.That(images[0].Data, Is.EqualTo(german));
      Assert.That(images[1].Data, Is.EqualTo(english));
    });
  }

  [Test]
  public void ReplaceImage_ByIndex_UsesTheRetainedEmbeddedImageLanguage() {
    var german = _Png(0x22);
    var english = _Png(0x11);
    var pe = _BuildPe(
      new ResourceLeaf(10, 100, 0x0407, german),
      new ResourceLeaf(10, 100, 0x0409, english)
    );
    var editable = PeResourceFile.ReadEditable(pe);
    var germanIndex = editable.ImageResources
      .Select(static (image, index) => (image, index))
      .Single(static pair =>
        pair.image.ResourceType == PeImageResourceType.EmbeddedImage && pair.image.LanguageId == 0x0407)
      .index;

    // No language is passed here on purpose: type 10 / ID 100 is ambiguous without one, so the edit
    // can only succeed if the selected image resource still carries the language it was read under.
    var replacement = _Image(2, 2);
    var edited = editable.ReplaceImage(germanIndex, replacement);

    var images = edited.ImageResources
      .Where(static image => image.ResourceType == PeImageResourceType.EmbeddedImage)
      .ToArray();

    Assert.Multiple(() => {
      Assert.That(
        images.Single(static image => image.LanguageId == 0x0407).Data,
        Is.EqualTo(PngWriter.ToBytes(PngFile.FromRawImage(replacement)))
      );
      Assert.That(images.Single(static image => image.LanguageId == 0x0409).Data, Is.EqualTo(english));
    });
  }

  [Test]
  public void ReadEditable_IconGroupTooLargeToAssemble_YieldsNoGroupInsteadOfOverflowing() {
    const ushort count = ushort.MaxValue;
    var component = new byte[32768];
    var group = new byte[6 + count * 14];
    BinaryPrimitives.WriteUInt16LittleEndian(group.AsSpan(2), 1);
    BinaryPrimitives.WriteUInt16LittleEndian(group.AsSpan(4), count);
    for (var i = 0; i < count; ++i) {
      var entry = 6 + i * 14;
      group[entry] = 16;
      group[entry + 1] = 16;
      BinaryPrimitives.WriteUInt16LittleEndian(group.AsSpan(entry + 4), 1);
      BinaryPrimitives.WriteUInt16LittleEndian(group.AsSpan(entry + 6), 32);
      BinaryPrimitives.WriteInt32LittleEndian(group.AsSpan(entry + 8), component.Length);
      BinaryPrimitives.WriteUInt16LittleEndian(group.AsSpan(entry + 12), 1);
    }

    // 65535 entries of 32 KiB each: the assembled ICO would be just over 2 GiB, which used to wrap
    // into a negative array length instead of being refused.
    var pe = _BuildPe(
      new ResourceLeaf(3, 1, 0x0409, component),
      new ResourceLeaf(14, 1, 0x0409, group)
    );

    PeResourceFile? file = null;
    Assert.DoesNotThrow(() => file = PeResourceFile.ReadEditable(pe));
    Assert.Multiple(() => {
      Assert.That(file!.IconGroups, Is.Empty);
      Assert.That(file.ImageResources.Any(static image => image.ResourceType == PeImageResourceType.Icon), Is.False);
    });
  }

  [Test]
  public void DetectImageSignature_RangeOverflowingIntArithmetic_ReturnsNull()
    => Assert.That(PeResourceReader._DetectImageSignature(new byte[8], int.MaxValue, 8), Is.Null);

  [Test]
  public void ReplaceResource_WithoutLanguage_RejectsActualMultipleLanguageVariants() {
    var dib = MinimalPeBuilder.CreateMinimalDib();
    var pe = _BuildPe(
      new ResourceLeaf(2, 7, 0x0407, dib),
      new ResourceLeaf(2, 7, 0x0409, dib)
    );
    var editable = PeResourceFile.ReadEditable(pe);

    var exception = Assert.Throws<InvalidOperationException>(() =>
      editable.ReplaceResource(2, 7, MinimalPeBuilder.CreateMinimalDib(3, 2)));

    Assert.That(exception!.Message, Does.Contain("multiple language variants"));
  }

  [Test]
  public void ReplaceResource_WithLanguage_AllowsMultipleLanguageVariants() {
    var dib = MinimalPeBuilder.CreateMinimalDib();
    var pe = _BuildPe(
      new ResourceLeaf(2, 7, 0x0407, dib),
      new ResourceLeaf(2, 7, 0x0409, dib)
    );
    var editable = PeResourceFile.ReadEditable(pe);

    Assert.DoesNotThrow(() => editable.ReplaceResource(2, 7, 0x0409, MinimalPeBuilder.CreateMinimalDib(3, 2)));
  }

  [Test]
  public void ReplaceImage_SharedRtIconComponent_RejectsImplicitCrossGroupEdit() {
    var icon = MinimalPeBuilder.CreateMinimalIconEntry();
    var pe = _BuildPe(
      new ResourceLeaf(3, 1, 0x0409, icon),
      new ResourceLeaf(14, 10, 0x0409, _GroupIcon(1, icon.Length)),
      new ResourceLeaf(14, 11, 0x0409, _GroupIcon(1, icon.Length))
    );
    var editable = PeResourceFile.ReadEditable(pe);
    var replacement = _Image(8, 8);

    var exception = Assert.Throws<InvalidOperationException>(() =>
      editable.ReplaceImage(PeImageResourceType.Icon, 10, 0, replacement, 0x0409));

    Assert.That(exception!.Message, Does.Contain("referenced by multiple RT_GROUP_ICON entries"));
  }

  [Test]
  public void ReplaceImage_SharedRtCursorComponent_RejectsImplicitCrossGroupEdit() {
    var dib = MinimalPeBuilder.CreateMinimalIconEntry();
    var cursor = new byte[dib.Length + 4];
    BinaryPrimitives.WriteUInt16LittleEndian(cursor, 2);
    BinaryPrimitives.WriteUInt16LittleEndian(cursor.AsSpan(2), 3);
    dib.CopyTo(cursor.AsSpan(4));
    var pe = _BuildPe(
      new ResourceLeaf(1, 1, 0x0409, cursor),
      new ResourceLeaf(12, 20, 0x0409, _GroupCursor(1, cursor.Length, 16, 32)),
      new ResourceLeaf(12, 21, 0x0409, _GroupCursor(1, cursor.Length, 16, 32))
    );
    var editable = PeResourceFile.ReadEditable(pe);
    var replacement = _Image(8, 8);

    var exception = Assert.Throws<InvalidOperationException>(() =>
      editable.ReplaceImage(PeImageResourceType.Cursor, 20, 0, replacement, 0x0409));

    Assert.That(exception!.Message, Does.Contain("referenced by multiple RT_GROUP_CURSOR entries"));
  }

  /// <summary>A payload that is recognized as PNG by signature but is not a decodable image.</summary>
  private static byte[] _Png(byte marker)
    => [0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A, marker, 2, 3, 4];

  private readonly record struct ResourceLeaf(int TypeId, int ResourceId, int LanguageId, byte[] Data);

  private sealed record ResourceName(int Id, List<ResourceLeaf> Languages);
  private sealed record ResourceType(int Id, List<ResourceName> Names);

  private static byte[] _BuildPe(params ResourceLeaf[] leaves) {
    var types = leaves
      .GroupBy(static leaf => leaf.TypeId)
      .OrderBy(static group => group.Key)
      .Select(type => new ResourceType(
        type.Key,
        type.GroupBy(static leaf => leaf.ResourceId)
          .OrderBy(static group => group.Key)
          .Select(name => new ResourceName(name.Key, name.OrderBy(static leaf => leaf.LanguageId).ToList()))
          .ToList()
      ))
      .ToList();

    var rootSize = 16 + types.Count * 8;
    var namesSize = types.Sum(static type => 16 + type.Names.Count * 8);
    var languagesSize = types.Sum(static type => type.Names.Sum(name => 16 + name.Languages.Count * 8));
    var leafCount = leaves.Length;
    var dataEntriesSize = leafCount * 16;
    var directoryBytes = rootSize + namesSize + languagesSize + dataEntriesSize;
    var payloadOffset = _Align4(directoryBytes);
    var payloadBytes = leaves.Sum(static leaf => _Align4(leaf.Data.Length));
    var resourceSection = new byte[payloadOffset + payloadBytes];

    BinaryPrimitives.WriteUInt16LittleEndian(resourceSection.AsSpan(14), checked((ushort)types.Count));

    var rootEntry = 16;
    var nextNameDirectory = rootSize;
    var nextLanguageDirectory = rootSize + namesSize;
    var nextDataEntry = rootSize + namesSize + languagesSize;
    var nextPayload = payloadOffset;

    foreach (var type in types) {
      BinaryPrimitives.WriteUInt32LittleEndian(resourceSection.AsSpan(rootEntry), checked((uint)type.Id));
      BinaryPrimitives.WriteUInt32LittleEndian(resourceSection.AsSpan(rootEntry + 4), 0x80000000u | checked((uint)nextNameDirectory));
      rootEntry += 8;

      BinaryPrimitives.WriteUInt16LittleEndian(resourceSection.AsSpan(nextNameDirectory + 14), checked((ushort)type.Names.Count));
      var nameEntry = nextNameDirectory + 16;

      foreach (var name in type.Names) {
        BinaryPrimitives.WriteUInt32LittleEndian(resourceSection.AsSpan(nameEntry), checked((uint)name.Id));
        BinaryPrimitives.WriteUInt32LittleEndian(resourceSection.AsSpan(nameEntry + 4), 0x80000000u | checked((uint)nextLanguageDirectory));
        nameEntry += 8;

        BinaryPrimitives.WriteUInt16LittleEndian(resourceSection.AsSpan(nextLanguageDirectory + 14), checked((ushort)name.Languages.Count));
        var languageEntry = nextLanguageDirectory + 16;

        foreach (var leaf in name.Languages) {
          BinaryPrimitives.WriteUInt32LittleEndian(resourceSection.AsSpan(languageEntry), checked((uint)leaf.LanguageId));
          BinaryPrimitives.WriteUInt32LittleEndian(resourceSection.AsSpan(languageEntry + 4), checked((uint)nextDataEntry));
          languageEntry += 8;

          BinaryPrimitives.WriteUInt32LittleEndian(resourceSection.AsSpan(nextDataEntry), checked(0x1000u + (uint)nextPayload));
          BinaryPrimitives.WriteUInt32LittleEndian(resourceSection.AsSpan(nextDataEntry + 4), checked((uint)leaf.Data.Length));
          nextDataEntry += 16;

          leaf.Data.CopyTo(resourceSection.AsSpan(nextPayload));
          nextPayload += _Align4(leaf.Data.Length);
        }

        nextLanguageDirectory += 16 + name.Languages.Count * 8;
      }

      nextNameDirectory += 16 + type.Names.Count * 8;
    }

    const int peOffset = 0x80;
    const int optionalHeaderSize = 0xE0;
    const int fileAlignment = 0x200;
    const int sectionAlignment = 0x1000;
    const int sectionRawOffset = 0x200;
    var sectionRawSize = _Align(resourceSection.Length, fileAlignment);
    var pe = new byte[sectionRawOffset + sectionRawSize];

    pe[0] = (byte)'M';
    pe[1] = (byte)'Z';
    BinaryPrimitives.WriteInt32LittleEndian(pe.AsSpan(60), peOffset);
    pe[peOffset] = (byte)'P';
    pe[peOffset + 1] = (byte)'E';

    var coff = peOffset + 4;
    BinaryPrimitives.WriteUInt16LittleEndian(pe.AsSpan(coff), 0x014C);
    BinaryPrimitives.WriteUInt16LittleEndian(pe.AsSpan(coff + 2), 1);
    BinaryPrimitives.WriteUInt16LittleEndian(pe.AsSpan(coff + 16), optionalHeaderSize);
    BinaryPrimitives.WriteUInt16LittleEndian(pe.AsSpan(coff + 18), 0x2102);

    var optional = coff + 20;
    BinaryPrimitives.WriteUInt16LittleEndian(pe.AsSpan(optional), 0x10B);
    BinaryPrimitives.WriteUInt32LittleEndian(pe.AsSpan(optional + 28), 0x00400000);
    BinaryPrimitives.WriteUInt32LittleEndian(pe.AsSpan(optional + 32), sectionAlignment);
    BinaryPrimitives.WriteUInt32LittleEndian(pe.AsSpan(optional + 36), fileAlignment);
    BinaryPrimitives.WriteUInt32LittleEndian(pe.AsSpan(optional + 56), 0x2000);
    BinaryPrimitives.WriteUInt32LittleEndian(pe.AsSpan(optional + 60), sectionRawOffset);
    BinaryPrimitives.WriteUInt16LittleEndian(pe.AsSpan(optional + 68), 2);
    BinaryPrimitives.WriteUInt32LittleEndian(pe.AsSpan(optional + 92), 16);
    BinaryPrimitives.WriteUInt32LittleEndian(pe.AsSpan(optional + 112), 0x1000);
    BinaryPrimitives.WriteUInt32LittleEndian(pe.AsSpan(optional + 116), checked((uint)resourceSection.Length));

    var section = optional + optionalHeaderSize;
    ".rsrc"u8.CopyTo(pe.AsSpan(section));
    BinaryPrimitives.WriteUInt32LittleEndian(pe.AsSpan(section + 8), checked((uint)resourceSection.Length));
    BinaryPrimitives.WriteUInt32LittleEndian(pe.AsSpan(section + 12), 0x1000);
    BinaryPrimitives.WriteUInt32LittleEndian(pe.AsSpan(section + 16), checked((uint)sectionRawSize));
    BinaryPrimitives.WriteUInt32LittleEndian(pe.AsSpan(section + 20), sectionRawOffset);
    BinaryPrimitives.WriteUInt32LittleEndian(pe.AsSpan(section + 36), 0x40000040);

    resourceSection.CopyTo(pe.AsSpan(sectionRawOffset));
    return pe;
  }

  private static byte[] _GroupIcon(ushort componentId, int componentSize) {
    var group = new byte[20];
    BinaryPrimitives.WriteUInt16LittleEndian(group.AsSpan(2), 1);
    BinaryPrimitives.WriteUInt16LittleEndian(group.AsSpan(4), 1);
    group[6] = 16;
    group[7] = 16;
    BinaryPrimitives.WriteUInt16LittleEndian(group.AsSpan(10), 1);
    BinaryPrimitives.WriteUInt16LittleEndian(group.AsSpan(12), 32);
    BinaryPrimitives.WriteUInt32LittleEndian(group.AsSpan(14), checked((uint)componentSize));
    BinaryPrimitives.WriteUInt16LittleEndian(group.AsSpan(18), componentId);
    return group;
  }

  /// <param name="height">
  /// The CURSORDIR value, which is the DIB's height and so twice the displayed one -- a cursor
  /// stacks its XOR bitmap on its AND mask. A 16-pixel cursor is written here as 32.
  /// </param>
  private static byte[] _GroupCursor(ushort componentId, int componentSize, ushort width, ushort height) {
    var group = new byte[20];
    BinaryPrimitives.WriteUInt16LittleEndian(group.AsSpan(2), 2);
    BinaryPrimitives.WriteUInt16LittleEndian(group.AsSpan(4), 1);
    BinaryPrimitives.WriteUInt16LittleEndian(group.AsSpan(6), width);
    BinaryPrimitives.WriteUInt16LittleEndian(group.AsSpan(8), height);
    BinaryPrimitives.WriteUInt16LittleEndian(group.AsSpan(10), 1);
    BinaryPrimitives.WriteUInt16LittleEndian(group.AsSpan(12), 32);
    BinaryPrimitives.WriteUInt32LittleEndian(group.AsSpan(14), checked((uint)componentSize));
    BinaryPrimitives.WriteUInt16LittleEndian(group.AsSpan(18), componentId);
    return group;
  }

  private static RawImage _Image(int width, int height) {
    var pixels = new byte[checked(width * height * 3)];
    for (var i = 0; i < pixels.Length; ++i)
      pixels[i] = (byte)(i * 29 + 7);
    return new RawImage {
      Width = width,
      Height = height,
      Format = PixelFormat.Rgb24,
      PixelData = pixels,
    };
  }

  private static int _Align4(int value) => checked((value + 3) & ~3);

  private static int _Align(int value, int alignment)
    => checked((value + alignment - 1) / alignment * alignment);
}
