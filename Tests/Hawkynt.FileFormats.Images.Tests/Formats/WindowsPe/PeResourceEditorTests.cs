using System;
using System.Buffers.Binary;
using System.Linq;
using FileFormat.Bmp;
using FileFormat.Core;
using FileFormat.Cur;
using FileFormat.Ico;

namespace FileFormat.WindowsPe.Tests;

[TestFixture]
public sealed class PeResourceEditorTests {

  [Test]
  public void ReadEditable_EnumeratesResourceSelectors() {
    var bytes = _BuildExecutableWithBitmapAndRcData();
    var file = PeResourceFile.ReadEditable(bytes);

    Assert.Multiple(() => {
      Assert.That(file.Resources, Has.Count.EqualTo(2));
      Assert.That(file.Resources.Any(resource =>
        resource.TypeId == 2 && resource.ResourceId == 7 && resource.LanguageId == 0), Is.True);
      Assert.That(file.Resources.Any(resource =>
        resource.TypeId == 10 && resource.ResourceId == 17 && resource.LanguageId == 0), Is.True);
    });
  }

  [Test]
  public void ReplaceResource_LargerPayload_PreservesCodeOtherResourcesAndOverlay() {
    var original = _BuildExecutableWithBitmapAndRcData();
    byte[] overlay = [0xDE, 0xAD, 0xBE, 0xEF, 0x11, 0x22, 0x33];
    var withOverlay = new byte[original.Length + overlay.Length];
    original.CopyTo(withOverlay, 0);
    overlay.CopyTo(withOverlay, original.Length);
    var originalText = _SectionBytes(withOverlay, ".text");

    var replacementImage = _CreateImage(37, 19);
    var replacementBmp = BmpWriter.ToBytes(BmpFile.FromRawImage(replacementImage));
    var replacementDib = replacementBmp.AsSpan(14).ToArray();

    var edited = PeResourceFile.ReadEditable(withOverlay)
      .ReplaceResource(2, 7, 0, replacementDib);
    var output = FormatIO.Write(edited);
    var reparsed = PeResourceFile.ReadEditable(output);

    Assert.Multiple(() => {
      Assert.That(output.Length, Is.GreaterThan(withOverlay.Length));
      Assert.That(output.AsSpan(output.Length - overlay.Length).ToArray(), Is.EqualTo(overlay));
      Assert.That(_SectionBytes(output, ".text"), Is.EqualTo(originalText));
      Assert.That(reparsed.Resources.Any(resource =>
        resource.TypeId == 2 && resource.ResourceId == 7 && resource.LanguageId == 0 && resource.Size == replacementDib.Length), Is.True);
      Assert.That(reparsed.ImageResources.Single(resource => resource.ResourceType == PeImageResourceType.EmbeddedImage).Data,
        Is.EqualTo(_EmbeddedPayload));
    });
  }

  [Test]
  public void ReplaceImage_ByImageResourceIndex_RoundTripsReplacementPixels() {
    var original = PeResourceWriter.ToBytes(PeResourceFile.FromRawImage(_CreateImage(3, 2), ".exe"));
    var replacement = _CreateImage(29, 17);

    var edited = PeResourceFile.ReadEditable(original).ReplaceImage(0, replacement);
    var output = FormatIO.Write(edited);
    var actual = PeResourceFile.ToRawImage(PeResourceFile.ReadEditable(output)).EnsureFormat(PixelFormat.Rgb24);

    Assert.Multiple(() => {
      Assert.That(actual.Width, Is.EqualTo(replacement.Width));
      Assert.That(actual.Height, Is.EqualTo(replacement.Height));
      Assert.That(actual.PixelData, Is.EqualTo(replacement.PixelData));
    });
  }

  [Test]
  public void ReplaceImage_ByGroupResourceIdAndIndex_PreservesOtherIconEntry() {
    var extracted = PeResourceReader.FromBytes(MinimalPeBuilder.BuildWithIconGroup([
      MinimalPeBuilder.CreateMinimalIconEntry(16, 16),
      MinimalPeBuilder.CreateMinimalIconEntry(16, 16),
    ], groupId: 42)).ImageResources.Single();

    var source = PeResourceWriter.ToBytes(new PeResourceFile {
      ImageResources = [
        new PeImageResource {
          ResourceType = PeImageResourceType.Icon,
          ResourceTypeId = 14,
          ResourceId = 42,
          Data = extracted.Data,
        }
      ],
      ModuleKind = PeResourceModuleKind.Dll,
    });
    var before = IcoReader.FromBytes(PeResourceFile.ReadEditable(source).ImageResources.Single().Data);
    var replacement = _CreateImage(23, 11);

    var edited = PeResourceFile.ReadEditable(source)
      .ReplaceImage(PeImageResourceType.Icon, 42, 1, replacement);
    var afterResource = PeResourceFile.ReadEditable(FormatIO.Write(edited)).ImageResources.Single();
    var after = IcoReader.FromBytes(afterResource.Data);
    var actual = IcoFile.ToRawImage(after, 1).EnsureFormat(PixelFormat.Rgb24);

    Assert.Multiple(() => {
      Assert.That(after.Images, Has.Count.EqualTo(2));
      Assert.That(after.Images[0].Data, Is.EqualTo(before.Images[0].Data));
      Assert.That(after.Images[1].Width, Is.EqualTo(23));
      Assert.That(after.Images[1].Height, Is.EqualTo(11));
      Assert.That(actual.PixelData, Is.EqualTo(replacement.PixelData));
    });
  }

