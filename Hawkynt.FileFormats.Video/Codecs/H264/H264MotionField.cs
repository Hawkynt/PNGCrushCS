using System;

namespace FileFormat.Codecs.H264;

/// <summary>
/// Per-4x4 motion state retained with a decoded picture for B-slice temporal direct prediction.
/// </summary>
internal sealed class H264MotionField {
  internal H264MotionField(int widthInBlocks, int heightInBlocks) {
    this.WidthInBlocks = widthInBlocks;
    this.HeightInBlocks = heightInBlocks;
    var count = checked(widthInBlocks * heightInBlocks);
    this.MvX0 = new short[count];
    this.MvY0 = new short[count];
    this.RefSerial0 = new long[count];
    this.MvX1 = new short[count];
    this.MvY1 = new short[count];
    this.RefSerial1 = new long[count];
  }

  internal int WidthInBlocks { get; }
  internal int HeightInBlocks { get; }
  internal short[] MvX0 { get; }
  internal short[] MvY0 { get; }
  internal long[] RefSerial0 { get; }
  internal short[] MvX1 { get; }
  internal short[] MvY1 { get; }
  internal long[] RefSerial1 { get; }

  internal bool TryGet(int blockX, int blockY, out H264StoredMotion motion) {
    if ((uint)blockX >= (uint)this.WidthInBlocks || (uint)blockY >= (uint)this.HeightInBlocks) {
      motion = default;
      return false;
    }

    var at = blockY * this.WidthInBlocks + blockX;
    motion = new(
      this.MvX0[at], this.MvY0[at], this.RefSerial0[at],
      this.MvX1[at], this.MvY1[at], this.RefSerial1[at]);
    return true;
  }
}

internal readonly record struct H264StoredMotion(
  int MvX0,
  int MvY0,
  long RefSerial0,
  int MvX1,
  int MvY1,
  long RefSerial1) {
  internal bool HasList0 => this.RefSerial0 != 0;
  internal bool HasList1 => this.RefSerial1 != 0;
}
