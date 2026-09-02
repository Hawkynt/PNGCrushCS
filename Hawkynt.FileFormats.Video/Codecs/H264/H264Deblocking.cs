using System;

namespace FileFormat.Codecs.H264;

/// <summary>The in-loop deblocking filter of H.264 clause 8.7.</summary>
internal static class H264Deblocking {
  private static readonly byte[] _ALPHA = [
    0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,4,4,5,6,7,8,9,10,12,13,15,17,20,22,25,28,
    32,36,40,45,50,56,63,71,80,90,101,113,127,144,162,182,203,226,255,255,
  ];
  private static readonly byte[] _BETA = [
    0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,2,2,2,3,3,3,3,4,4,4,6,6,7,7,8,8,
    9,9,10,10,11,11,12,12,13,13,14,14,15,15,16,16,17,17,18,18,
  ];
  private static readonly byte[,] _TC0 = {
    {
      0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,1,1,1,1,1,1,1,1,1,
      1,2,2,2,2,3,3,3,4,4,4,5,6,6,7,8,9,10,11,13,
    },
    {
      0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,1,1,1,1,1,1,1,1,1,1,2,
      2,2,2,3,3,3,4,4,5,5,6,7,8,8,10,11,12,13,15,17,
    },
    {
      0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,1,1,1,1,1,1,1,1,1,1,2,2,2,2,3,
      3,3,4,4,4,5,6,6,7,8,9,10,11,13,14,16,18,20,23,25,
    },
  };

  internal static void Filter(H264FrameDecoder frame) {
    for (var mbY = 0; mbY < frame.MacroblockHeight; ++mbY)
      for (var mbX = 0; mbX < frame.MacroblockWidth; ++mbX) {
        var mbAddr = mbY * frame.MacroblockWidth + mbX;
        var idc = frame.DeblockingIdcOf(mbAddr);
        if (idc == 1)
          continue;
        var filterLeft = mbX > 0 && (idc != 2 || frame.SliceOf(mbAddr - 1) == frame.SliceOf(mbAddr));
        var filterTop = mbY > 0
          && (idc != 2 || frame.SliceOf(mbAddr - frame.MacroblockWidth) == frame.SliceOf(mbAddr));
        _FilterLumaVertical(frame, mbAddr, mbX, mbY, filterLeft);
        _FilterLumaHorizontal(frame, mbAddr, mbX, mbY, filterTop);
        for (var component = 0; component < 2; ++component) {
          _FilterChromaVertical(frame, mbAddr, mbX, mbY, component, filterLeft);
          _FilterChromaHorizontal(frame, mbAddr, mbX, mbY, component, filterTop);
        }
      }
  }

  private static void _FilterLumaVertical(H264FrameDecoder frame, int mbAddr, int mbX, int mbY, bool filterLeft) {
    var picture = frame.Picture;
    var transform8x8 = frame.Transform8x8Of(mbAddr);
    for (var edge = 0; edge < 4; ++edge) {
      if (edge == 0 && !filterLeft) continue;
      // Clause 8.7.1 filters only the solid luma edges when transform_size_8x8_flag is set.
      // In 4x4-edge units those are the macroblock edge and the edge at offset 8, not 4 or 12.
      if (transform8x8 && (edge & 1) != 0) continue;
      var x = mbX * 16 + edge * 4;
      for (var group = 0; group < 4; ++group) {
        var y = mbY * 16 + group * 4;
        var strength = _BoundaryStrength(frame, x - 1, y, x, y, edge == 0);
        if (strength == 0) continue;
        var (alpha,beta,indexA) = _Thresholds(frame, mbAddr, _MacroblockAt(frame, x - 1, y), false, 0);
        for (var line = 0; line < 4; ++line)
          _FilterLine(picture.Luma, (y + line) * picture.LumaWidth + x, 1, strength, alpha, beta, indexA, false);
      }
    }
  }

  private static void _FilterLumaHorizontal(H264FrameDecoder frame, int mbAddr, int mbX, int mbY, bool filterTop) {
    var picture = frame.Picture;
    var transform8x8 = frame.Transform8x8Of(mbAddr);
    for (var edge = 0; edge < 4; ++edge) {
      if (edge == 0 && !filterTop) continue;
      if (transform8x8 && (edge & 1) != 0) continue;
      var y = mbY * 16 + edge * 4;
      for (var group = 0; group < 4; ++group) {
        var x = mbX * 16 + group * 4;
        var strength = _BoundaryStrength(frame, x, y - 1, x, y, edge == 0);
        if (strength == 0) continue;
        var (alpha,beta,indexA) = _Thresholds(frame, mbAddr, _MacroblockAt(frame, x, y - 1), false, 0);
        for (var line = 0; line < 4; ++line)
          _FilterLine(picture.Luma, y * picture.LumaWidth + x + line, picture.LumaWidth,
            strength, alpha, beta, indexA, false);
      }
    }
  }