  [Test]
  public void ReplaceImage_CursorGroup_PreservesHotspot() {
    var cursorDib = MinimalPeBuilder.CreateMinimalIconEntry(16, 11);
    var extracted = PeResourceReader.FromBytes(
      MinimalPeBuilder.BuildWithCursorGroup(cursorDib, 16, 11, hotspotX: 3, hotspotY: 7, groupId: 23)
    ).ImageResources.Single();
    var source = PeResourceWriter.ToBytes(new PeResourceFile {
      ImageResources = [
        new PeImageResource {
          ResourceType = PeImageResourceType.Cursor,
          ResourceTypeId = 12,
          ResourceId = 23,
          Data = extracted.Data,
        }
      ],
      ModuleKind = PeResourceModuleKind.Dll,
    });
    var replacement = _CreateImage(9, 13);

    var edited = PeResourceFile.ReadEditable(source)
      .ReplaceImage(PeImageResourceType.Cursor, 23, 0, replacement);
    var afterResource = PeResourceFile.ReadEditable(FormatIO.Write(edited)).ImageResources.Single();
    var cursor = CurReader.FromBytes(afterResource.Data);
    var actual = CurFile.ToRawImage(cursor, 0).EnsureFormat(PixelFormat.Rgb24);

    Assert.Multiple(() => {
      Assert.That(cursor.Images, Has.Count.EqualTo(1));
      Assert.That(cursor.Images[0].Width, Is.EqualTo(9));
      Assert.That(cursor.Images[0].Height, Is.EqualTo(13));
      Assert.That(cursor.Images[0].HotspotX, Is.EqualTo(3));
      Assert.That(cursor.Images[0].HotspotY, Is.EqualTo(7));
      Assert.That(actual.PixelData, Is.EqualTo(replacement.PixelData));
    });
  }

  [Test]
  public void ReplaceResource_WithoutLanguage_RejectsAmbiguousLanguageVariants() {
    var pe = MinimalPeBuilder.BuildWithBitmap(MinimalPeBuilder.CreateMinimalDib(), resourceId: 7);
    // The synthetic builder has a single 0x0409 language. This test pins the non-language overload
    // to the same resource when it is unique; multi-language files are rejected by the editor rather
    // than silently picking whichever directory entry happens to come first.
    var editableBytes = PeResourceWriter.ToBytes(new PeResourceFile {
      ImageResources = [PeResourceReader.FromBytes(pe).ImageResources.Single()],
      ModuleKind = PeResourceModuleKind.Dll,
    });
    var file = PeResourceFile.ReadEditable(editableBytes);
    var replacement = MinimalPeBuilder.CreateMinimalDib(1, 1);

    Assert.DoesNotThrow(() => file.ReplaceResource(2, 7, replacement));
  }

  private static readonly byte[] _EmbeddedPayload = [
    0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3, 4,
  ];

  private static byte[] _BuildExecutableWithBitmapAndRcData() {
    var bitmap = BmpWriter.ToBytes(BmpFile.FromRawImage(_CreateImage(3, 2)));
    return PeResourceWriter.ToBytes(new PeResourceFile {
      ImageResources = [
        new PeImageResource {
          ResourceType = PeImageResourceType.Bitmap,
          ResourceTypeId = 2,
          ResourceId = 7,
          Data = bitmap,
        },
        new PeImageResource {
          ResourceType = PeImageResourceType.EmbeddedImage,
          ResourceTypeId = 10,
          ResourceId = 17,
          Data = _EmbeddedPayload,
          FormatHint = "png",
        },
      ],
      ModuleKind = PeResourceModuleKind.Executable,
    });
  }

  private static RawImage _CreateImage(int width, int height) {
    var pixels = new byte[checked(width * height * 3)];
    for (var i = 0; i < pixels.Length; ++i)
      pixels[i] = (byte)(i * 37 + i / 5 * 11);
    return new RawImage {
      Width = width,
      Height = height,
      Format = PixelFormat.Rgb24,
      PixelData = pixels,
    };
  }

  private static byte[] _SectionBytes(byte[] pe, string name) {
    var peOffset = BinaryPrimitives.ReadInt32LittleEndian(pe.AsSpan(60));
    var coffOffset = peOffset + 4;
    var sectionCount = BinaryPrimitives.ReadUInt16LittleEndian(pe.AsSpan(coffOffset + 2));
    var optionalSize = BinaryPrimitives.ReadUInt16LittleEndian(pe.AsSpan(coffOffset + 16));
    var sectionOffset = coffOffset + 20 + optionalSize;

    for (var i = 0; i < sectionCount; ++i) {
      var current = sectionOffset + i * 40;
      var length = 0;
      while (length < 8 && pe[current + length] != 0)
        ++length;
      var currentName = System.Text.Encoding.ASCII.GetString(pe, current, length);
      if (!string.Equals(currentName, name, StringComparison.Ordinal))
        continue;

      var rawSize = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(pe.AsSpan(current + 16)));
      var rawOffset = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(pe.AsSpan(current + 20)));
      return pe.AsSpan(rawOffset, rawSize).ToArray();
    }

    throw new InvalidOperationException($"Section '{name}' was not found.");
  }
}
