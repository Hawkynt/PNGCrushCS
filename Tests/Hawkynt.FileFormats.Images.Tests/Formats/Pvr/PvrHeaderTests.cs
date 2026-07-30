using System;
using System.Buffers.Binary;
using System.Linq;
using FileFormat.Core;
using FileFormat.Pvr;

namespace FileFormat.Pvr.Tests;

[TestFixture]
public sealed class PvrHeaderTests {

  [Test]
  [Category("Unit")]
  public void RoundTrip_PreservesAllFields() {
    var original = new PvrHeader(
      PvrHeader.Magic,
      0x02,
      (ulong)PvrPixelFormat.PVRTC_4BPP_RGBA,
      (uint)PvrColorSpace.Srgb,
      0,
      256,
      512,
      1,
      1,
      6,
      4,
      128
    );

    var buffer = new byte[PvrHeader.StructSize];
    original.WriteTo(buffer);
    var parsed = PvrHeader.ReadFrom(buffer);

    Assert.That(parsed.Version, Is.EqualTo(original.Version));
    Assert.That(parsed.Flags, Is.EqualTo(original.Flags));
    Assert.That(parsed.PixelFormat, Is.EqualTo(original.PixelFormat));
    Assert.That(parsed.ColorSpace, Is.EqualTo(original.ColorSpace));
    Assert.That(parsed.ChannelType, Is.EqualTo(original.ChannelType));
    Assert.That(parsed.Height, Is.EqualTo(original.Height));
    Assert.That(parsed.Width, Is.EqualTo(original.Width));
    Assert.That(parsed.Depth, Is.EqualTo(original.Depth));
    Assert.That(parsed.Surfaces, Is.EqualTo(original.Surfaces));
    Assert.That(parsed.Faces, Is.EqualTo(original.Faces));
    Assert.That(parsed.MipmapCount, Is.EqualTo(original.MipmapCount));
    Assert.That(parsed.MetadataSize, Is.EqualTo(original.MetadataSize));
  }

  [Test]
  [Category("Unit")]
  public void GetFieldMap_CoversFullStructSize() {
    var map = PvrHeader.GetFieldMap();
    var totalSize = map.Sum(f => f.Size);
    Assert.That(totalSize, Is.EqualTo(PvrHeader.StructSize));
  }

  [Test]
  [Category("Unit")]
  public void GetFieldMap_HasNoOverlaps() {
    var map = PvrHeader.GetFieldMap();
    for (var i = 0; i < map.Length - 1; ++i)
      Assert.That(map[i].Offset + map[i].Size, Is.LessThanOrEqualTo(map[i + 1].Offset), $"Field {map[i].Name} overlaps with {map[i + 1].Name}");
  }

  [Test]
  [Category("Unit")]
  public void StructSize_Is52() {
    Assert.That(PvrHeader.StructSize, Is.EqualTo(52));
  }

  [Test]
  [Category("Unit")]
  public void ReadFrom_ParsesKnownValues() {
    var data = new byte[52];
    BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(0), PvrHeader.Magic);
    BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(4), 0x02);
    BinaryPrimitives.WriteUInt64LittleEndian(data.AsSpan(8), 3);   // PVRTC_4BPP_RGBA
    BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(16), 1);  // Srgb
    BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(24), 128); // Height
    BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(28), 256); // Width

    var header = PvrHeader.ReadFrom(data);
    Assert.Multiple(() => {
      Assert.That(header.Version, Is.EqualTo(PvrHeader.Magic));
      Assert.That(header.Flags, Is.EqualTo(0x02u));
      Assert.That(header.PixelFormat, Is.EqualTo((ulong)PvrPixelFormat.PVRTC_4BPP_RGBA));
      Assert.That(header.ColorSpace, Is.EqualTo(1u));
      Assert.That(header.Height, Is.EqualTo(128u));
      Assert.That(header.Width, Is.EqualTo(256u));
    });
  }
}
