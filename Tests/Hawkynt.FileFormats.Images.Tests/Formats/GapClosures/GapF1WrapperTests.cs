using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using FileFormat.CartesMichelin;
using FileFormat.Core;
using FileFormat.Gif;
using FileFormat.HalfLifeModel;
using FileFormat.PicturePublisher4;
using FileFormat.Prisms;
using FileFormat.Tiff;

namespace FileFormat.GapClosures.Tests;

/// <summary>
/// Four formats settled against XnView's own converter: Prisms (.pri, which the converter also reads
/// as LucasFilm's .lff), the Half-Life model's skins (.mdl), Micrografx Picture Publisher 4 (.pp4)
/// and the Cartes Michelin road atlas (.big). The first two are read out of the file directly; the
/// last two turn out to be wrappers, one round a TIFF and one round a grid of GIFs, and were
/// identified by dropping candidate payloads in and seeing which the converter took.
/// </summary>
[TestFixture]
public sealed class GapF1WrapperTests {

  // -------- Prisms --------

  private static byte[] _PrismsHeader(int width, int height, int dataOffset) {
    var header = new byte[dataOffset];
    PrismsFile.Signature.CopyTo(header);
    PrismsFile.Layout.CopyTo(header.AsSpan(PrismsFile.LayoutOffset));
    BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(PrismsFile.HeightOffset), (ushort)height);
    BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(PrismsFile.WidthOffset), (ushort)width);
    BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(PrismsFile.DataPointerOffset), (ushort)dataOffset);
    return header;
  }

  private static byte[] _Prisms(int width, int height, IEnumerable<byte> stream, int dataOffset = 0x400) {
    var header = _PrismsHeader(width, height, dataOffset);
    var body = new List<byte>(stream);
    var file = new byte[header.Length + body.Count];
    header.CopyTo(file, 0);
    body.CopyTo(file, header.Length);
    return file;
  }

  /// <summary>The four bytes one pixel takes; only the last three are drawn, and in that order.</summary>
  private static byte[] _Pixel(int index) => [(byte)(index * 3), (byte)(index * 11 + 7), (byte)(index * 5 + 3), (byte)(index * 17 + 1)];

  private static void _AssertPrisms(PrismsFile file, int width, int height, Func<int, int, int> indexAt) {
    Assert.Multiple(() => {
      Assert.That(file.Width, Is.EqualTo(width));
      Assert.That(file.Height, Is.EqualTo(height));

      for (var y = 0; y < height; ++y)
      for (var x = 0; x < width; ++x) {
        var pixel = _Pixel(indexAt(x, y));
        var at = (y * width + x) * 3;
        Assert.That(file.PixelData[at], Is.EqualTo(pixel[3]), $"red at {x},{y}");
        Assert.That(file.PixelData[at + 1], Is.EqualTo(pixel[2]), $"green at {x},{y}");
        Assert.That(file.PixelData[at + 2], Is.EqualTo(pixel[1]), $"blue at {x},{y}");
      }
    });
  }

  [Test]
  [Category("Unit")]
  public void Prisms_ReadsLiteralRunsFromTheBottomRowUpwards() {
    const int width = 6, height = 3;
    var stream = new List<byte>();
    for (var y = 0; y < height; ++y) {
      stream.Add(width - 1);
      stream.Add(PrismsFile.OpcodeLiteral);
      for (var x = 0; x < width; ++x)
        stream.AddRange(_Pixel(y * width + x));
    }

    // The rows go down bottom first, so the row written last is the one at the top.
    _AssertPrisms(PrismsReader.FromBytes(_Prisms(width, height, stream)), width, height,
      (x, y) => (height - 1 - y) * width + x);
  }

  [Test]
  [Category("Unit")]
  public void Prisms_ReadsRunLengthGroupsAndIgnoresAnOpcodeItDoesNotKnow() {
    const int width = 6, height = 2;
    var stream = new List<byte>();
    for (var y = 0; y < height; ++y) {
      stream.Add(0x30);           // a command the converter reads and does nothing with
      stream.Add(0x40);
      stream.Add(1);              // two run-length groups
      stream.Add(PrismsFile.OpcodeRuns);
      stream.Add(width / 2 - 1);
      stream.AddRange(_Pixel(y * 2));
      stream.Add(width / 2 - 1);
      stream.AddRange(_Pixel(y * 2 + 1));
    }

    _AssertPrisms(PrismsReader.FromBytes(_Prisms(width, height, stream)), width, height,
      (x, y) => (height - 1 - y) * 2 + (x < width / 2 ? 0 : 1));
  }

  [Test]
  [Category("Unit")]
  public void Prisms_RefusesAFileWithoutTheLayoutStringAtEightySix() {
    var stream = new List<byte> { 0, PrismsFile.OpcodeLiteral };
    stream.AddRange(_Pixel(0));
    var file = _Prisms(1, 1, stream);
    file[PrismsFile.LayoutOffset] = (byte)'X';

    Assert.Throws<InvalidDataException>(() => PrismsReader.FromBytes(file));
  }

  [Test]
  [Category("Unit")]
  public void Prisms_ClaimsTheLucasFilmExtensionTheConverterReadsWithThisSameLoader()
    => Assert.That(_ExtensionsOf<PrismsFile>(), Does.Contain(".lff"));

  private static string[] _ExtensionsOf<T>() where T : IImageFormatMetadata<T> => T.FileExtensions;

  // -------- Half-Life model --------

  private static byte[] _Model(params (string Name, int Width, int Height)[] skins) {
    const int tableAt = 0x100;
    var dataAt = tableAt + skins.Length * HalfLifeModelFile.TextureEntrySize;
    var table = new byte[skins.Length * HalfLifeModelFile.TextureEntrySize];
    var blobs = new List<byte>();

    for (var i = 0; i < skins.Length; ++i) {
      var (name, width, height) = skins[i];
      var entry = table.AsSpan(i * HalfLifeModelFile.TextureEntrySize);
      for (var c = 0; c < name.Length; ++c)
        entry[c] = (byte)name[c];

      BinaryPrimitives.WriteInt32LittleEndian(entry[64..], 0);
      BinaryPrimitives.WriteInt32LittleEndian(entry[68..], width);
      BinaryPrimitives.WriteInt32LittleEndian(entry[72..], height);
      BinaryPrimitives.WriteInt32LittleEndian(entry[76..], dataAt + blobs.Count);

      for (var p = 0; p < width * height; ++p)
        blobs.Add((byte)((p * 5 + i * 31) % 256));

      for (var c = 0; c < HalfLifeModelFile.PaletteEntries; ++c)
        blobs.AddRange([(byte)c, (byte)((c * 7 + i) & 0xFF), (byte)((c * 13 + i * 2) & 0xFF)]);
    }

    var header = new byte[tableAt];
    HalfLifeModelFile.Signature.CopyTo(header);
    BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(4), HalfLifeModelFile.Version);
    BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(HalfLifeModelFile.TextureCountOffset), skins.Length);
    BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(HalfLifeModelFile.TextureIndexOffset), tableAt);
    BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(HalfLifeModelFile.TextureDataOffset), dataAt);

    var file = new byte[tableAt + table.Length + blobs.Count];
    header.CopyTo(file, 0);
    table.CopyTo(file, tableAt);
    blobs.CopyTo(file, dataAt);
    return file;
  }

  [Test]
  [Category("Unit")]
  public void HalfLifeModel_ReadsTheSkinTableAtTheOffsetStudioHdrPutsIt() {
    var file = HalfLifeModelReader.FromBytes(_Model(("skinA", 8, 5), ("skinB", 4, 3)));

    Assert.Multiple(() => {
      Assert.That(file.SkinCount, Is.EqualTo(2));
      // The converter draws the last skin, so this reads the last skin.
      Assert.That(file.Name, Is.EqualTo("skinB"));
      Assert.That(file.Width, Is.EqualTo(4));
      Assert.That(file.Height, Is.EqualTo(3));
    });

    var image = HalfLifeModelFile.ToRawImage(file);
    Assert.Multiple(() => {
      Assert.That(image.PaletteCount, Is.EqualTo(256));
      for (var p = 0; p < 12; ++p)
        Assert.That(image.PixelData[p], Is.EqualTo((byte)((p * 5 + 31) % 256)), $"index {p}");
    });
  }

  [Test]
  [Category("Unit")]
  public void HalfLifeModel_ReadsAnEarlierSkinWhenAskedFor() {
    var bytes = _Model(("skinA", 8, 5), ("skinB", 4, 3));
    var first = HalfLifeModelReader.FromSpan(bytes, 0);

    Assert.Multiple(() => {
      Assert.That(first.Name, Is.EqualTo("skinA"));
      Assert.That(first.Width, Is.EqualTo(8));
      Assert.That(first.Height, Is.EqualTo(5));
    });
  }

  [Test]
  [Category("Unit")]
  public void HalfLifeModel_RefusesAModelWithTheFieldsWhereTheyWereThoughtToBe() {
    var bytes = _Model(("skinA", 8, 5));

    // Move the three fields from 0xB4 back to 0xAC, which is where they were believed to be. The
    // converter refuses that file, and so does this.
    Array.Copy(bytes, HalfLifeModelFile.TextureCountOffset, bytes, 0xAC, 12);
    Array.Clear(bytes, HalfLifeModelFile.TextureCountOffset, 12);

    Assert.Throws<InvalidDataException>(() => HalfLifeModelReader.FromBytes(bytes));
  }

  [Test]
  [Category("Unit")]
  public void HalfLifeModel_RefusesSomethingThatIsNotAModel() {
    var bytes = _Model(("skinA", 8, 5));
    bytes[1] = (byte)'X';

    Assert.Throws<InvalidDataException>(() => HalfLifeModelReader.FromBytes(bytes));
  }

  // -------- Picture Publisher 4 --------

  private static byte[] _Rgb(int width, int height) {
    var pixels = new byte[width * height * 3];
    for (var i = 0; i < width * height; ++i) {
      pixels[i * 3] = (byte)(i * 9 % 256);
      pixels[i * 3 + 1] = (byte)(i * 5 % 256);
      pixels[i * 3 + 2] = (byte)(i * 3 % 256);
    }

    return pixels;
  }

  private static byte[] _PicturePublisher4(byte[] payload, int at = 0x100) {
    var file = new byte[at + payload.Length];
    PicturePublisher4File.Signature.CopyTo(file.AsSpan(0));
    BinaryPrimitives.WriteInt32LittleEndian(file.AsSpan(PicturePublisher4File.PointerOffset), at);
    payload.CopyTo(file, at);
    return file;
  }

  [Test]
  [Category("Unit")]
  public void PicturePublisher4_ReadsTheTiffTheOffsetAtTwoAPointsAt() {
    const int width = 7, height = 4;
    var source = new RawImage { Width = width, Height = height, Format = PixelFormat.Rgb24, PixelData = _Rgb(width, height) };
    var tiff = TiffWriter.ToBytes(TiffFile.FromRawImage(source));

    var file = PicturePublisher4Reader.FromBytes(_PicturePublisher4(tiff));
    var image = PicturePublisher4File.ToRawImage(file);

    Assert.Multiple(() => {
      Assert.That(file.PictureOffset, Is.EqualTo(0x100));
      Assert.That(image.Width, Is.EqualTo(width));
      Assert.That(image.Height, Is.EqualTo(height));
      Assert.That(image.EnsureFormat(PixelFormat.Rgb24).PixelData, Is.EqualTo(source.PixelData));
    });
  }

  [Test]
  [Category("Unit")]
  public void PicturePublisher4_RefusesAFileThatOpensIiWithNoTiffBehindTheOffset() {
    var junk = new byte[64];
    for (var i = 0; i < junk.Length; ++i)
      junk[i] = (byte)i;

    Assert.Throws<InvalidDataException>(() => PicturePublisher4Reader.FromBytes(_PicturePublisher4(junk)));
  }

  // -------- Cartes Michelin --------

  private const int _TileWidth = 32, _TileHeight = 32;

  private static byte[] _TileGif(int tag) {
    var pixels = new byte[_TileWidth * _TileHeight * 3];
    for (var y = 0; y < _TileHeight; ++y)
    for (var x = 0; x < _TileWidth; ++x) {
      var lit = (x / (tag + 1) + y / 2 + tag) % 2 == 0;
      var at = (y * _TileWidth + x) * 3;
      pixels[at] = pixels[at + 1] = pixels[at + 2] = lit ? (byte)0 : (byte)255;
    }

    var image = new RawImage {
      Width = _TileWidth, Height = _TileHeight, Format = PixelFormat.Rgb24, PixelData = pixels,
    };

    return GifWriter.ToBytes(GifFile.FromRawImage(image));
  }

  private static byte[] _Michelin(int across, int down, Dictionary<(int Row, int Column), int> present) {
    var directory = across * down * CartesMichelinFile.DirectoryEntrySize;
    var blobs = new List<byte>();
    var placed = new Dictionary<(int, int), (int Offset, int Length)>();

    foreach (var ((row, column), tag) in present) {
      var gif = _TileGif(tag);
      placed[(row, column)] = (CartesMichelinFile.HeaderSize + directory + blobs.Count, gif.Length);
      blobs.AddRange(gif);
    }

    var file = new byte[CartesMichelinFile.HeaderSize + directory + blobs.Count];
    BinaryPrimitives.WriteInt32LittleEndian(file.AsSpan(0), _TileWidth);
    BinaryPrimitives.WriteInt32LittleEndian(file.AsSpan(4), _TileHeight);
    BinaryPrimitives.WriteInt32LittleEndian(file.AsSpan(8), across);
    BinaryPrimitives.WriteInt32LittleEndian(file.AsSpan(12), down);

    for (var row = 0; row < down; ++row)
    for (var column = 0; column < across; ++column) {
      var at = CartesMichelinFile.HeaderSize + (row * across + column) * CartesMichelinFile.DirectoryEntrySize;
      var (offset, length) = placed.TryGetValue((row, column), out var entry) ? entry : (0, 0);
      BinaryPrimitives.WriteInt32LittleEndian(file.AsSpan(at), offset);
      BinaryPrimitives.WriteInt32LittleEndian(file.AsSpan(at + 4), length);
    }

    blobs.CopyTo(file, CartesMichelinFile.HeaderSize + directory);
    return file;
  }

  [Test]
  [Category("Unit")]
  public void CartesMichelin_LaysTheGifTilesOutInTheGridTheDirectoryGives() {
    var present = new Dictionary<(int, int), int> {
      [(0, 0)] = 0, [(0, 1)] = 1, [(0, 2)] = 2,
      [(1, 0)] = 3, [(1, 1)] = 4, [(1, 2)] = 5,
    };

    var file = CartesMichelinReader.FromBytes(_Michelin(3, 2, present));

    Assert.Multiple(() => {
      Assert.That(file.Width, Is.EqualTo(_TileWidth * 3));
      Assert.That(file.Height, Is.EqualTo(_TileHeight * 2));
      Assert.That(file.TileCount, Is.EqualTo(6));

      foreach (var ((row, column), tag) in present)
      for (var y = 0; y < _TileHeight; ++y)
      for (var x = 0; x < _TileWidth; ++x) {
        var lit = (x / (tag + 1) + y / 2 + tag) % 2 == 0;
        var at = ((row * _TileHeight + y) * file.Width + column * _TileWidth + x) * 3;
        Assert.That(file.PixelData[at], Is.EqualTo(lit ? (byte)0 : (byte)255), $"tile {row},{column} pixel {x},{y}");
      }
    });
  }

  [Test]
  [Category("Unit")]
  public void CartesMichelin_SizesTheSheetToTheOccupiedPartOfTheGrid() {
    var file = CartesMichelinReader.FromBytes(_Michelin(3, 2, new() { [(1, 2)] = 1 }));

    Assert.Multiple(() => {
      Assert.That(file.Width, Is.EqualTo(_TileWidth));
      Assert.That(file.Height, Is.EqualTo(_TileHeight));
      Assert.That(file.TileCount, Is.EqualTo(1));
    });
  }

  [Test]
  [Category("Unit")]
  public void CartesMichelin_DrawsTheOccupiedBoxAtTheOriginRatherThanAtItsGridPosition() {
    // Columns one and two of the second row, so the sheet is two tiles wide and the tile from
    // column one is the left-hand one — not an empty column with the pair pushed right.
    var present = new Dictionary<(int, int), int> { [(1, 1)] = 0, [(1, 2)] = 3 };
    var file = CartesMichelinReader.FromBytes(_Michelin(3, 2, present));

    Assert.Multiple(() => {
      Assert.That(file.Width, Is.EqualTo(_TileWidth * 2));
      Assert.That(file.Height, Is.EqualTo(_TileHeight));

      var slot = 0;
      foreach (var tag in new[] { 0, 3 }) {
        for (var y = 0; y < _TileHeight; ++y)
        for (var x = 0; x < _TileWidth; ++x) {
          var lit = (x / (tag + 1) + y / 2 + tag) % 2 == 0;
          var at = (y * file.Width + slot * _TileWidth + x) * 3;
          Assert.That(file.PixelData[at], Is.EqualTo(lit ? (byte)0 : (byte)255), $"slot {slot} pixel {x},{y}");
        }

        ++slot;
      }
    });
  }

  [Test]
  [Category("Unit")]
  public void CartesMichelin_RefusesAFileWhoseFourNumbersAreInRangeButCarriesNoTile() {
    // This is what makes the name claimable: without the GIF signature at a tile, four numbers in
    // range would draw anything at all.
    var file = _Michelin(3, 2, []);

    Assert.Throws<InvalidDataException>(() => CartesMichelinReader.FromBytes(file));
  }

  [Test]
  [Category("Unit")]
  public void CartesMichelin_RefusesATileSizeOutsideTheRangeTheFormatAllows() {
    var file = _Michelin(3, 2, new() { [(0, 0)] = 0 });
    BinaryPrimitives.WriteInt32LittleEndian(file.AsSpan(0), 16);

    Assert.Throws<InvalidDataException>(() => CartesMichelinReader.FromBytes(file));
  }
}
