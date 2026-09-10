using System;
using System.Buffers.Binary;
using System.Linq;
using FileFormat.Core;

namespace FileFormat.Codecs.DnxHd.Tests;

/// <summary>The DNxHR SQ writer: its canonical codes, RI frame header, round trip and refusals.</summary>
[TestFixture]
public sealed class DnxHdVideoEncoderTests {

  [Test]
  [Category("Unit")]
  public void EveryVlcTableWritesWhatItReads() {
    for (var group = 0; group < 6; ++group) {
      _RoundTripTable(DnxHdVlcTables.AmplitudeLengths[group], DnxHdVlcTables.AmplitudeSymbols[group]);
      _RoundTripTable(DnxHdVlcTables.RunLengths[group], DnxHdVlcTables.RunSymbols[group]);
      _RoundTripTable(DnxHdVlcTables.DcLengths[group], DnxHdVlcTables.DcBitCounts[group]);
    }
  }

  [Test]
  [Category("Unit")]
  public void AFrameSaysWhatTheRiSqProfileRequires() {
    var packet = _Encode(_Flat(64, 48, 64, 128, 192), 64, 48);
    var bytes = packet.Data.ToArray();
    var header = DnxHdFrameHeader.Parse(bytes);

    Assert.Multiple(() => {
      Assert.That(bytes.Length, Is.EqualTo(8192), "7.1: small RI CBR frames are at least 8192 bytes");
      Assert.That(header.HeaderVersion, Is.EqualTo(3), "7.2.1: RI uses header version 3");
      Assert.That(header.HeaderSize, Is.EqualTo(640));
      Assert.That(header.CompressionIdValue, Is.EqualTo(1273), "Table C.2: DNxHR SQ");
      Assert.That(header.SamplesPerLine, Is.EqualTo(64));
      Assert.That(header.ActiveLines, Is.EqualTo(48));
      Assert.That(header.BitDepth, Is.EqualTo(8));
      Assert.That(header.SubSampling, Is.EqualTo(0), "7.2.5: SSC=00 is 4:2:2");
      Assert.That(header.FrameEncoded, Is.True);
      Assert.That(header.InterlacedSource, Is.False);
      Assert.That(header.Rgb, Is.False);
      Assert.That(header.Alpha, Is.False);
      Assert.That(header.ScanIndices, Has.Length.EqualTo(3));
      Assert.That(header.ScanIndices, Is.Ordered);
      Assert.That(header.ScanIndices.All(static offset => (offset & 3) == 0), Is.True,
        "7.3.1.3: every macroblock scan line starts at a four-byte boundary");
      Assert.That(bytes[^4..], Is.EqualTo(new byte[] { 0x60, 0x0D, 0xC0, 0xDE }).AsCollection,
        "7.4: fixed EOF signature when CRCF is clear");
      Assert.That(BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(0x16A)), Is.EqualTo(16),
        "7.2.10: MSIPS is four bytes per scan index plus four");
    });
  }

  [Test]
  [Category("Unit")]
  public void AFlatPictureComesBackSampleExact() {
    const int WIDTH = 32;
    const int HEIGHT = 16;
    var stream = _Stream(WIDTH, HEIGHT);
    var encoder = DnxHdVideoEncoder.Create(stream);

    Assert.That(encoder.TryEncode(_Flat(WIDTH, HEIGHT, 64, 128, 192), 7, out var packet), Is.True);
    var decoder = DnxHdVideoDecoder.Create(encoder.DescribeStream());
    var planes = decoder.DecodePlanes(packet.Data, out _);

    Assert.Multiple(() => {
      Assert.That(planes.Luma, Is.All.EqualTo((ushort)64));
      Assert.That(planes.Cb, Is.All.EqualTo((ushort)128));
      Assert.That(planes.Cr, Is.All.EqualTo((ushort)192));
      Assert.That(packet.PresentationTimestamp, Is.EqualTo(7));
      Assert.That(packet.IsKeyFrame, Is.True);
    });
  }

  [Test]
  [Category("Unit")]
  public void OddRasterRoundTripsWithoutInventingAWholeMacroblockPicture() {
    const int WIDTH = 17;
    const int HEIGHT = 19;
    var packet = _Encode(_Ramp(WIDTH, HEIGHT), WIDTH, HEIGHT);
    var header = DnxHdFrameHeader.Parse(packet.Data.Span);
    var decoder = DnxHdVideoDecoder.Create(_Stream(WIDTH, HEIGHT));

    Assert.That(decoder.TryDecode(packet, out var image), Is.True);
    Assert.Multiple(() => {
      Assert.That(header.SamplesPerLine, Is.EqualTo(WIDTH));
      Assert.That(header.ActiveLines, Is.EqualTo(HEIGHT));
      Assert.That(image.Width, Is.EqualTo(WIDTH));
      Assert.That(image.Height, Is.EqualTo(HEIGHT));
    });
  }

  [Test]
  [Category("Unit")]
  public void RiHeaderGrowsPast1088Lines() {
    const int WIDTH = 16;
    const int HEIGHT = 1089;
    var packet = _Encode(_Flat(WIDTH, HEIGHT, 64, 128, 128), WIDTH, HEIGHT);
    var header = DnxHdFrameHeader.Parse(packet.Data.Span);

    Assert.Multiple(() => {
      Assert.That(header.ScanIndices, Has.Length.EqualTo(69));
      Assert.That(header.HeaderSize, Is.EqualTo(644),
        "7.2.1: four bytes are added for every whole or partial sixteen lines past 1088");
    });
  }

  [Test]
  [Category("Unit")]
  public void TheEncoderRefusesTheFixedRasterDnxHdTag() {
    var stream = new MediaStreamInfo {
      Index = 0,
      Kind = MediaStreamKind.Video,
      Codec = CodecTag.FromCharacters("AVdn"),
      Width = 1920,
      Height = 1080,
    };

    Assert.That(
      () => DnxHdVideoEncoder.Create(stream),
      Throws.TypeOf<NotSupportedException>().With.Message.Contains("AVdh"));
  }

  [Test]
  [Category("Unit")]
  public void DecoderAcceptsMatroskaVfwDnxHrSpelling() {
    var stream = new MediaStreamInfo {
      Index = 0,
      Kind = MediaStreamKind.Video,
      Codec = CodecTag.None,
      CodecId = "V_MS/VFW/FOURCC/AVdh",
      Width = 64,
      Height = 48,
    };

    Assert.That(DnxHdVideoDecoder.Accepts(stream), Is.True);
  }

  private static void _RoundTripTable(byte[] lengths, ushort[] symbols) {
    var table = DnxHdVlcTable.From(lengths, symbols);
    foreach (var symbol in symbols) {
      var writer = new DnxHdBitWriter();
      table.Write(writer, symbol);
      Assert.That(table.Read(new DnxHdBitReader(writer.ToArray())), Is.EqualTo(symbol));
    }
  }

  private static void _RoundTripTable(byte[] lengths, byte[] symbols) {
    var table = DnxHdVlcTable.From(lengths, symbols);
    foreach (var symbol in symbols) {
      var writer = new DnxHdBitWriter();
      table.Write(writer, symbol);
      Assert.That(table.Read(new DnxHdBitReader(writer.ToArray())), Is.EqualTo(symbol));
    }
  }

  private static CodedPacket _Encode(RawImage image, int width, int height) {
    var encoder = DnxHdVideoEncoder.Create(_Stream(width, height));
    Assert.That(encoder.TryEncode(image, 0, out var packet), Is.True);
    return packet;
  }

  private static MediaStreamInfo _Stream(int width, int height) => new() {
    Index = 0,
    Kind = MediaStreamKind.Video,
    Codec = CodecTag.FromCharacters("AVdh"),
    Width = width,
    Height = height,
  };

  private static RawImage _Flat(int width, int height, byte y, byte cb, byte cr) {
    var chromaWidth = (width + 1) / 2;
    var yLength = width * height;
    var cLength = chromaWidth * height;
    var bytes = new byte[yLength + cLength * 2];
    Array.Fill(bytes, y, 0, yLength);
    Array.Fill(bytes, cb, yLength, cLength);
    Array.Fill(bytes, cr, yLength + cLength, cLength);

    return new() {
      Width = width,
      Height = height,
      Format = PixelFormat.Yuv422P8,
      PixelData = bytes,
      ColorInfo = RawImageColorInfo.Bt709Limited,
    };
  }

  private static RawImage _Ramp(int width, int height) {
    var chromaWidth = (width + 1) / 2;
    var yLength = width * height;
    var cLength = chromaWidth * height;
    var bytes = new byte[yLength + cLength * 2];

    for (var y = 0; y < height; ++y)
      for (var x = 0; x < width; ++x)
        bytes[y * width + x] = (byte)(16 + (x * 173 + y * 47) % 220);

    Array.Fill(bytes, (byte)96, yLength, cLength);
    Array.Fill(bytes, (byte)160, yLength + cLength, cLength);

    return new() {
      Width = width,
      Height = height,
      Format = PixelFormat.Yuv422P8,
      PixelData = bytes,
      ColorInfo = RawImageColorInfo.Bt709Limited,
    };
  }
}
