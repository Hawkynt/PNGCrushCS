using System;
using System.IO;
using System.Linq;
using FileFormat.Bfli;
using FileFormat.Core;

namespace Hawkynt.FileFormats.Images.Tests.GapClosures;

/// <summary>
/// BFLI, which the library already claimed and drew wrongly: 320 by 200 as a high-resolution screen
/// where the format is 320 by 400 multicolour FLI. These build a file to the layout XnView's own
/// reader was disassembled to want and insist the picture comes back as that layout says, including
/// the two parts of it that look like mistakes and are not — the fixed colours in the three cells the
/// raster switch has not reached, and colour memory wrapping at 1024 rather than at 1000.
/// </summary>
[TestFixture]
public sealed class GapB0BfliTests {

  private const int _Columns = 40;
  private const int _CellHeight = 8;
  private const int _CellCount = 2000;
  private const int _BitmapSize = _CellCount * _CellHeight;
  private const int _ScreenCount = 8;
  private const int _ScreenEntries = _CellCount;
  private const int _ColorRamSize = 1024;
  private const int _MatrixSize = 1000;
  private const int _Split = 24;
  private const int _Padding = 24;
  private const int _BitmapTail = 192;
  private const int _HalfBitmap = _BitmapSize / 2;

  /// <summary>What the four patterns show in the leftmost three cells, where nothing can be stored.</summary>
  private static readonly byte[] _Hidden = [0x00, 0x0F, 0x0F, 0x09];

  /// <summary>
  /// Lays the three sections out in the order the file stores them, which is not address order.
  /// </summary>
  /// <remarks>
  /// Written out here rather than borrowed from the reader so the test states the layout instead of
  /// agreeing with whatever the reader happens to do. Colour memory, the eight matrices of the top
  /// half with their page padding, the top half's bitmap and its page tail, the eight matrices of the
  /// bottom half saved split around their padding, and the bottom half's bitmap with its first 192
  /// bytes held back to the end of the file.
  /// </remarks>
  private static byte[] _File(byte[] bitmap, byte[] screens, byte[] colorRam) {
    var data = new byte[BfliFile.FileSize];
    data[0] = 0xFF;
    data[1] = 0x3B;
    data[2] = (byte)'b';

    var at = BfliFile.HeaderSize;
    void Put(byte[] source, int from, int count) {
      Array.Copy(source, from, data, at, count);
      at += count;
    }

    Put(colorRam, 0, _ColorRamSize);

    for (var bank = 0; bank < _ScreenCount; ++bank) {
      Put(screens, bank * _ScreenEntries, _MatrixSize);
      at += _Padding;
    }

    Put(bitmap, 0, _HalfBitmap);
    at += _BitmapTail;

    for (var bank = 0; bank < _ScreenCount; ++bank) {
      var second = bank * _ScreenEntries + _MatrixSize;
      Put(screens, second + _Split, _MatrixSize - _Split);
      at += _Padding;
      Put(screens, second, _Split);
    }

    Put(bitmap, _HalfBitmap + _BitmapTail, _HalfBitmap - _BitmapTail);
    at += _BitmapTail;
    Put(bitmap, _HalfBitmap, _BitmapTail);

    Assert.That(at, Is.EqualTo(BfliFile.FileSize), "the fixture must account for the file to the byte");
    return data;
  }

  /// <summary>
  /// A picture whose every raster line of every cell carries the four bit patterns in order, so each
  /// of the four places a colour can come from is exercised in every cell of the screen.
  /// </summary>
  private static (byte[] Bitmap, byte[] Screens, byte[] ColorRam) _Sections() {
    var bitmap = new byte[_BitmapSize];
    Array.Fill(bitmap, (byte)0b00_01_10_11);

    var screens = new byte[_ScreenCount * _ScreenEntries];
    for (var bank = 0; bank < _ScreenCount; ++bank)
    for (var cell = 0; cell < _ScreenEntries; ++cell)
      screens[bank * _ScreenEntries + cell] = (byte)(((cell + bank) % 16 << 4) | (cell * 7 + bank) % 16);

    var colorRam = new byte[_ColorRamSize];
    for (var slot = 0; slot < _ColorRamSize; ++slot)
      colorRam[slot] = (byte)((slot * 3 + 1) % 16);

    return (bitmap, screens, colorRam);
  }

