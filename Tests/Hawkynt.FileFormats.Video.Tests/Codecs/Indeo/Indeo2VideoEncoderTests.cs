using System;
using System.Buffers.Binary;
using System.IO;
using System.Linq;
using FileFormat.Avi;
using FileFormat.Codecs.Indeo;
using FileFormat.Core;
using Hawkynt.FileFormats.Video;

namespace FileFormat.Codecs.Tests;

[TestFixture]
public sealed class Indeo2VideoEncoderTests {

  [Test]
  [Category("Unit")]
  public void TheEncoderIsRegisteredUnderRt21() {
    var stream = _Stream(8, 4);

    Assert.Multiple(() => {
      Assert.That(VideoFormatRegistry.AllEncoders.Select(e => e.CodecName), Does.Contain("Intel Indeo 2"));
      Assert.That(VideoFormatRegistry.CanEncode(stream), Is.True);
      Assert.That(VideoFormatRegistry.CreateEncoder(stream), Is.InstanceOf<Indeo2VideoEncoder>());
    });
  }

  [Test]
  [Category("Unit")]
  public void DescribeStreamWritesTheVfwRt21Description() {
    var requested = new MediaStreamInfo {
      Index = 0,
      Kind = MediaStreamKind.Video,
      Codec = CodecTag.FromCharacters("RT21"),
      Width = 8,
      Height = 4,
      TimeBase = new Rational(1, 25),
      FrameRate = new Rational(25, 1),
      DeclaredFrameCount = 7,
    };

    var described = Indeo2VideoEncoder.Create(requested).DescribeStream();

    Assert.Multiple(() => {
      Assert.That(described.Codec, Is.EqualTo(CodecTag.FromCharacters("RT21")));
      Assert.That(described.Handler, Is.EqualTo(CodecTag.FromCharacters("RT21")));
      Assert.That(described.CodecId, Is.EqualTo("V_MS/VFW/FOURCC"));
      Assert.That(described.BitsPerPixel, Is.EqualTo(24));
      Assert.That(described.TimeBase, Is.EqualTo(requested.TimeBase));
      Assert.That(described.FrameRate, Is.EqualTo(requested.FrameRate));
      Assert.That(described.DeclaredFrameCount, Is.EqualTo(7));
      Assert.That(described.CodecPrivateData, Has.Length.EqualTo(40));
      Assert.That(BinaryPrimitives.ReadInt16LittleEndian(described.CodecPrivateData.AsSpan(14)), Is.EqualTo(24));
      Assert.That(described.CodecPrivateData.AsSpan(16, 4).ToArray(), Is.EqualTo("RT21"u8.ToArray()));
    });
  }

  [TestCase(7, 4)]
  [TestCase(8, 3)]
  [Category("Unit")]
  public void GeometryTheNativePlanesCannotRepresentRefuses(int width, int height)
    => Assert.Throws<NotSupportedException>(() => Indeo2VideoEncoder.Create(_Stream(width, height)));

  [Test]
  [Category("Unit")]
  public void AnotherBitDepthRefuses() {
    var stream = new MediaStreamInfo {
      Index = 0,
      Kind = MediaStreamKind.Video,
      Codec = CodecTag.FromCharacters("RT21"),
      Width = 8,
      Height = 4,
      BitsPerPixel = 16,
    };

    Assert.Throws<NotSupportedException>(() => Indeo2VideoEncoder.Create(stream));
  }

