using System;
using System.Buffers.Binary;
using System.Text;
using FileFormat.Core;

namespace FileFormat.MayaIff.Tests;

/// <summary>
/// Maya IFF: a form holding a version, a header, and a second form holding the tiles.
/// </summary>
/// <remarks>
/// The picture is stored as 64-pixel tiles, and each states its own corners before its pixels. What
/// was written here before was the header and then one chunk of the whole picture — no version, no
/// nested form, no corners — so a reader took the first four samples as a tile's corners and went
/// looking for memory for a tile 65535 square.
/// <para/>
/// The structure below was taken from a file Maya itself wrote. What a tile holds between its
/// corners and its end used to be unsettled and is not any more: it is the channels named backwards
/// for however many the header's flags say, the rows from the bottom of the tile upwards, and either
/// interleaved at full length or one run-length coded plane per channel. That was settled against
/// files XnView's converter wrote from pictures of this project's making, and the three of them —
/// colour, colour with alpha, and one flat enough to be compressed — come back pixel for pixel.
/// </remarks>
[TestFixture]
public sealed class MayaIffTests {

  private static RawImage _Picture(int width, int height) {
    var pixels = new byte[width * height * 3];
    for (var y = 0; y < height; ++y)
    for (var x = 0; x < width; ++x) {
      var at = (y * width + x) * 3;
      pixels[at] = (byte)(x * 255 / Math.Max(1, width - 1));
      pixels[at + 1] = (byte)(y * 255 / Math.Max(1, height - 1));
      pixels[at + 2] = (byte)((x / 8 + y / 8) % 2 == 0 ? 255 : 0);
    }

    return new() { Width = width, Height = height, Format = PixelFormat.Rgb24, PixelData = pixels };
  }

  private static int _Find(byte[] data, string tag) {
    var wanted = Encoding.ASCII.GetBytes(tag);
    for (var i = 0; i + wanted.Length <= data.Length; ++i) {
      var match = true;
      for (var j = 0; j < wanted.Length; ++j)
        if (data[i + j] != wanted[j]) {
          match = false;
          break;
        }

      if (match)
        return i;
    }

    return -1;
  }

  [Test]
  [Category("Unit")]
  public void Written_HasTheFormsAndChunksTheFormatUses() {
    var bytes = MayaIffWriter.ToBytes(MayaIffFile.FromRawImage(_Picture(320, 200)));

    Assert.Multiple(() => {
      Assert.That(Encoding.ASCII.GetString(bytes, 0, 4), Is.EqualTo("FOR4"));
      Assert.That(Encoding.ASCII.GetString(bytes, 8, 4), Is.EqualTo("CIMG"));
      Assert.That(_Find(bytes, "FVER"), Is.GreaterThan(0), "the version chunk");
      Assert.That(_Find(bytes, "TBHD"), Is.GreaterThan(0), "the header");
      Assert.That(_Find(bytes, "TBMP"), Is.GreaterThan(0), "the nested form the tiles live in");
    });
  }

  [Test]
  [Category("Unit")]
  public void Written_CutsThePictureIntoTilesAndNamesTheirCorners() {
    var bytes = MayaIffWriter.ToBytes(MayaIffFile.FromRawImage(_Picture(320, 200)));
    var tbhd = _Find(bytes, "TBHD");

    // Five tiles across and four down for a 320 by 200 picture at sixty-four a side.
    // Within the header: width and height take four each, the pixel ratio two each, the flags four,
    // then the byte depth and the tile count two each.
    var tiles = BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(tbhd + 8 + 18));
    Assert.That(tiles, Is.EqualTo(5 * 4));