  /// <summary>The colour index the layout says a pixel has, worked out from the sections alone.</summary>
  private static int _Expected(byte[] bitmap, byte[] screens, byte[] colorRam, int x, int y) {
    var pair = x / 2;
    var band = y / _CellHeight;
    var cell = band * _Columns + pair / 4;
    var pattern = (bitmap[band * _Columns * _CellHeight + pair / 4 * _CellHeight + y % _CellHeight]
                   >> ((3 - pair % 4) * 2)) & 3;

    if (pair < 12)
      return _Hidden[pattern];

    var entry = screens[y % _ScreenCount * _ScreenEntries + cell];
    return pattern switch {
      0 => 0,
      1 => entry >> 4,
      2 => entry & 0x0F,
      _ => colorRam[cell & (_ColorRamSize - 1)] & 0x0F,
    };
  }

  [Test]
  [Category("Unit")]
  public void ABfliIsFourHundredRowsOfMulticolourAndNotTwoHundredOfHires() {
    var (bitmap, screens, colorRam) = _Sections();
    var image = BfliFile.ToRawImage(BfliReader.FromBytes(_File(bitmap, screens, colorRam)));

    Assert.Multiple(() => {
      Assert.That(image.Width, Is.EqualTo(320));
      Assert.That(image.Height, Is.EqualTo(400));
      Assert.That(image.Format, Is.EqualTo(PixelFormat.Indexed8));
      Assert.That(image.PaletteCount, Is.EqualTo(Commodore64Graphics.ColorCount));
      Assert.That(image.Palette, Is.EqualTo(Commodore64Graphics.CreatePalette()));
    });

    var wrong = 0;
    for (var y = 0; y < 400; ++y)
    for (var x = 0; x < 320; ++x)
      if (image.PixelData[y * 320 + x] != _Expected(bitmap, screens, colorRam, x, y))
        ++wrong;

    Assert.That(wrong, Is.Zero, "pixels drawn from somewhere other than the layout says");
  }

  /// <summary>A stored pixel is two wide, which is what makes the mode multicolour rather than hires.</summary>
  [Test]
  [Category("Unit")]
  public void EveryStoredPixelIsDrawnTwice() {
    var (bitmap, screens, colorRam) = _Sections();
    var image = BfliFile.ToRawImage(BfliReader.FromBytes(_File(bitmap, screens, colorRam)));

    for (var y = 0; y < 400; ++y)
    for (var x = 0; x < 320; x += 2)
      if (image.PixelData[y * 320 + x] != image.PixelData[y * 320 + x + 1])
        Assert.Fail($"columns {x} and {x + 1} of row {y} differ");

    Assert.Pass();
  }

  /// <summary>
  /// The eight video matrices are what FLI buys, and which one speaks is the raster line within the
  /// character cell — so a row eight further down reads a different matrix at the same cell.
  /// </summary>
  [Test]
  [Category("Unit")]
  public void EachRasterLineOfACellReadsItsOwnVideoMatrix() {
    var (bitmap, screens, colorRam) = _Sections();

    // Pattern 01 takes the high nibble of the matrix entry; column 96 is cell 12, past the hidden three.
    for (var bank = 0; bank < _ScreenCount; ++bank)
      screens[bank * _ScreenEntries + 12] = (byte)((bank + 1) << 4);

    var image = BfliFile.ToRawImage(BfliReader.FromBytes(_File(bitmap, screens, colorRam)));

    // Pair 48 of a row is column 96; pattern 01 falls on pairs where pair % 4 == 1, so 49, column 98.
    for (var line = 0; line < _ScreenCount; ++line)
      Assert.That(image.PixelData[line * 320 + 98], Is.EqualTo(line + 1), $"raster line {line}");
  }

  /// <summary>
  /// Colour memory is 1024 bytes and the video chip reaches it with ten address bits, so a picture of
  /// 2000 cells wraps there. Cell 1024 is row 204, and it has to show what cell 0's colour memory
  /// says rather than cell 24's.
  /// </summary>
  [Test]
  [Category("Unit")]
  public void ColourMemoryWrapsAtTenBitsRatherThanAtAThousand() {
    var (bitmap, screens, colorRam) = _Sections();
    colorRam[0] = 0x0B;
    colorRam[24] = 0x02;

    var image = BfliFile.ToRawImage(BfliReader.FromBytes(_File(bitmap, screens, colorRam)));

    // Cell 1024 is band 25 (rows 200..207), column 24 of it: pairs 96..99, and pattern 11 is pair 99.
    Assert.That(image.PixelData[204 * 320 + 99 * 2], Is.EqualTo(0x0B));
  }

