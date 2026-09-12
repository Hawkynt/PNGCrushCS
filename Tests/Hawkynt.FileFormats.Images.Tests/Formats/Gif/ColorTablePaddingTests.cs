using System;
using System.IO;
using System.Linq;
using FileFormat.Gif;

namespace FileFormat.Gif.Tests;

/// <summary>GIF stores a colour table's size as a 3-bit exponent, never a length, so a table whose
/// entry count is not a power of two has to be padded out on write. Emitting the short table makes
/// every byte after it land at the wrong offset, and the file is unreadable from the image descriptor
/// on. These cover that padding, in the global table and the local one.</summary>
[TestFixture]
public sealed class ColorTablePaddingTests {

  private static GifFile _Build(byte[] globalColorTable, byte[]? localColorTable, byte[] pixels, ushort width, ushort height) => new() {
    Version = GifVersion.Gif89a,
    LogicalScreenDescriptor = new GifLogicalScreenDescriptor(
      Width: width, Height: height,
      HasGlobalColorTable: globalColorTable.Length > 0,
      ColorResolution: 8, GlobalColorTableSorted: false,
      GlobalColorTableSize: 0, BackgroundColorIndex: 0, PixelAspectRatio: 0),
    GlobalColorTable = globalColorTable.Length > 0 ? globalColorTable : null,
    LoopCount = LoopCount.PlayOnce,
    Frames = [new Frame {
      Left = 0, Top = 0, Width = width, Height = height,
      LocalColorTable = localColorTable,
      PixelData = pixels,
    }],
  };

  [Test]
  public void GlobalTable_ThreeColors_IsPaddedToFourEntries() {
    // 3 colours: the smallest exponent that holds them is 1, meaning 4 entries = 12 bytes.
    var gct = new byte[] { 0xFF, 0, 0, 0, 0xFF, 0, 0, 0, 0xFF };
    var bytes = GifWriter.ToBytes(_Build(gct, null, [0, 1, 2, 1], 2, 2));

    var packed = bytes[10];
    Assert.That((packed & 0x80), Is.Not.Zero, "global colour table flag must be set");
    Assert.That((packed & 0x07), Is.EqualTo(1), "size exponent 1 => 4 entries");

    // 6 signature + 7 LSD = 13, then the table.
    Assert.That(bytes.Length, Is.GreaterThan(13 + 12));
    Assert.That(bytes.Skip(13).Take(9).ToArray(), Is.EqualTo(gct), "the three real colours come first");
    Assert.That(bytes.Skip(13 + 9).Take(3).ToArray(), Is.EqualTo(new byte[] { 0, 0, 0 }), "the fourth entry is padding");
    Assert.That(bytes[13 + 12], Is.EqualTo(0x2C), "the image descriptor must start right after a 12-byte table");
  }

  [Test]
  public void GlobalTable_NonPowerOfTwo_RoundTripsPixels() {
    var gct = new byte[] { 10, 20, 30, 40, 50, 60, 70, 80, 90, 100, 110, 120, 130, 140, 150 }; // 5 colours
    var pixels = new byte[] { 0, 1, 2, 3, 4, 0, 1, 2, 3 };
    var src = _Build(gct, null, pixels, 3, 3);

    var reread = GifReader.FromBytes(GifWriter.ToBytes(src));

    Assert.That(reread.Frames, Has.Count.EqualTo(1));
    Assert.That(reread.Frames[0].PixelData, Is.EqualTo(pixels));
    Assert.That(reread.LogicalScreenDescriptor.GlobalColorTableEntryCount, Is.EqualTo(8), "5 colours round up to 8 entries");
    Assert.That(reread.GlobalColorTable!.Take(15).ToArray(), Is.EqualTo(gct));
    Assert.That(reread.GlobalColorTable!.Length, Is.EqualTo(24));
  }

  [Test]
  public void LocalTable_NonPowerOfTwo_RoundTripsPixels() {
    var lct = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9 }; // 3 colours -> 4 entries
    var pixels = new byte[] { 0, 1, 2, 0 };
    var src = _Build([], lct, pixels, 2, 2);

    var reread = GifReader.FromBytes(GifWriter.ToBytes(src));

    Assert.That(reread.Frames, Has.Count.EqualTo(1));
    Assert.That(reread.Frames[0].LocalColorTableEntryCount, Is.EqualTo(4));
    Assert.That(reread.Frames[0].LocalColorTable!.Take(9).ToArray(), Is.EqualTo(lct));
    Assert.That(reread.Frames[0].PixelData, Is.EqualTo(pixels));
  }

  [Test]
  public void LogicalScreenDescriptor_ClaimingATableItDoesNotHave_DropsTheFlag() {
    // An LSD that advertises a global table with no bytes behind it used to emit the flag anyway,
    // and the reader then consumed frame bytes as palette entries.
    var src = new GifFile {
      Version = GifVersion.Gif89a,
      LogicalScreenDescriptor = new GifLogicalScreenDescriptor(
        Width: 2, Height: 2, HasGlobalColorTable: true, ColorResolution: 8,
        GlobalColorTableSorted: false, GlobalColorTableSize: 3, BackgroundColorIndex: 0, PixelAspectRatio: 0),
      GlobalColorTable = null,
      LoopCount = LoopCount.PlayOnce,
      Frames = [new Frame {
        Left = 0, Top = 0, Width = 2, Height = 2,
        LocalColorTable = [0, 0, 0, 255, 255, 255],
        PixelData = [0, 1, 1, 0],
      }],
    };

    var reread = GifReader.FromBytes(GifWriter.ToBytes(src));

    Assert.That(reread.LogicalScreenDescriptor.HasGlobalColorTable, Is.False);
    Assert.That(reread.Frames, Has.Count.EqualTo(1));
    Assert.That(reread.Frames[0].PixelData, Is.EqualTo(new byte[] { 0, 1, 1, 0 }));
  }

  [Test]
  public void FrameIndexingPastItsColorTable_StillRoundTrips() {
    // Malformed but common: a 4-entry table with pixels reaching index 7. Encoding at the table's
    // width would turn 4 and 5 into the clear and end-of-information codes.
    var lct = new byte[] { 0, 0, 0, 1, 1, 1, 2, 2, 2, 3, 3, 3 }; // 4 entries
    var pixels = new byte[] { 0, 1, 2, 3, 4, 5, 6, 7, 0 };
    var src = _Build([], lct, pixels, 3, 3);

    var reread = GifReader.FromBytes(GifWriter.ToBytes(src));

    Assert.That(reread.Frames[0].PixelData, Is.EqualTo(pixels));
  }
}
