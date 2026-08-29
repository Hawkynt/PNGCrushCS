namespace FileFormat.Codecs.H264;

/// <summary>One reconstructed 4:2:0 frame and the identities used by the H.264 DPB.</summary>
internal sealed class H264Picture {
  internal H264Picture(int lumaWidth, int lumaHeight, long serial) {
    this.Serial = serial;
    this.LumaWidth = lumaWidth;
    this.LumaHeight = lumaHeight;
    this.ChromaWidth = lumaWidth >> 1;
    this.ChromaHeight = lumaHeight >> 1;
    this.Luma = new byte[lumaWidth * lumaHeight];
    this.Cb = new byte[this.ChromaWidth * this.ChromaHeight];
    this.Cr = new byte[this.ChromaWidth * this.ChromaHeight];
  }

  internal int LumaWidth { get; }
  internal int LumaHeight { get; }
  internal int ChromaWidth { get; }
  internal int ChromaHeight { get; }
  internal byte[] Luma { get; }
  internal byte[] Cb { get; }
  internal byte[] Cr { get; }
  internal byte[] Chroma(int component) => component == 0 ? this.Cb : this.Cr;

  internal int FrameNum { get; set; }
  internal int PicNum { get; set; }

  /// <summary>TopFieldOrderCnt of clause 8.2.1, retained for temporal direct prediction.</summary>
  internal int TopFieldOrderCnt { get; set; }

  /// <summary>BottomFieldOrderCnt of clause 8.2.1, retained for temporal direct prediction.</summary>
  internal int BottomFieldOrderCnt { get; set; }

  /// <summary>Frame picture order count used to construct B-slice reference lists and presentation order.</summary>
  internal int PicOrderCnt { get; set; }

  /// <summary>Motion metadata used when this picture becomes the co-located picture of temporal direct prediction.</summary>
  internal H264MotionField? Motion { get; set; }

  internal bool IsLongTerm { get; set; }
  internal int LongTermFrameIdx { get; set; } = -1;
  internal int LongTermPicNum => this.LongTermFrameIdx;
  internal long Serial { get; }
}