    var first = _Find(bytes, "TBMP") + 4;
    Assert.Multiple(() => {
      Assert.That(BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(first + 8)), Is.EqualTo(0), "left");
      Assert.That(BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(first + 10)), Is.EqualTo(0), "top");
      Assert.That(BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(first + 12)), Is.EqualTo(63), "right");
      Assert.That(BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(first + 14)), Is.EqualTo(63), "bottom");
    });
  }

  [Test]
  [Category("Unit")]
  public void Written_SaysOneByteAChannel() {
    var bytes = MayaIffWriter.ToBytes(MayaIffFile.FromRawImage(_Picture(64, 64)));
    var tbhd = _Find(bytes, "TBHD");

    // Saying two would send a reader looking for twice the data there is.
    Assert.That(BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(tbhd + 8 + 16)), Is.EqualTo(0));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_PutsEveryTileBackWhereItCameFrom() {
    // A picture wider than one tile is the case that catches a reader laying tiles out in the order
    // it meets them rather than at the corners each one names.
    var original = _Picture(200, 100);
    var restored = MayaIffReader.FromBytes(MayaIffWriter.ToBytes(MayaIffFile.FromRawImage(original)));

    Assert.That(restored.Width, Is.EqualTo(200));
    Assert.That(restored.Height, Is.EqualTo(100));

    var image = MayaIffFile.ToRawImage(restored);
    var rgb = PixelConverter.Convert(image, PixelFormat.Rgb24);
    Assert.That(rgb.PixelData, Is.EqualTo(original.PixelData));
  }

  /// <summary>
  /// A file as another program writes one: a 24-byte header, and a tile whose channels are named
  /// backwards, whose rows run upwards, and whose planes are run-length coded one after another.
  /// </summary>
  private static byte[] _AsAnotherProgramWritesOne(int width, int height, uint flags, Func<int, int, int, byte> sample) {
    var channels = (flags & MayaIffTbhdHeader.RgbFlag) != 0 ? 3 : 0;
    if ((flags & MayaIffTbhdHeader.AlphaFlag) != 0)
      ++channels;

    var payload = new System.IO.MemoryStream();
    for (var plane = 0; plane < channels; ++plane) {
      var channel = channels - 1 - plane;
      for (var row = 0; row < height; ++row) {
        var y = height - 1 - row;
        for (var x = 0; x < width; ++x) {
          // One literal byte at a time: the shape the coding takes does not matter to the reader,
          // only that it accounts for the plane exactly.
          payload.WriteByte(0);
          payload.WriteByte(sample(x, y, channel));
        }
      }
    }

    var tile = payload.ToArray();

    // The nested form holds its own type and the tile chunk: four bytes, then eight of chunk header,
    // then the eight the corners take, then the coding.
    var nested = 4 + 8 + 8 + tile.Length;
    var body = 4 + 12 + (8 + MayaIffTbhdHeader.StructSize) + (8 + nested);
    var file = new byte[8 + body];
    var span = file.AsSpan();
    var at = 0;

    Encoding.ASCII.GetBytes("FOR4").CopyTo(file, at);
    BinaryPrimitives.WriteUInt32BigEndian(span[(at + 4)..], (uint)body);
    Encoding.ASCII.GetBytes("CIMG").CopyTo(file, at + 8);
    at += 12;

    Encoding.ASCII.GetBytes("FVER").CopyTo(file, at);
    BinaryPrimitives.WriteUInt32BigEndian(span[(at + 4)..], 4);
    at += 12;

    Encoding.ASCII.GetBytes("TBHD").CopyTo(file, at);
    BinaryPrimitives.WriteUInt32BigEndian(span[(at + 4)..], MayaIffTbhdHeader.StructSize);
    at += 8;
    new MayaIffTbhdHeader((uint)width, (uint)height, 1, 1, flags, 0, 1, 1).WriteTo(span[at..]);
    at += MayaIffTbhdHeader.StructSize;

    Encoding.ASCII.GetBytes("FOR4").CopyTo(file, at);
    BinaryPrimitives.WriteUInt32BigEndian(span[(at + 4)..], (uint)nested);
    Encoding.ASCII.GetBytes("TBMP").CopyTo(file, at + 8);
    at += 12;

    Encoding.ASCII.GetBytes("RGBA").CopyTo(file, at);
    BinaryPrimitives.WriteUInt32BigEndian(span[(at + 4)..], (uint)(8 + tile.Length));
    BinaryPrimitives.WriteUInt16BigEndian(span[(at + 8)..], 0);
    BinaryPrimitives.WriteUInt16BigEndian(span[(at + 10)..], 0);
    BinaryPrimitives.WriteUInt16BigEndian(span[(at + 12)..], (ushort)(width - 1));
    BinaryPrimitives.WriteUInt16BigEndian(span[(at + 14)..], (ushort)(height - 1));
    tile.CopyTo(file, at + 16);

    return file;
  }

  [Test]
  [Category("Integration")]
  public void FromBytes_ATileAsTheFormatStoresOne_ComesBackTheRightWayUpAndTheRightWayRound() {
    // Red rises with x, green with y, blue is constant: a picture that is wrong in a different way
    // for each of upside down, planes swapped, and channels reversed.
    byte Sample(int x, int y, int channel) => channel switch { 0 => (byte)(x * 3), 1 => (byte)(y * 3), _ => 200 };

    var file = MayaIffReader.FromBytes(_AsAnotherProgramWritesOne(16, 12, MayaIffTbhdHeader.RgbFlag, Sample));

    Assert.Multiple(() => {
      Assert.That(file.Width, Is.EqualTo(16));
      Assert.That(file.Height, Is.EqualTo(12));
      Assert.That(file.HasAlpha, Is.False, "the flags say three planes, whatever the chunk is called");
    });

    for (var y = 0; y < 12; ++y)
    for (var x = 0; x < 16; ++x) {
      var at = (y * 16 + x) * 3;
      Assert.That(new[] { file.PixelData[at], file.PixelData[at + 1], file.PixelData[at + 2] },
        Is.EqualTo(new[] { (byte)(x * 3), (byte)(y * 3), (byte)200 }), $"pixel {x},{y}");
    }
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TheFlagsSayHowManyPlanesThereAreAndTheChunksNameDoesNot() {
    byte Sample(int x, int y, int channel) => (byte)(channel * 40 + x);

    var file = MayaIffReader.FromBytes(
      _AsAnotherProgramWritesOne(8, 8, MayaIffTbhdHeader.RgbFlag | MayaIffTbhdHeader.AlphaFlag, Sample));

    Assert.That(file.HasAlpha, Is.True);
    Assert.That(file.PixelData[3], Is.EqualTo(120), "the alpha of the first pixel");
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ATileCodingMoreThanItsOwnChunk_IsRefused() {
    byte Sample(int x, int y, int channel) => (byte)(x + y + channel);
    var data = _AsAnotherProgramWritesOne(8, 8, MayaIffTbhdHeader.RgbFlag, Sample);

    // Turn one literal byte into a run of 128, which codes past the end of the plane.
    var tile = _Find(data, "RGBA") + 16;
    data[tile] = 0xFF;

    Assert.Throws<System.IO.InvalidDataException>(() => MayaIffReader.FromBytes(data));
  }

  /// <summary>
  /// The name <c>.tdi</c> is claimed as well as <c>.iff</c>, and neither of them decides anything.
  /// </summary>
  [Test]
  [Category("Unit")]
  public void FromBytes_SomethingElseUnderOneOfTheNamesThisClaims_IsRefused() {
    // An Amiga IFF bitmap, which is the other thing a file called .iff is, and a JPEG, which is
    // what a stray .tdi is as likely to be as anything.
    var amiga = Encoding.ASCII.GetBytes("FORM\0\0\0\x40ILBMBMHD\0\0\0\x14");
    var jpeg = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0, 16, (byte)'J', (byte)'F', (byte)'I', (byte)'F', 0 };

    Assert.Multiple(() => {
      Assert.Throws<System.IO.InvalidDataException>(() => MayaIffReader.FromBytes(_Padded(amiga)));
      Assert.Throws<System.IO.InvalidDataException>(() => MayaIffReader.FromBytes(_Padded(jpeg)));
      Assert.Throws<System.IO.InvalidDataException>(() => MayaIffReader.FromBytes(new byte[512]));
    });
  }

  private static byte[] _Padded(byte[] head) {
    var data = new byte[512];
    head.CopyTo(data, 0);
    return data;
  }
}
