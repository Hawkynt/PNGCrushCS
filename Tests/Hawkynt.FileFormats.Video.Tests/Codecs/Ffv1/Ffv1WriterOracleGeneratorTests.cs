using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using FileFormat.Core;
using FileFormat.Matroska;
using Hawkynt.FileFormats.Video;

namespace FileFormat.Codecs.Ffv1.Tests;

/// <summary>One-shot fixture producer used to validate this branch's writer with an external FFmpeg.</summary>
[TestFixture]
public class Ffv1WriterOracleGeneratorTests {

  [Test]
  [Category("OracleGenerator")]
  [TestCaseSource(nameof(_Cases))]
  public void EmitMatroskaForExternalFfmpeg(OracleCase oracleCase) {
    if (!OperatingSystem.IsLinux())
      Assert.Ignore("The one-shot writer oracle is emitted only once on the Linux CI leg.");

    const int width = 8;
    const int height = 6;
    var stream = new MediaStreamInfo {
      Index = 0,
      Kind = MediaStreamKind.Video,
      Width = width,
      Height = height,
      TimeBase = new Rational(1, 1),
      FrameRate = new Rational(1, 1),
      BitsPerPixel = RawImage.BitsPerPixel(oracleCase.Format),
    };
    var encoder = Ffv1Encoder.Create(stream, oracleCase.Format, oracleCase.Options);
    var raw = new List<byte>();
    var packets = new CodedPacket[oracleCase.FrameCount];

    for (var frame = 0; frame < packets.Length; ++frame) {
      var source = _Picture(oracleCase.Format, oracleCase.Options.BitsPerRawSample, width, height, frame);
      raw.AddRange(source.PixelData);
      Assert.That(encoder.TryEncode(source, frame, out packets[frame]), Is.True);
    }

    var file = VideoIO.Mux<MatroskaWriter>([encoder.DescribeStream()], packets);
    Assert.Fail($"FFV1_WRITER_ORACLE|{oracleCase.Name}|{oracleCase.FfmpegPixelFormat}|{Convert.ToBase64String(raw.ToArray())}|{Convert.ToBase64String(file)}");
  }

  private static IEnumerable<TestCaseData> _Cases() {
    foreach (var value in _DATA)
      yield return new TestCaseData(value).SetName($"WriterOracle({value.Name})");
  }

  private static RawImage _Picture(PixelFormat format, int statedBits, int width, int height, int frame) {
    var bits = statedBits == 0 ? format switch {
      PixelFormat.Gray8 => 8,
      PixelFormat.Gray10 => 10,
      PixelFormat.Yuv420P12 => 12,
      _ => 16,
    } : statedBits;

    if (format == PixelFormat.Gray8) {
      var pixels = new byte[width * height];
      for (var i = 0; i < pixels.Length; ++i)
        pixels[i] = (byte)((i * 37 + frame * 19 + 11) & 0xFF);
      return new() { Width = width, Height = height, Format = format, PixelData = pixels };
    }

    var mask = (1 << bits) - 1;
    if (format is PixelFormat.Gray10) {
      var pixels = new byte[width * height * 2];
      for (var i = 0; i < width * height; ++i)
        BinaryPrimitives.WriteUInt16LittleEndian(pixels.AsSpan(i * 2), (ushort)((i * 173 + frame * 97 + 31) & mask));
      return new() { Width = width, Height = height, Format = format, PixelData = pixels };
    }

    if (format is PixelFormat.Rgb48) {
      var pixels = new byte[width * height * 6];
      for (var i = 0; i < width * height; ++i)
        for (var channel = 0; channel < 3; ++channel) {
          var value = (i * (211 + channel * 58) + frame * 101 + channel * 313 + 17) & mask;
          BinaryPrimitives.WriteUInt16BigEndian(pixels.AsSpan((i * 3 + channel) * 2), (ushort)value);
        }
      return new() { Width = width, Height = height, Format = format, PixelData = pixels };
    }

    if (format is PixelFormat.Yuv420P12) {
      var pixels = new byte[(width * height + 2 * ((width + 1) / 2) * ((height + 1) / 2)) * 2];
      var sample = 0;
      for (var plane = 0; plane < 3; ++plane) {
        var planeWidth = plane == 0 ? width : (width + 1) / 2;
        var planeHeight = plane == 0 ? height : (height + 1) / 2;
        for (var y = 0; y < planeHeight; ++y)
          for (var x = 0; x < planeWidth; ++x) {
            var value = (x * 211 + y * 397 + plane * 907 + frame * 131 + 29) & mask;
            BinaryPrimitives.WriteUInt16LittleEndian(pixels.AsSpan(sample++ * 2), (ushort)value);
          }
      }
      return new() { Width = width, Height = height, Format = format, PixelData = pixels };
    }

    if (format is PixelFormat.Yuv444P16) {
      var pixels = new byte[width * height * 3 * 2];
      var sample = 0;
      for (var plane = 0; plane < 3; ++plane)
        for (var y = 0; y < height; ++y)
          for (var x = 0; x < width; ++x) {
            // Cross 0x8000 repeatedly so RFC 9043 section 3.3.1's signed-16 predictor exception is exercised.
            var value = (x * 10007 + y * 20011 + plane * 30013 + frame * 40009 + 0x7000) & mask;
            BinaryPrimitives.WriteUInt16LittleEndian(pixels.AsSpan(sample++ * 2), (ushort)value);
          }
      return new() { Width = width, Height = height, Format = format, PixelData = pixels };
    }

    throw new NotSupportedException(format.ToString());
  }

