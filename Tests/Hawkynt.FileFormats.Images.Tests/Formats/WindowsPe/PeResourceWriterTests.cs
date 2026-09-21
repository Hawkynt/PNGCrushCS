using System;
using System.Buffers.Binary;
using System.IO;
using FileFormat.Core;

namespace FileFormat.WindowsPe.Tests;

[TestFixture]
public sealed class PeResourceWriterTests {

  // PeResourceModuleKind is internal, so it cannot appear in a public signature. The flag is
  // mapped inside the method, where the internal type is reachable.
  [TestCase(".exe", false)]
  [TestCase(".scr", false)]
  [TestCase(".dll", true)]
  [TestCase(".ocx", true)]
  [TestCase(".cpl", true)]
  public void FromRawImage_ExtensionSelectsModuleKind(string extension, bool expectsDll) {
    var expected = expectsDll ? PeResourceModuleKind.Dll : PeResourceModuleKind.Executable;
    var file = PeResourceFile.FromRawImage(_CreateImage(), extension);
    Assert.Multiple(() => {
      Assert.That(file.ModuleKind, Is.EqualTo(expected));
      Assert.That(file.ImageResources, Has.Count.EqualTo(1));
      Assert.That(file.ImageResources[0].ResourceType, Is.EqualTo(PeImageResourceType.Bitmap));
      Assert.That(file.ImageResources[0].ResourceId, Is.EqualTo(1));
      Assert.That(file.ImageResources[0].Data.AsSpan(0, 2).ToArray(), Is.EqualTo(new byte[] { (byte)'B', (byte)'M' }));
    });
  }

  [Test]
  public void FromRawImage_UnsupportedExtension_Throws()
    => Assert.Throws<ArgumentException>(() => PeResourceFile.FromRawImage(_CreateImage(), ".sys"));