  [Test]
  [Category("Unit")]
  public void NeutralNativePlanesRoundTripExactlyThroughAnAvi() {
    const int width = 8;
    const int height = 4;
    var encoder = Indeo2VideoEncoder.Create(_Stream(width, height));

    Assert.That(encoder.TryEncode(_NeutralYuv(width, height), 12, out var packet), Is.True);
    Assert.Multiple(() => {
      Assert.That(packet.IsKeyFrame, Is.True);
      Assert.That(packet.PresentationTimestamp, Is.EqualTo(12));
      Assert.That(packet.DecodeTimestamp, Is.EqualTo(12));
      Assert.That(packet.Data.Length, Is.GreaterThan(48));
      Assert.That(packet.Data.Span[18], Is.Not.Zero, "the packet is an intra frame");
      Assert.That(packet.Data.Span[0x22] >> 4, Is.Zero, "only the two documented table-selector pairs are set");
    });

    var stream = encoder.DescribeStream();
    var avi = VideoIO.Mux<AviWriter>([stream], [packet]);
    var container = AviContainer.FromBytes(avi);
    var readStream = AviContainer.Streams(container).Single();
    var readPacket = AviContainer.ReadPackets(container).Single(p => p.StreamIndex == readStream.Index);
    var decoder = (Indeo2VideoDecoder)VideoFormatRegistry.CreateDecoder(readStream);

    Assert.That(decoder.TryDecode(readPacket, out var decoded), Is.True);
    Assert.Multiple(() => {
      Assert.That(readStream.Codec, Is.EqualTo(CodecTag.FromCharacters("RT21")));
      Assert.That(decoded.Width, Is.EqualTo(width));
      Assert.That(decoded.Height, Is.EqualTo(height));
      Assert.That(decoder.Planes.Luma, Is.All.EqualTo(128));
      Assert.That(decoder.Planes.Cb, Is.All.EqualTo(128));
      Assert.That(decoder.Planes.Cr, Is.All.EqualTo(128));
    });
  }

  [Test]
  [Category("Unit")]
  public void TruncatedSourceRefuses() {
    var encoder = Indeo2VideoEncoder.Create(_Stream(8, 4));
    var image = new RawImage {
      Width = 8,
      Height = 4,
      Format = PixelFormat.Rgba32,
      PixelData = [0],
    };

    Assert.Throws<InvalidDataException>(() => encoder.TryEncode(image, 0, out _));
  }

  [Test]
  [Category("Unit")]
  public void MidStreamGeometryChangeRefuses() {
    var encoder = Indeo2VideoEncoder.Create(_Stream(8, 4));
    var image = _NeutralYuv(16, 4);

    Assert.Throws<InvalidDataException>(() => encoder.TryEncode(image, 0, out _));
  }

  [Test]
  [Category("Unit")]
  public void HuffmanWriterIsTheInverseOfTheReaderForEverySymbol() {
    var writer = new Indeo2BitWriter();
    foreach (var entry in Indeo2Tables.Codes)
      writer.WriteSymbol(entry.Symbol);

    var reader = new Indeo2BitReader(writer.ToArray());
    foreach (var entry in Indeo2Tables.Codes)
      Assert.That(reader.ReadSymbol(), Is.EqualTo(entry.Symbol));
  }

  [Test]
  [Category("Unit")]
  public void AnInterRunCannotCrossItsScanline() {
    var bits = new Indeo2BitWriter();
    bits.WriteSymbol(0x84); // run of five pairs / ten samples on an eight-sample line
    var body = bits.ToArray();
    var frame = new byte[48 + body.Length];
    body.CopyTo(frame, 48);

    var decoder = new Indeo2FrameDecoder(8, 4);
    var exception = Assert.Throws<InvalidDataException>(() => decoder.Decode(frame));
    Assert.That(exception!.Message, Does.Contain("overruns the 8-sample line"));
  }

  private static MediaStreamInfo _Stream(int width, int height) => new() {
    Index = 0,
    Kind = MediaStreamKind.Video,
    Codec = CodecTag.FromCharacters("RT21"),
    Width = width,
    Height = height,
  };

  private static RawImage _NeutralYuv(int width, int height) {
    var samples = width * height * 3;
    var data = new byte[samples];
    Array.Fill(data, (byte)128);
    return new() {
      Width = width,
      Height = height,
      Format = PixelFormat.Yuv444P8,
      PixelData = data,
    };
  }
}