  private static void _FilterChromaVertical(
    H264FrameDecoder frame, int mbAddr, int mbX, int mbY, int component, bool filterLeft) {
    var picture = frame.Picture;
    var plane = picture.Chroma(component);
    for (var edge = 0; edge < 2; ++edge) {
      if (edge == 0 && !filterLeft) continue;
      var chromaX = mbX * 8 + edge * 4;
      var lumaX = chromaX * 2;
      for (var group = 0; group < 4; ++group) {
        var chromaY = mbY * 8 + group * 2;
        var lumaY = chromaY * 2;
        var strength = _BoundaryStrength(frame, lumaX - 1, lumaY, lumaX, lumaY, edge == 0);
        if (strength == 0) continue;
        var (alpha,beta,indexA) = _Thresholds(frame, mbAddr, _MacroblockAt(frame, lumaX - 1, lumaY), true, component);
        for (var line = 0; line < 2; ++line)
          _FilterLine(plane, (chromaY + line) * picture.ChromaWidth + chromaX, 1,
            strength, alpha, beta, indexA, true);
      }
    }
  }

  private static void _FilterChromaHorizontal(
    H264FrameDecoder frame, int mbAddr, int mbX, int mbY, int component, bool filterTop) {
    var picture = frame.Picture;
    var plane = picture.Chroma(component);
    for (var edge = 0; edge < 2; ++edge) {
      if (edge == 0 && !filterTop) continue;
      var chromaY = mbY * 8 + edge * 4;
      var lumaY = chromaY * 2;
      for (var group = 0; group < 4; ++group) {
        var chromaX = mbX * 8 + group * 2;
        var lumaX = chromaX * 2;
        var strength = _BoundaryStrength(frame, lumaX, lumaY - 1, lumaX, lumaY, edge == 0);
        if (strength == 0) continue;
        var (alpha,beta,indexA) = _Thresholds(frame, mbAddr, _MacroblockAt(frame, lumaX, lumaY - 1), true, component);
        for (var line = 0; line < 2; ++line)
          _FilterLine(plane, chromaY * picture.ChromaWidth + chromaX + line, picture.ChromaWidth,
            strength, alpha, beta, indexA, true);
      }
    }
  }

  private static int _MacroblockAt(H264FrameDecoder frame, int lumaX, int lumaY)
    => lumaY / 16 * frame.MacroblockWidth + lumaX / 16;

  /// <summary>Boundary strength including the two-reference B-picture cases of clause 8.7.2.1.</summary>
  private static int _BoundaryStrength(
    H264FrameDecoder frame, int pX, int pY, int qX, int qY, bool macroblockEdge) {
    var pMb = _MacroblockAt(frame, pX, pY);
    var qMb = _MacroblockAt(frame, qX, qY);
    if (frame.KindOf(pMb) != H264MacroblockKind.Inter || frame.KindOf(qMb) != H264MacroblockKind.Inter)
      return macroblockEdge ? 4 : 3;
    if (frame.BlockHasCoefficients(pX >> 2, pY >> 2) || frame.BlockHasCoefficients(qX >> 2, qY >> 2))
      return 2;

    var p = frame.BlockMotionPair(pX >> 2, pY >> 2);
    var q = frame.BlockMotionPair(qX >> 2, qY >> 2);
    return _SamePrediction(p, q) || _SwappedPrediction(p, q) ? 0 : 1;
  }

  private static bool _SamePrediction(
    (int X0,int Y0,long Reference0,bool Predicted0,int X1,int Y1,long Reference1,bool Predicted1) p,
    (int X0,int Y0,long Reference0,bool Predicted0,int X1,int Y1,long Reference1,bool Predicted1) q)
    => _SameListEntry(p.Predicted0,p.Reference0,p.X0,p.Y0,q.Predicted0,q.Reference0,q.X0,q.Y0)
       && _SameListEntry(p.Predicted1,p.Reference1,p.X1,p.Y1,q.Predicted1,q.Reference1,q.X1,q.Y1);