  [Test]
  public void ToBytes_Executable_HasCodeAndResourceSections() {
    var bytes = PeResourceWriter.ToBytes(PeResourceFile.FromRawImage(_CreateImage(), ".exe"));
    var (coffOffset, optionalOffset) = _HeaderOffsets(bytes);

    Assert.Multiple(() => {
      Assert.That(BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(coffOffset + 2)), Is.EqualTo(2));
      Assert.That(BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(coffOffset + 18)) & 0x2000, Is.Zero);
      Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(optionalOffset + 16)), Is.EqualTo(0x1000u));
      Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(optionalOffset + 112)), Is.EqualTo(0x2000u));
      Assert.That(_SectionName(bytes, coffOffset, optionalOffset, 0), Is.EqualTo(".text"));
      Assert.That(_SectionName(bytes, coffOffset, optionalOffset, 1), Is.EqualTo(".rsrc"));
    });
  }

  [Test]
  public void ToBytes_Dll_IsNoEntryResourceDll() {
    var bytes = PeResourceWriter.ToBytes(PeResourceFile.FromRawImage(_CreateImage(), ".dll"));
    var (coffOffset, optionalOffset) = _HeaderOffsets(bytes);

    Assert.Multiple(() => {
      Assert.That(BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(coffOffset + 2)), Is.EqualTo(1));
      Assert.That(BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(coffOffset + 18)) & 0x2000, Is.EqualTo(0x2000));
      Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(optionalOffset + 16)), Is.Zero);
      Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(optionalOffset + 112)), Is.EqualTo(0x1000u));
      Assert.That(_SectionName(bytes, coffOffset, optionalOffset, 0), Is.EqualTo(".rsrc"));
    });
  }

  [Test]
  public void ToBytes_FromRawImage_RoundTripsPixels() {
    var source = _CreateImage();
    var bytes = PeResourceWriter.ToBytes(PeResourceFile.FromRawImage(source, ".dll"));
    var parsed = PeResourceReader.FromBytes(bytes);
    var actual = PeResourceFile.ToRawImage(parsed).EnsureFormat(PixelFormat.Rgb24);

    Assert.Multiple(() => {
      Assert.That(parsed.ModuleKind, Is.EqualTo(PeResourceModuleKind.Dll));
      Assert.That(parsed.ImageResources, Has.Count.EqualTo(1));
      Assert.That(actual.Width, Is.EqualTo(source.Width));
      Assert.That(actual.Height, Is.EqualTo(source.Height));
      Assert.That(actual.PixelData, Is.EqualTo(source.PixelData));
    });
  }

  [Test]
  public void ToBytes_EmbeddedImage_PreservesIdAndPayload() {
    byte[] png = [0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3, 4];
    var source = new PeResourceFile {
      ImageResources = [
        new PeImageResource {
          ResourceType = PeImageResourceType.EmbeddedImage,
          ResourceId = 17,
          Data = png,
          FormatHint = "png",
        }
      ],
      ModuleKind = PeResourceModuleKind.Dll,
    };

    var parsed = PeResourceReader.FromBytes(PeResourceWriter.ToBytes(source));

    Assert.Multiple(() => {
      Assert.That(parsed.ImageResources, Has.Count.EqualTo(1));
      Assert.That(parsed.ImageResources[0].ResourceType, Is.EqualTo(PeImageResourceType.EmbeddedImage));
      Assert.That(parsed.ImageResources[0].ResourceId, Is.EqualTo(17));
      Assert.That(parsed.ImageResources[0].FormatHint, Is.EqualTo("png"));
      Assert.That(parsed.ImageResources[0].Data, Is.EqualTo(png));
    });
  }

  /// <summary>
  /// A cursor group states the DIB's height; the .cur lifted out of it states half that, which is
  /// the displayed height, alongside the hotspot the RT_CURSOR component carries.
  /// </summary>
  /// <remarks>
  /// The builder writes CURSORDIR.wHeight as 22 for the 11-pixel cursor asked for here, because
  /// that is what every producer of an RT_GROUP_CURSOR emits -- see MinimalPeBuilder. Both numbers
  /// are asserted rather than only the second, so that the relation is pinned rather than being
  /// satisfied accidentally by a fixture and a reader making the same mistake: that is precisely
  /// how a missing halve survived here before. CursorGroupHeightTests holds the same relation to
  /// producers outside this repository.
  /// </remarks>
  [Test]
  public void Reader_CursorGroup_HalvesCursordirHeightAndKeepsHotspot() {
    var dib = MinimalPeBuilder.CreateMinimalIconEntry(16, 11);
    var pe = MinimalPeBuilder.BuildWithCursorGroup(dib, width: 16, height: 11, hotspotX: 3, hotspotY: 7, groupId: 23);

    var parsed = PeResourceReader.FromBytes(pe);

    Assert.Multiple(() => {
      Assert.That(parsed.IconGroups, Has.Count.EqualTo(1));
      Assert.That(parsed.IconGroups[0].IsCursor, Is.True);
      Assert.That(parsed.IconGroups[0].GroupId, Is.EqualTo(23));
      Assert.That(parsed.IconGroups[0].IcoData[6], Is.EqualTo(16));
      Assert.That(parsed.IconGroups[0].IcoData[7], Is.EqualTo(11), "the .cur directory states the displayed height");
      Assert.That(BinaryPrimitives.ReadUInt16LittleEndian(parsed.IconGroups[0].IcoData.AsSpan(10)), Is.EqualTo(3));
      Assert.That(BinaryPrimitives.ReadUInt16LittleEndian(parsed.IconGroups[0].IcoData.AsSpan(12)), Is.EqualTo(7));

      // The other half of the relation: what the group directory the reader was handed said.
      var group = _FindGroupCursorDirectory(pe);
      Assert.That(BinaryPrimitives.ReadUInt16LittleEndian(group.AsSpan(6)), Is.EqualTo(16));
      Assert.That(BinaryPrimitives.ReadUInt16LittleEndian(group.AsSpan(8)), Is.EqualTo(22), "CURSORDIR states the DIB height");
    });
  }

  /// <summary>Locates the single RT_GROUP_CURSOR leaf a <see cref="MinimalPeBuilder"/> PE holds.</summary>
  private static byte[] _FindGroupCursorDirectory(byte[] pe) {
    // The builder emits a 0x200-aligned .rsrc at a known file offset with one language per
    // resource, so the tree can be walked without a general-purpose parser.
    const int rsrcOffset = 0x200;
    var typeCount = BinaryPrimitives.ReadUInt16LittleEndian(pe.AsSpan(rsrcOffset + 14));
    for (var i = 0; i < typeCount; ++i) {
      var entry = rsrcOffset + 16 + i * 8;
      if (BinaryPrimitives.ReadUInt32LittleEndian(pe.AsSpan(entry)) != 12)
        continue;

      var names = rsrcOffset + (int)(BinaryPrimitives.ReadUInt32LittleEndian(pe.AsSpan(entry + 4)) & 0x7FFFFFFF);
      var languages = rsrcOffset + (int)(BinaryPrimitives.ReadUInt32LittleEndian(pe.AsSpan(names + 20)) & 0x7FFFFFFF);
      var leaf = rsrcOffset + (int)(BinaryPrimitives.ReadUInt32LittleEndian(pe.AsSpan(languages + 20)) & 0x7FFFFFFF);
      var rva = BinaryPrimitives.ReadUInt32LittleEndian(pe.AsSpan(leaf));
      var size = BinaryPrimitives.ReadInt32LittleEndian(pe.AsSpan(leaf + 4));
      return pe.AsSpan(rsrcOffset + (int)(rva - 0x1000), size).ToArray();
    }

    throw new InvalidDataException("The built PE carries no RT_GROUP_CURSOR.");
  }

  [Test]
  public void ToBytes_CursorGroup_RoundTripsCompleteCur() {
    var dib = MinimalPeBuilder.CreateMinimalIconEntry(16, 11);
    var original = PeResourceReader.FromBytes(
      MinimalPeBuilder.BuildWithCursorGroup(dib, width: 16, height: 11, hotspotX: 3, hotspotY: 7, groupId: 23)
    );
    original = new PeResourceFile {
      IconGroups = original.IconGroups,
      ImageResources = original.ImageResources,
      ModuleKind = PeResourceModuleKind.Dll,
    };

    var parsed = PeResourceReader.FromBytes(PeResourceWriter.ToBytes(original));

    Assert.Multiple(() => {
      Assert.That(parsed.ImageResources, Has.Count.EqualTo(1));
      Assert.That(parsed.ImageResources[0].ResourceType, Is.EqualTo(PeImageResourceType.Cursor));
      Assert.That(parsed.ImageResources[0].ResourceId, Is.EqualTo(23));
      Assert.That(parsed.ImageResources[0].Data, Is.EqualTo(original.ImageResources[0].Data));
    });
  }

  [Test]
  public void ToBytes_EmptyFile_Throws()
    => Assert.Throws<InvalidDataException>(() => PeResourceWriter.ToBytes(new PeResourceFile()));

  [Test]
  public void ToBytes_BitmapWithoutFileHeader_Throws() {
    var file = new PeResourceFile {
      ImageResources = [
        new PeImageResource {
          ResourceType = PeImageResourceType.Bitmap,
          ResourceId = 1,
          Data = MinimalPeBuilder.CreateMinimalDib(),
        }
      ]
    };

    Assert.Throws<InvalidDataException>(() => PeResourceWriter.ToBytes(file));
  }

  private static RawImage _CreateImage() => new() {
    Width = 3,
    Height = 2,
    Format = PixelFormat.Rgb24,
    PixelData = [
      255, 0, 0,     0, 255, 0,     0, 0, 255,
      12, 34, 56,    78, 90, 123,   210, 180, 140,
    ],
  };

  private static (int CoffOffset, int OptionalOffset) _HeaderOffsets(byte[] data) {
    var peOffset = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(60));
    Assert.That(data.AsSpan(peOffset, 4).ToArray(), Is.EqualTo(new byte[] { (byte)'P', (byte)'E', 0, 0 }));
    var coffOffset = peOffset + 4;
    return (coffOffset, coffOffset + 20);
  }

  private static string _SectionName(byte[] data, int coffOffset, int optionalOffset, int index) {
    var optionalSize = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(coffOffset + 16));
    var offset = optionalOffset + optionalSize + index * 40;
    var length = 0;
    while (length < 8 && data[offset + length] != 0)
      ++length;
    return System.Text.Encoding.ASCII.GetString(data, offset, length);
  }
}
