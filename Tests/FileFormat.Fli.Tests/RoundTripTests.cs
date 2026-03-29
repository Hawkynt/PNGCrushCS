using System;
using System.Buffers.Binary;
using FileFormat.Fli;

namespace FileFormat.Fli.Tests;

[TestFixture]
public sealed class RoundTripTests {

  [Test]
  [Category("Integration")]
  public void RoundTrip_SingleFrame_Fli_WithByteRun() {
    var width = (short)8;
    var height = (short)4;
    var pixels = new byte[width * height];
    for (var i = 0; i < pixels.Length; ++i)
      pixels[i] = (byte)(i % 3 == 0 ? 10 : i * 7 % 256);

    var byteRunData = FliDeltaEncoder.EncodeByteRun(pixels, width, height);

    var original = new FliFile {
      Width = width,
      Height = height,
      FrameCount = 1,
      Speed = 5,
      FrameType = FliFrameType.Fli,
      Frames = [
        new FliFrame {
          Chunks = [new FliFrameChunk { ChunkType = FliChunkType.ByteRun, Data = byteRunData }]
        }
      ]
    };

    var bytes = FliWriter.ToBytes(original);
    var restored = FliReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(width));
    Assert.That(restored.Height, Is.EqualTo(height));
    Assert.That(restored.FrameCount, Is.EqualTo(1));
    Assert.That(restored.Speed, Is.EqualTo(5));
    Assert.That(restored.FrameType, Is.EqualTo(FliFrameType.Fli));
    Assert.That(restored.Frames.Count, Is.EqualTo(1));
    Assert.That(restored.Frames[0].Chunks.Count, Is.EqualTo(1));
    Assert.That(restored.Frames[0].Chunks[0].ChunkType, Is.EqualTo(FliChunkType.ByteRun));

    var decodedPixels = FliDeltaDecoder.DecodeByteRun(restored.Frames[0].Chunks[0].Data, width, height);
    Assert.That(decodedPixels, Is.EqualTo(pixels));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_MultipleFrames_Flc() {
    var width = (short)4;
    var height = (short)2;

    var frames = new FliFrame[3];
    for (var f = 0; f < 3; ++f) {
      var pixels = new byte[width * height];
      for (var i = 0; i < pixels.Length; ++i)
        pixels[i] = (byte)((i + f * 10) % 256);

      frames[f] = new FliFrame {
        Chunks = [new FliFrameChunk { ChunkType = FliChunkType.ByteRun, Data = FliDeltaEncoder.EncodeByteRun(pixels, width, height) }]
      };
    }

    var original = new FliFile {
      Width = width,
      Height = height,
      FrameCount = 3,
      Speed = 33,
      FrameType = FliFrameType.Flc,
      Frames = frames
    };

    var bytes = FliWriter.ToBytes(original);
    var restored = FliReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(width));
    Assert.That(restored.Height, Is.EqualTo(height));
    Assert.That(restored.FrameCount, Is.EqualTo(3));
    Assert.That(restored.FrameType, Is.EqualTo(FliFrameType.Flc));
    Assert.That(restored.Frames.Count, Is.EqualTo(3));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_BlackChunk() {
    var original = new FliFile {
      Width = 320,
      Height = 200,
      FrameCount = 1,
      Speed = 100,
      FrameType = FliFrameType.Fli,
      Frames = [
        new FliFrame {
          Chunks = [new FliFrameChunk { ChunkType = FliChunkType.Black, Data = [] }]
        }
      ]
    };

    var bytes = FliWriter.ToBytes(original);
    var restored = FliReader.FromBytes(bytes);

    Assert.That(restored.Frames[0].Chunks[0].ChunkType, Is.EqualTo(FliChunkType.Black));
    Assert.That(restored.Frames[0].Chunks[0].Data, Is.Empty);
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_Color256Palette() {
    // Build a Color256 chunk: 1 packet, skip 0, count 4, 4 RGB triplets
    var paletteChunkData = new byte[2 + 1 + 1 + 4 * 3];
    BinaryPrimitives.WriteUInt16LittleEndian(paletteChunkData, 1); // 1 packet
    paletteChunkData[2] = 0;  // skip 0
    paletteChunkData[3] = 4;  // count 4
    paletteChunkData[4] = 255; paletteChunkData[5] = 0;   paletteChunkData[6] = 0;   // red
    paletteChunkData[7] = 0;   paletteChunkData[8] = 255; paletteChunkData[9] = 0;   // green
    paletteChunkData[10] = 0;  paletteChunkData[11] = 0;  paletteChunkData[12] = 255; // blue
    paletteChunkData[13] = 128; paletteChunkData[14] = 128; paletteChunkData[15] = 128; // gray

    var original = new FliFile {
      Width = 4,
      Height = 2,
      FrameCount = 1,
      Speed = 100,
      FrameType = FliFrameType.Fli,
      Frames = [
        new FliFrame {
          Chunks = [
            new FliFrameChunk { ChunkType = FliChunkType.Color256, Data = paletteChunkData },
            new FliFrameChunk { ChunkType = FliChunkType.Black, Data = [] }
          ]
        }
      ]
    };

    var bytes = FliWriter.ToBytes(original);
    var restored = FliReader.FromBytes(bytes);

    Assert.That(restored.Palette, Is.Not.Null);
    Assert.That(restored.Palette![0], Is.EqualTo(255)); // R of color 0
    Assert.That(restored.Palette[1], Is.EqualTo(0));    // G of color 0
    Assert.That(restored.Palette[2], Is.EqualTo(0));    // B of color 0
    Assert.That(restored.Palette[3], Is.EqualTo(0));    // R of color 1
    Assert.That(restored.Palette[4], Is.EqualTo(255));  // G of color 1
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_LiteralChunk() {
    var pixelData = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 };

    var original = new FliFile {
      Width = 4,
      Height = 2,
      FrameCount = 1,
      Speed = 50,
      FrameType = FliFrameType.Fli,
      Frames = [
        new FliFrame {
          Chunks = [new FliFrameChunk { ChunkType = FliChunkType.Literal, Data = pixelData }]
        }
      ]
    };

    var bytes = FliWriter.ToBytes(original);
    var restored = FliReader.FromBytes(bytes);

    Assert.That(restored.Frames[0].Chunks[0].ChunkType, Is.EqualTo(FliChunkType.Literal));
    Assert.That(restored.Frames[0].Chunks[0].Data, Is.EqualTo(pixelData));
  }
}
