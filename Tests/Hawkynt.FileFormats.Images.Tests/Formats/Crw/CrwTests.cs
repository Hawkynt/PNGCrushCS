using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Text;
using FileFormat.Core;
using FileFormat.Crw;

namespace FileFormat.Crw.Tests;

/// <summary>
/// Canon's CIFF raw, built here from the published layout.
/// </summary>
/// <remarks>
/// The file assembled below is a heap with its directory at the end, exactly as a camera writes one:
/// a sensor-information record stating the sensor's size and the borders of the picture inside it, a
/// record stating the size the camera means to produce, a record naming one of three Huffman table
/// pairs, and the sensor record itself. The coded stream in it uses the two codes dcraw's own worked
/// example spells out for the first table — 11110 for the leaf that means "no skip, no bits" — so
/// what is being checked is this project's tables against a published statement of them rather than
/// against itself.
/// </remarks>
[TestFixture]
public sealed class CrwTests {

  private const int _SENSOR_WIDTH = 64;
  private const int _SENSOR_HEIGHT = 48;
  private const int _LEFT = 2;
  private const int _TOP = 2;
  private const int _RIGHT = 61;
  private const int _BOTTOM = 45;

  /// <summary>The plane holding the low two bits of every sample, four samples a byte.</summary>
  private static byte[] _LowBits() {
    var plane = new byte[_SENSOR_WIDTH * _SENSOR_HEIGHT / 4];
    for (var i = 0; i < plane.Length; ++i)
      plane[i] = (byte)(i % 200); // never 0xFF, which would look like the end of a coded stream

    return plane;
  }

  /// <summary>What the low-bits plane says the sample at this position carries.</summary>
  private static int _LowBitsAt(int x, int y) {
    var plane = _LowBits();
    var index = y * _SENSOR_WIDTH + x;
    return (plane[index / 4] >> (2 * (index % 4))) & 3;
  }

  /// <summary>Every block coded as sixty-four differences of nothing, so every sample is the base.</summary>
  private static byte[] _CodedStream() {
    // 11110 is the first table's leaf 0x00 — skip nothing, write nothing — and 111111011 is the
    // second table's, which ends the block.
    var bits = new StringBuilder();
    for (var block = 0; block < _SENSOR_WIDTH * _SENSOR_HEIGHT / 64; ++block)
      bits.Append("11110").Append("111111011");

    while (bits.Length % 8 != 0)
      bits.Append('0');

    var stream = new byte[bits.Length / 8];
    for (var i = 0; i < stream.Length; ++i)
      stream[i] = Convert.ToByte(bits.ToString(i * 8, 8), 2);

    return stream;
  }

  private sealed class _Record {
    public ushort Type;
    public byte[] Content = [];
    public int Offset;
  }

