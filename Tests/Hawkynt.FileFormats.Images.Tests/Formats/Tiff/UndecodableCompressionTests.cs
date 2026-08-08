using System;
using System.IO;
using FileFormat.Tiff;

namespace FileFormat.Tiff.Tests;

/// <summary>
/// A TIFF whose compression this cannot decode is refused, not returned blank.
/// </summary>
/// <remarks>
/// The library returns failure for a strip it cannot decode and the buffer is left as it was, so a
/// file using a vendor's private compression came back as a white page reported as a success. A
/// blank page filed under a document's name is worse than a refusal: nothing downstream can tell it
/// from a document that really is blank.
/// </remarks>
[TestFixture]
public sealed class UndecodableCompressionTests {

  /// <summary>A little-endian TIFF stating a compression nothing here has, over one strip.</summary>
  private static byte[] _PrivateCompressionTiff() {
    using var ms = new MemoryStream();
    using var w = new BinaryWriter(ms);

    w.Write((ushort)0x4949);
    w.Write((ushort)42);
    w.Write((uint)8);

    var entries = new (ushort Tag, ushort Type, uint Count, uint Value)[] {
      (256, 3, 1, 64),     // width
      (257, 3, 1, 64),     // height
      (258, 3, 1, 8),      // bits per sample
      (259, 3, 1, 34673),  // compression: a private one
      (262, 3, 1, 1),      // photometric: min is black
      (273, 4, 1, 200),    // strip offset
      (277, 3, 1, 1),      // samples per pixel
      (278, 3, 1, 64),     // rows per strip
      (279, 4, 1, 64),     // strip byte count
    };

    w.Write((ushort)entries.Length);
    foreach (var (tag, type, count, value) in entries) {
      w.Write(tag);
      w.Write(type);
      w.Write(count);
      w.Write(value);
    }

    w.Write((uint)0);
    while (ms.Length < 264)
      w.Write((byte)0);

    return ms.ToArray();
  }

  [Test]
  [Category("Unit")]
  public void APrivateCompressionIsRefusedRatherThanDrawnBlank() {
    var thrown = Assert.Catch(() => TiffReader.FromBytes(_PrivateCompressionTiff()));

    Assert.That(thrown, Is.Not.Null, "a TIFF this cannot decode came back as a picture");
  }
}