  private static bool _SwappedPrediction(
    (int X0,int Y0,long Reference0,bool Predicted0,int X1,int Y1,long Reference1,bool Predicted1) p,
    (int X0,int Y0,long Reference0,bool Predicted0,int X1,int Y1,long Reference1,bool Predicted1) q)
    => _SameListEntry(p.Predicted0,p.Reference0,p.X0,p.Y0,q.Predicted1,q.Reference1,q.X1,q.Y1)
       && _SameListEntry(p.Predicted1,p.Reference1,p.X1,p.Y1,q.Predicted0,q.Reference0,q.X0,q.Y0);

  private static bool _SameListEntry(
    bool predictedP,long referenceP,int mvXP,int mvYP,
    bool predictedQ,long referenceQ,int mvXQ,int mvYQ) {
    if (predictedP != predictedQ) return false;
    if (!predictedP) return true;
    return referenceP == referenceQ && Math.Abs(mvXP - mvXQ) < 4 && Math.Abs(mvYP - mvYQ) < 4;
  }

  private static (int Alpha,int Beta,int IndexA) _Thresholds(
    H264FrameDecoder frame,int qMb,int pMb,bool chroma,int component) {
    var qpP = frame.QpOf(pMb);
    var qpQ = frame.QpOf(qMb);
    if (chroma) {
      qpP = H264Transform.ChromaQp(Math.Clamp(qpP + frame.ChromaQpOffsetOf(pMb,component),0,51));
      qpQ = H264Transform.ChromaQp(Math.Clamp(qpQ + frame.ChromaQpOffsetOf(qMb,component),0,51));
    }
    var average = (qpP + qpQ + 1) >> 1;
    var indexA = Math.Clamp(average + frame.FilterOffsetAOf(qMb),0,51);
    var indexB = Math.Clamp(average + frame.FilterOffsetBOf(qMb),0,51);
    return (_ALPHA[indexA],_BETA[indexB],indexA);
  }

  private static void _FilterLine(
    byte[] plane,int q0At,int step,int strength,int alpha,int beta,int indexA,bool chromaStyle) {
    var p0=plane[q0At-step]; var p1=plane[q0At-2*step]; var p2=plane[q0At-3*step]; var p3=plane[q0At-4*step];
    var q0=plane[q0At]; var q1=plane[q0At+step]; var q2=plane[q0At+2*step]; var q3=plane[q0At+3*step];
    if (Math.Abs(p0-q0)>=alpha || Math.Abs(p1-p0)>=beta || Math.Abs(q1-q0)>=beta) return;
    var ap=Math.Abs(p2-p0); var aq=Math.Abs(q2-q0);
    if (strength<4) {
      var tc0=_TC0[strength-1,indexA];
      var tc=chromaStyle?tc0+1:tc0+(ap<beta?1:0)+(aq<beta?1:0);
      var delta=Math.Clamp((((q0-p0)<<2)+(p1-q1)+4)>>3,-tc,tc);
      plane[q0At-step]=_Clip(p0+delta); plane[q0At]=_Clip(q0-delta);
      if (!chromaStyle && ap<beta)
        plane[q0At-2*step]=(byte)(p1+Math.Clamp((p2+((p0+q0+1)>>1)-(p1<<1))>>1,-tc0,tc0));
      if (!chromaStyle && aq<beta)
        plane[q0At+step]=(byte)(q1+Math.Clamp((q2+((p0+q0+1)>>1)-(q1<<1))>>1,-tc0,tc0));
      return;
    }

    var wide=Math.Abs(p0-q0)<(alpha>>2)+2;
    if (!chromaStyle && ap<beta && wide) {
      plane[q0At-step]=(byte)((p2+2*p1+2*p0+2*q0+q1+4)>>3);
      plane[q0At-2*step]=(byte)((p2+p1+p0+q0+2)>>2);
      plane[q0At-3*step]=(byte)((2*p3+3*p2+p1+p0+q0+4)>>3);
    } else plane[q0At-step]=(byte)((2*p1+p0+q1+2)>>2);
    if (!chromaStyle && aq<beta && wide) {
      plane[q0At]=(byte)((p1+2*p0+2*q0+2*q1+q2+4)>>3);
      plane[q0At+step]=(byte)((p0+q0+q1+q2+2)>>2);
      plane[q0At+2*step]=(byte)((2*q3+3*q2+q1+q0+p0+4)>>3);
    } else plane[q0At]=(byte)((2*q1+q0+p1+2)>>2);
  }

  private static byte _Clip(int value) => (byte)(value<0?0:value>255?255:value);
}
