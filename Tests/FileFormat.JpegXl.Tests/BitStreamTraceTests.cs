using System;
using System.IO;
using System.Text;
using FileFormat.JpegXl;
using FileFormat.JpegXl.Codec;

namespace FileFormat.JpegXl.Tests;

/// <summary>
/// Diagnostic bit-stream tracer for the small modular .jxl fixture.
/// Decodes step-by-step and prints bit positions/values to TestContext.Out so we
/// can compare against the libjxl reference and spot bit-position desyncs without
/// having to run djxl interactively.
/// </summary>
[TestFixture]
public sealed class BitStreamTraceTests {

  [TestCase("relossless_8x8.jxl")]
  [TestCase("minimal_8x8.jxl")]
  [TestCase("8x8_vardct.jxl")]
  [Explicit("Diagnostic — prints bit-stream layout to TestContext.Out.")]
  public void Trace_BitStream(string filename) {
    var path = Path.Combine(TestContext.CurrentContext.TestDirectory, "Fixtures", filename);
    var bytes = File.ReadAllBytes(path);

    var sb = new StringBuilder();
    sb.AppendLine($"=== {filename} ===");
    sb.AppendLine($"File: {bytes.Length} bytes");
    sb.AppendLine($"Sig: {bytes[0]:X2} {bytes[1]:X2} (expect FF 0A)");
    sb.Append("First 16 bytes after sig:");
    for (var i = 2; i < Math.Min(18, bytes.Length); ++i) sb.Append($" {bytes[i]:X2}");
    sb.AppendLine();
    sb.AppendLine();

    var reader = new JxlBitReader(bytes, 2);

    var b0 = reader.BitsRead;
    var (w, h) = JxlSizeHeader.Decode(reader);
    sb.AppendLine($"SizeHeader: {w}×{h}  (consumed {reader.BitsRead - b0} bits)");

    b0 = reader.BitsRead;
    JxlImageMetadata meta;
    try {
      meta = JxlImageMetadata.Decode(reader);
      sb.AppendLine($"ImageMetadata: all_default={meta.AllDefault}, " +
        $"bits/sample={meta.BitDepth.BitsPerSample}{(meta.BitDepth.FloatingPoint ? "f" : "u")}, " +
        $"extra_channels={meta.NumExtraChannels}, xyb={meta.XybEncoded}, " +
        $"modular16={meta.Modular16BitBuffers}, ce.AllDefault={meta.ColorEncoding.AllDefault}, " +
        $"ce.ColorSpace={meta.ColorEncoding.ColorSpace}, ce.WantIcc={meta.ColorEncoding.WantIcc}");
      sb.AppendLine($"  (consumed {reader.BitsRead - b0} bits, total bits read = {reader.BitsRead})");
    } catch (Exception ex) {
      sb.AppendLine($"ImageMetadata FAILED at bit {reader.BitsRead}: {ex.GetType().Name}: {ex.Message}");
      TestContext.Out.WriteLine(sb.ToString());
      Assert.Pass();
      return;
    }

    b0 = reader.BitsRead;
    JxlSpecFrameHeader fh;
    try {
      fh = JxlSpecFrameHeader.Decode(reader, meta);
      sb.AppendLine($"FrameHeader: all_default={fh.AllDefault}, type={fh.FrameType}, " +
        $"encoding={fh.Encoding}, flags=0x{fh.Flags:X}, " +
        $"groupSizeShift={fh.GroupSizeShift}, passes={fh.NumPasses}, isLast={fh.IsLast}");
      sb.AppendLine($"  (consumed {reader.BitsRead - b0} bits, total bits read = {reader.BitsRead})");
      sb.AppendLine($"After FrameHeader: {bytes.Length * 8 - reader.BitsRead} bits remaining " +
        $"({(bytes.Length * 8 - reader.BitsRead) / 8.0:F2} bytes)");
    } catch (Exception ex) {
      sb.AppendLine($"FrameHeader FAILED at bit {reader.BitsRead}: {ex.GetType().Name}: {ex.Message}");
      TestContext.Out.WriteLine(sb.ToString());
      Assert.Pass();
      return;
    }

    if (fh.Encoding == JxlFrameEncoding.Modular) {
      b0 = reader.BitsRead;
      // Top-level modular frames begin with: has_tree (1 bit) + optional
      // global tree + optional global residual histograms BEFORE the per-group
      // ModularGenericDecompress section. Per libjxl `dec_modular.cc::
      // ModularFrameDecoder::DecodeGlobalInfo`.
      try {
        var hasTreeBit = reader.BitsRead;
        var hasTree = reader.ReadBool();
        sb.AppendLine($"  has_tree = {hasTree} at bit {hasTreeBit}");
        if (hasTree) {
          var treeStart = reader.BitsRead;
          var globalTree = JxlMaTreeDecoder.Decode(reader);
          sb.AppendLine($"  Global MA tree: leafCount={globalTree.LeafCount}, " +
            $"consumed {reader.BitsRead - treeStart} bits at bit {treeStart}");
          var entStart = reader.BitsRead;
          var globalEnt = JxlEntropyDecoder.Read(reader, Math.Max(1, globalTree.LeafCount));
          sb.AppendLine($"  Global residual entropy: consumed {reader.BitsRead - entStart} bits at bit {entStart}");
        }
        var startBit = reader.BitsRead;
        var useGlobalTree = reader.ReadBool();
        sb.AppendLine($"  GroupHeader.use_global_tree = {useGlobalTree} at bit {startBit} (offset {startBit - b0})");

        var wpStart = reader.BitsRead;
        var wpAllDefault = reader.ReadBool();
        if (!wpAllDefault) {
          for (var i = 0; i < 7; ++i) reader.ReadBits(5);
          for (var i = 0; i < 4; ++i) reader.ReadBits(4);
        }
        sb.AppendLine($"  WP.all_default = {wpAllDefault} (consumed {reader.BitsRead - wpStart} bits)");

        var ntStart = reader.BitsRead;
        var nt = reader.ReadU32(0, 0, 1, 0, 2, 4, 18, 8);
        sb.AppendLine($"  num_transforms = {nt} at bit {ntStart} (consumed {reader.BitsRead - ntStart} bits)");

        if (nt > 0) {
          for (var t = 0; t < nt; ++t) {
            var tStart = reader.BitsRead;
            var tid = reader.ReadBits(2);
            sb.AppendLine($"  transform[{t}] id={tid} at bit {tStart}");
            if (tid == 0 || tid == 1) {
              // RCT or Palette: read begin_c
              var bc = reader.ReadU32(0, 3, 8, 6, 72, 10, 1096, 13);
              sb.AppendLine($"    begin_c = {bc}");
              if (tid == 0) {
                var rctType = reader.ReadU32(6, 0, 0, 2, 2, 4, 10, 6);
                sb.AppendLine($"    rct_type = {rctType}");
              } else {
                var numC = reader.ReadU32(1, 0, 3, 0, 4, 0, 1, 13);
                var nbColours = reader.ReadU32(0, 8, 256, 10, 1280, 12, 5376, 16);
                var nbDeltas = reader.ReadU32(0, 0, 1, 8, 257, 10, 1281, 16);
                var dPred = reader.ReadBits(4);
                sb.AppendLine($"    num_c={numC}, nb_colours={nbColours}, nb_deltas={nbDeltas}, d_pred={dPred}");
              }
            } else if (tid == 2) {
              // Squeeze: num_squeezes U32 + per-step (1 bit horizontal + 1 bit in_place + begin_c U32 + num_c U32)
              var numSqueezes = reader.ReadU32(0, 0, 1, 4, 9, 6, 41, 8);
              sb.AppendLine($"    num_squeezes = {numSqueezes}");
              for (var s = 0; s < numSqueezes; ++s) {
                var horiz = reader.ReadBool();
                var inPlace = reader.ReadBool();
                var sBeg = reader.ReadU32(0, 3, 8, 6, 72, 10, 1096, 13);
                var sNum = reader.ReadU32(1, 0, 2, 0, 3, 0, 4, 4);
                sb.AppendLine($"    step[{s}] horiz={horiz}, in_place={inPlace}, begin_c={sBeg}, num_c={sNum}");
              }
            }
          }
        }

        sb.AppendLine($"  After transforms at bit {reader.BitsRead}");

        // Now MA tree decode
        try {
          var treeStart = reader.BitsRead;
          var tree = JxlMaTreeDecoder.Decode(reader);
          sb.AppendLine($"  MA tree decoded at bit {treeStart}, leafCount={tree.LeafCount}, " +
            $"consumed {reader.BitsRead - treeStart} bits");
        } catch (Exception ex) {
          sb.AppendLine($"  MA tree FAILED at bit {reader.BitsRead}: {ex.GetType().Name}: {ex.Message}");
          // After failure, the reader is in some indeterminate position.
          // The error message itself contains the diagnostic info.
        }
      } catch (Exception ex) {
        sb.AppendLine($"  Granular trace FAILED at bit {reader.BitsRead}: {ex.GetType().Name}: {ex.Message}");
      }
    } else {
      b0 = reader.BitsRead;
      try {
        var img = JxlVarDctSpecDecoder.Decode(reader, w, h, (int)meta.BitDepth.BitsPerSample);
        sb.AppendLine($"VarDCT decode SUCCESS — channels={img.Channels.Length}");
        sb.AppendLine($"  (consumed {reader.BitsRead - b0} bits)");
      } catch (Exception ex) {
        sb.AppendLine($"VarDCT decode FAILED at bit {reader.BitsRead} (offset {reader.BitsRead - b0}): " +
          $"{ex.GetType().Name}: {ex.Message}");
      }
    }

    TestContext.Out.WriteLine(sb.ToString());
    Assert.Pass();
  }

  [Test]
  [Explicit("Diagnostic — dumps every bit of the codestream sequentially.")]
  public void Trace_RawBits() {
    foreach (var filename in new[] { "minimal_8x8.jxl", "8x8_vardct.jxl" }) {
      var path = Path.Combine(TestContext.CurrentContext.TestDirectory, "Fixtures", filename);
      if (!File.Exists(path)) continue;
      var bytes = File.ReadAllBytes(path);

      var sb = new StringBuilder();
      sb.AppendLine($"=== {filename} bit-by-bit (first 64 bits after FF 0A) ===");
      for (var b = 0; b < Math.Min(64, (bytes.Length - 2) * 8); ++b) {
        var byteIdx = 2 + b / 8;
        var bitIdx = b % 8;
        var bit = (bytes[byteIdx] >> bitIdx) & 1;
        sb.Append(bit);
        if ((b + 1) % 8 == 0) sb.Append(' ');
        if ((b + 1) % 32 == 0) sb.AppendLine();
      }
      TestContext.Out.WriteLine(sb.ToString());
    }
    Assert.Pass();
  }
}