  private static byte[] _Build(int statedWidth = _RIGHT - _LEFT + 1, int statedHeight = _BOTTOM - _TOP + 1, int right = _RIGHT, int bottom = _BOTTOM) {
    var sensorInfo = new byte[18];
    void Short(int index, int value) => BinaryPrimitives.WriteInt16LittleEndian(sensorInfo.AsSpan(index * 2), (short)value);
    Short(0, 18);
    Short(1, _SENSOR_WIDTH);
    Short(2, _SENSOR_HEIGHT);
    Short(3, 1);
    Short(4, 1);
    Short(5, _LEFT);
    Short(6, _TOP);
    Short(7, right);
    Short(8, bottom);

    var imageSpec = new byte[16];
    BinaryPrimitives.WriteUInt32LittleEndian(imageSpec.AsSpan(0), (uint)statedWidth);
    BinaryPrimitives.WriteUInt32LittleEndian(imageSpec.AsSpan(4), (uint)statedHeight);

    var decoderTable = new byte[4];

    var lowBits = _LowBits();
    var coded = _CodedStream();

    // The record opens with the low-bits plane; the coded stream begins 514 bytes past its end.
    var sensorRecord = new byte[lowBits.Length + 514 + coded.Length];
    lowBits.CopyTo(sensorRecord, 0);
    coded.CopyTo(sensorRecord, lowBits.Length + 514);

    var records = new List<_Record> {
      new() { Type = 0x2005, Content = sensorRecord },
      new() { Type = 0x1031, Content = sensorInfo },
      new() { Type = 0x1810, Content = imageSpec },
      new() { Type = 0x1835, Content = decoderTable },
    };

    var heap = new MemoryStream();
    foreach (var record in records) {
      record.Offset = (int)heap.Position;
      heap.Write(record.Content);
    }

    var tableOffset = (int)heap.Position;
    Span<byte> field = stackalloc byte[4];
    BinaryPrimitives.WriteUInt16LittleEndian(field, (ushort)records.Count);
    heap.Write(field[..2]);
    foreach (var record in records) {
      BinaryPrimitives.WriteUInt16LittleEndian(field, record.Type);
      heap.Write(field[..2]);
      BinaryPrimitives.WriteUInt32LittleEndian(field, (uint)record.Content.Length);
      heap.Write(field);
      BinaryPrimitives.WriteUInt32LittleEndian(field, (uint)record.Offset);
      heap.Write(field);
    }

    BinaryPrimitives.WriteUInt32LittleEndian(field, (uint)tableOffset);
    heap.Write(field);

    var file = new MemoryStream();
    file.Write("II"u8);
    BinaryPrimitives.WriteUInt32LittleEndian(field, 26);
    file.Write(field);
    file.Write("HEAPCCDR"u8);
    file.Write(new byte[12]);
    file.Write(heap.ToArray());
    return file.ToArray();
  }

  [Test]
  [Category("Unit")]
  public void TheBordersAndTheStatedSizeAgreeAndDecideThePicture() {
    var file = CrwReader.FromBytes(_Build());

    Assert.Multiple(() => {
      Assert.That(file.SensorWidth, Is.EqualTo(_SENSOR_WIDTH));
      Assert.That(file.SensorHeight, Is.EqualTo(_SENSOR_HEIGHT));
      Assert.That(file.Width, Is.EqualTo(60));
      Assert.That(file.Height, Is.EqualTo(44));
      Assert.That(CrwFile.ToRawImage(file).PixelData, Has.Length.EqualTo(60 * 44 * 3));
    });
  }

  [Test]
  [Category("Unit")]
  public void EverySampleIsTheBaseWithItsLowTwoBitsBesideIt() {
    var file = CrwReader.FromBytes(_Build());

    Assert.Multiple(() => {
      for (var y = 0; y < _SENSOR_HEIGHT; y += 7)
      for (var x = 0; x < _SENSOR_WIDTH; x += 5) {
        var expected = (512 << 2) + _LowBitsAt(x, y);
        Assert.That(file.Sensor[y * _SENSOR_WIDTH + x], Is.EqualTo(expected), $"sample at {x},{y}");
      }
    });
  }

  [Test]
  [Category("Unit")]
  public void ANonCanonFileIsRefused() {
    var data = _Build();
    data[7] = (byte)'X';
    Assert.That(() => CrwReader.FromBytes(data), Throws.InstanceOf<InvalidDataException>());
  }

  [Test]
  [Category("Unit")]
  public void BordersReachingOutsideTheSensorAreRefused() {
    Assert.That(() => CrwReader.FromBytes(_Build(right: _SENSOR_WIDTH + 10, statedWidth: _SENSOR_WIDTH + 10 - _LEFT + 1)),
      Throws.InstanceOf<InvalidDataException>());
  }

  [Test]
  [Category("Unit")]
  public void ASizeTheBordersDoNotDescribeIsRefused() {
    // The camera states the picture's size in a record of its own, and the two are read separately.
    Assert.That(() => CrwReader.FromBytes(_Build(statedWidth: 61)), Throws.InstanceOf<InvalidDataException>());
  }

  [Test]
  [Category("Unit")]
  public void TooSmallToBeAHeapIsRefused() {
    Assert.That(() => CrwReader.FromBytes(new byte[8]), Throws.InstanceOf<InvalidDataException>());
  }
}