  /// <summary>
  /// The leftmost three character cells are drawn before the raster switch can have happened, so
  /// nothing stored in them survives and they come back as the fixed table instead.
  /// </summary>
  [Test]
  [Category("Unit")]
  public void TheThreeCellsTheRasterSwitchCannotReachAreFixed() {
    var (bitmap, screens, colorRam) = _Sections();
    for (var bank = 0; bank < _ScreenCount; ++bank)
    for (var cell = 0; cell < 3; ++cell)
      screens[bank * _ScreenEntries + cell] = 0x45;

    var image = BfliFile.ToRawImage(BfliReader.FromBytes(_File(bitmap, screens, colorRam)));

    // Pairs 0..11 carry patterns 00, 01, 10, 11 repeating, whatever the matrix says.
    for (var pair = 0; pair < 12; ++pair)
      Assert.That(image.PixelData[pair * 2], Is.EqualTo(_Hidden[pair % 4]), $"pair {pair}");
  }

  // -------- the name --------

  private static void _With(byte[] data, string extension, Action<FileInfo> check) {
    var path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + extension);
    File.WriteAllBytes(path, data);
    try {
      check(new FileInfo(path));
    } finally {
      File.Delete(path);
    }
  }

  private static FormatEntry[] _Claiming(string extension) => FormatRegistry.AllFormats
    .Where(entry => entry.AllExtensions?.Any(x => string.Equals(x, extension, StringComparison.OrdinalIgnoreCase)) == true)
    .ToArray();

  /// <summary>A JPEG, which is not a BFLI whatever it is called.</summary>
  private static byte[] _Foreign() {
    byte[] head = [
      0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, (byte)'J', (byte)'F', (byte)'I', (byte)'F',
      0x00, 0x01, 0x01, 0x00, 0x00, 0x01, 0x00, 0x01, 0x00, 0x00
    ];

    return head.Concat(Enumerable.Range(0, 512).Select(i => (byte)(i * 37 % 251))).Concat<byte>([0xFF, 0xD9]).ToArray();
  }

  [Test]
  [Category("Integration")]
  public void TheFlpNameIsClaimedAndReadsABfli() {
    var (bitmap, screens, colorRam) = _Sections();
    Assert.That(_Claiming(".flp"), Is.Not.Empty, "nothing claims .flp");

    _With(_File(bitmap, screens, colorRam), ".flp", file => {
      var image = FormatRegistry.Read(file);
      Assert.That(image, Is.Not.Null);
      Assert.Multiple(() => {
        Assert.That(image!.Width, Is.EqualTo(320));
        Assert.That(image.Height, Is.EqualTo(400));
      });
    });
  }

  [Test]
  [Category("Integration")]
  public void TheNamesItHoldsAllRefuseAForeignFile() {
    foreach (var extension in new[] { ".bfl", ".bfli", ".flp" })
      _With(_Foreign(), extension, file => {
        foreach (var entry in _Claiming(extension))
          Assert.Throws<InvalidDataException>(
            () => entry.LoadRawImageOrThrow!(file), $"{entry.Name} took a JPEG named {extension}");
      });
  }

  /// <summary>
  /// A file of exactly the right length that says nothing about being this format is refused too —
  /// the length on its own is not the signature.
  /// </summary>
  [Test]
  [Category("Unit")]
  public void TheRightLengthWithoutTheRightThreeBytesIsRefused() {
    var (bitmap, screens, colorRam) = _Sections();

    var noMarker = _File(bitmap, screens, colorRam);
    noMarker[2] = (byte)'a';
    Assert.Throws<InvalidDataException>(() => BfliReader.FromBytes(noMarker));

    var otherAddress = _File(bitmap, screens, colorRam);
    otherAddress[1] = 0x3C;
    Assert.Throws<InvalidDataException>(() => BfliReader.FromBytes(otherAddress));

    Assert.Throws<InvalidDataException>(() => BfliReader.FromBytes(new byte[BfliFile.FileSize]));
  }

  /// <summary>Any other length is refused, which is what RECOIL asks of the format as well.</summary>
  [Test]
  [Category("Unit")]
  public void OnlyTheOneLengthIsAccepted() {
    var (bitmap, screens, colorRam) = _Sections();
    var whole = _File(bitmap, screens, colorRam);

    Assert.Multiple(() => {
      Assert.DoesNotThrow(() => BfliReader.FromBytes(whole));
      Assert.Throws<InvalidDataException>(() => BfliReader.FromBytes(whole[..^1]));
      Assert.Throws<InvalidDataException>(() => BfliReader.FromBytes(whole.Concat<byte>([0]).ToArray()));
    });
  }
}