  private static int[] _CustomTransitions() {
    var result = new int[256];
    result[128] = 1;
    result[129] = -1;
    result[130] = 1;
    return result;
  }

  private static Ffv1EncoderOptions _V3(int bits = 0, int keyFrameInterval = 1, Ffv1EntropyCoder coder = Ffv1EntropyCoder.Range, Ffv1ContextModel model = Ffv1ContextModel.Small)
    => new() {
      Version = 3,
      BitsPerRawSample = bits,
      EntropyCoder = coder,
      ContextModel = model,
      KeyFrameInterval = keyFrameInterval,
      HorizontalSlices = 1,
      VerticalSlices = 1,
      StateTransitionDelta = coder == Ffv1EntropyCoder.RangeCustom ? _CustomTransitions() : null,
    };

  private static readonly OracleCase[] _DATA = [
    new("v0-rice", PixelFormat.Gray8, "gray", new() { Version = 0, EntropyCoder = Ffv1EntropyCoder.GolombRice }, 1),
    new("v1-range", PixelFormat.Gray8, "gray", new() { Version = 1, EntropyCoder = Ffv1EntropyCoder.Range }, 1),
    new("v3-rice", PixelFormat.Gray8, "gray", _V3(coder: Ffv1EntropyCoder.GolombRice), 1),
    new("v3-range-large", PixelFormat.Gray8, "gray", _V3(model: Ffv1ContextModel.Large), 1),
    new("v3-range-custom", PixelFormat.Gray8, "gray", _V3(coder: Ffv1EntropyCoder.RangeCustom), 1),
    new("v3-gop", PixelFormat.Gray8, "gray", _V3(keyFrameInterval: 3), 4),
    new("gray10", PixelFormat.Gray10, "gray10le", _V3(bits: 10), 1),
    new("yuv420p12", PixelFormat.Yuv420P12, "yuv420p12le", _V3(bits: 12), 1),
    new("rgb9", PixelFormat.Rgb48, "rgb48be", _V3(bits: 9), 1),
    new("rgb10", PixelFormat.Rgb48, "rgb48be", _V3(bits: 10), 1),
    new("rgb11", PixelFormat.Rgb48, "rgb48be", _V3(bits: 11), 1),
    new("rgb12", PixelFormat.Rgb48, "rgb48be", _V3(bits: 12), 1),
    new("rgb13", PixelFormat.Rgb48, "rgb48be", _V3(bits: 13), 1),
    new("rgb14", PixelFormat.Rgb48, "rgb48be", _V3(bits: 14), 1),
    new("rgb15", PixelFormat.Rgb48, "rgb48be", _V3(bits: 15), 1),
    new("rgb16", PixelFormat.Rgb48, "rgb48be", _V3(bits: 16), 1),
    new("yuv444p16-signed-predictor", PixelFormat.Yuv444P16, "yuv444p16le", _V3(bits: 16), 1),
  ];

  public sealed record OracleCase(string Name, PixelFormat Format, string FfmpegPixelFormat, Ffv1EncoderOptions Options, int FrameCount);
}
