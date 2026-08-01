using System;
using System.Buffers.Binary;
using FileFormat.Core;
using FileFormat.Viff;

namespace FileFormat.Viff.Tests;

/// <summary>
/// Decodes VIFF files laid out the way every other tool writes them, rather than the way this
/// library happens to.
/// </summary>
/// <remarks>
/// The round-trip tests could not see the bug these cover: the writer put the band count and the
/// storage type in the same two wrong places the reader looked in, so the pair agreed with each other
/// while disagreeing with the format. A real ImageMagick file — three bands of bytes, with
/// <c>subrow_size</c> and <c>maps_per_cycle</c> both left at zero — came back as a one-band bitmap,
/// which is what those two zeroes mean at the offsets that were being read. The headers below are
/// built field by field at Khoros' offsets, so a drift like that fails here even though a round-trip
/// still passes.
/// </remarks>
[TestFixture]
public sealed class ViffForeignFileTests {

  private const int _WIDTH = 4;
  private const int _HEIGHT = 2;

  [Test]
  [Category("Unit")]
  public void ThreeBandByteFile_DecodesToItsColours() {
    var pixels = new byte[_WIDTH * _HEIGHT * 3];

    // Band-sequential: the whole red plane, then green, then blue. Pixel 0 red, pixel 1 green,
    // pixel 2 blue, pixel 3 white; the second row repeats them.
    byte[] reds = [255, 0, 0, 255, 255, 0, 0, 255];
    byte[] greens = [0, 255, 0, 255, 0, 255, 0, 255];
    byte[] blues = [0, 0, 255, 255, 0, 0, 255, 255];
    reds.CopyTo(pixels, 0);
    greens.CopyTo(pixels, 8);
    blues.CopyTo(pixels, 16);

    var data = _Header(bands: 3, ViffStorageType.Byte, ViffColorSpaceModel.GenericRgb, pixels.Length);
    pixels.CopyTo(data, ViffHeader.StructSize);

    var file = ViffReader.FromBytes(data);
    Assert.Multiple(() => {
      Assert.That(file.Width, Is.EqualTo(_WIDTH));
      Assert.That(file.Height, Is.EqualTo(_HEIGHT));
      Assert.That(file.Bands, Is.EqualTo(3), "num_data_bands lives at offset 560, not 528");
      Assert.That(file.StorageType, Is.EqualTo(ViffStorageType.Byte), "data_storage_type lives at offset 564, not 596");
    });

    var rgb = ViffFile.ToRawImage(file).ToRgb24();
    Assert.Multiple(() => {
      Assert.That(rgb[..3], Is.EqualTo(new byte[] { 255, 0, 0 }), "first pixel");
      Assert.That(rgb[3..6], Is.EqualTo(new byte[] { 0, 255, 0 }), "second pixel");
      Assert.That(rgb[6..9], Is.EqualTo(new byte[] { 0, 0, 255 }), "third pixel");
      Assert.That(rgb[9..12], Is.EqualTo(new byte[] { 255, 255, 255 }), "fourth pixel");
    });
  }

  [Test]
  [Category("Unit")]
  public void MappedFile_ResolvesItsPixelsThroughTheColourMap() {
    // Four entries — red, green, blue, white — stored a channel at a time, like the pixels.
    byte[] map = [
      255, 0, 0, 255, // reds
      0, 255, 0, 255, // greens
      0, 0, 255, 255, // blues
    ];
    byte[] indices = [0, 1, 2, 3, 0, 1, 2, 3];

    var data = _Header(bands: 1, ViffStorageType.Byte, ViffColorSpaceModel.None, map.Length + indices.Length);
    BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(572), (uint)ViffMapScheme.OnePerBand);
    BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(576), (uint)ViffMapType.Byte);
    BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(580), 3); // map_row_size: channels
    BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(584), 4); // map_col_size: entries
    map.CopyTo(data, ViffHeader.StructSize);
    indices.CopyTo(data, ViffHeader.StructSize + map.Length);

    var file = ViffReader.FromBytes(data);
    Assert.That(file.MapData, Is.Not.Null, "the map sits between header and pixels and has to be consumed");
    Assert.That(file.PixelData[..4], Is.EqualTo(new byte[] { 0, 1, 2, 3 }));

    var rgb = ViffFile.ToRawImage(file).ToRgb24();
    Assert.Multiple(() => {
      Assert.That(rgb[..3], Is.EqualTo(new byte[] { 255, 0, 0 }), "index 0");
      Assert.That(rgb[3..6], Is.EqualTo(new byte[] { 0, 255, 0 }), "index 1");
      Assert.That(rgb[6..9], Is.EqualTo(new byte[] { 0, 0, 255 }), "index 2");
      Assert.That(rgb[9..12], Is.EqualTo(new byte[] { 255, 255, 255 }), "index 3");
    });
  }

  /// <summary>
  /// A file whose <c>map_scheme</c> is VFF_MS_NONE carries no map even though <c>map_enable</c> is
  /// set, which is what ImageMagick writes on every unmapped file it produces.
  /// </summary>
  [Test]
  [Category("Unit")]
  public void MapEnableAlone_DoesNotMeanThereIsAMap() {
    byte[] greys = [0, 64, 128, 255, 255, 128, 64, 0];
    var data = _Header(bands: 1, ViffStorageType.Byte, ViffColorSpaceModel.None, greys.Length);
    greys.CopyTo(data, ViffHeader.StructSize);

    var file = ViffReader.FromBytes(data);
    Assert.That(file.MapData, Is.Null);
    Assert.That(file.PixelData, Is.EqualTo(greys));
  }

  /// <summary>Builds the header ImageMagick would write, one Khoros offset at a time.</summary>
  private static byte[] _Header(int bands, ViffStorageType storage, ViffColorSpaceModel colorSpace, int payloadBytes) {
    var data = new byte[ViffHeader.StructSize + payloadBytes];
    data[0] = ViffHeader.Magic;
    data[1] = 1; // file_type
    data[2] = 1; // release
    data[3] = 3; // version
    data[4] = 0x02; // machine_dep: VFF_DEP_IEEEORDER, so everything below is big-endian

    void Put(int offset, uint value) => BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(offset), value);

    Put(520, _WIDTH);
    Put(524, _HEIGHT);
    Put(528, 0); // subrow_size: zero on an ordinary image, which is why it cannot be the band count
    Put(548, 1); // location_type: VFF_LOC_IMPLICIT
    Put(556, 1); // num_of_images
    Put(560, (uint)bands);
    Put(564, (uint)storage);
    Put(592, 1); // map_enable: VFF_MAP_OPTIONAL, set whether or not a map follows
    Put(596, 0); // maps_per_cycle: zero, and it is not the storage type
    Put(600, (uint)colorSpace);
    return data;
  }
}
