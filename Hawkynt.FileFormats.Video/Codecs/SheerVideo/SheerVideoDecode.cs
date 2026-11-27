using System;
using System.Collections.Generic;
using FileFormat.Codecs.Hqx;

namespace FileFormat.Codecs.SheerVideo;

/// <summary>One SheerVideo code table: how many codes of each length from 1 to 15 going up, then of 16, then 15 to 1 coming down.</summary>
internal readonly record struct SheerTable(byte[] Counts, int Sixteens);

/// <summary>One SheerVideo layout: its four-character code, whether it is the alternative start, FFmpeg's pixel format, decoder and tables.</summary>
internal readonly record struct SheerFormat(uint Tag, bool Alt, string PixelFormat, string Decoder, string Table);

/// <summary>
/// SheerVideo's layouts and code tables, and its row decoders, transcribed mechanically from FFmpeg's
/// <c>libavcodec/sheervideo.c</c> and <c>sheervideodata.h</c> (LGPL-2.1-or-later) by a script: every pointer
/// becomes an offset into a plane, every store is masked to the plane's sample width as FFmpeg's stores
/// narrow, and nothing else is changed.
/// </summary>
internal static partial class SheerDecode {

  internal static readonly SheerFormat[] Formats = [
    new(0x42475220, false, "RGB0", "decode_rgb", "rgb"),
    new(0x42477220, false, "RGB0", "decode_rgbi", "rgbi"),
    new(0x58475241, false, "GBRAP10", "decode_argx", "rgbx"),
    new(0x58477241, false, "GBRAP10", "decode_argxi", "rgbxi"),
    new(0x58424752, false, "GBRP10", "decode_rgbx", "rgbx"),
    new(0x58424772, false, "GBRP10", "decode_rgbxi", "rgbxi"),
    new(0x42475241, false, "ARGB", "decode_argb", "rgb"),
    new(0x42477241, false, "ARGB", "decode_argbi", "rgbi"),
    new(0x52425941, true, "YUVA444P", "decode_aybr", "ybr"),
    new(0x52625941, false, "YUVA444P", "decode_aybr", "ybr"),
    new(0x52427941, true, "YUVA444P", "decode_aybri", "ybri"),
    new(0x52627941, false, "YUVA444P", "decode_aybri", "ybri"),
    new(0x52425920, true, "YUV444P", "decode_ybr", "ybr"),
    new(0x52625920, false, "YUV444P", "decode_ybr", "ybr"),
    new(0x52427920, true, "YUV444P", "decode_ybri", "ybri"),
    new(0x52627920, false, "YUV444P", "decode_ybri", "ybri"),
    new(0x0A524259, false, "YUV444P10", "decode_ybr10", "ybr10"),
    new(0x0A524279, false, "YUV444P10", "decode_ybr10i", "ybr10i"),
    new(0x70344143, false, "YUVA444P10", "decode_ca4p", "ybr10"),
    new(0x69344143, false, "YUVA444P10", "decode_ca4i", "ybr10i"),
    new(0x59525942, false, "YUV422P", "decode_byry", "byry"),
    new(0x79525942, false, "YUV422P", "decode_byryi", "byryi"),
    new(0x72596259, false, "YUV422P", "decode_ybyr", "ybyr"),
    new(0x70323843, false, "YUVA422P", "decode_c82p", "byry"),
    new(0x69323843, false, "YUVA422P", "decode_c82i", "byryi"),
    new(0x595259A2, false, "YUV422P10", "decode_yry10", "yry10"),
    new(0x795259A2, false, "YUV422P10", "decode_yry10i", "yry10i"),
    new(0x70324143, false, "YUVA422P10", "decode_ca2p", "yry10"),
    new(0x69324143, false, "YUVA422P10", "decode_ca2i", "yry10i"),
  ];

  internal static readonly Dictionary<string, SheerTable[]> Tables = new() {
    ["rgb"] = [new([0, 0, 2, 2, 3, 3, 5, 5, 8, 8, 10, 9, 14, 15, 18, 17, 16, 13, 10, 10, 8, 7, 6, 5, 3, 2, 3, 0, 0, 0], 54), new([0, 2, 0, 2, 0, 1, 1, 0, 2, 1, 3, 3, 4, 7, 13, 11, 8, 4, 3, 3, 1, 2, 1, 0, 1, 0, 1, 2, 0, 0], 180)],
    ["rgbi"] = [new([0, 0, 1, 3, 3, 3, 6, 8, 8, 11, 12, 15, 18, 21, 38, 0, 22, 19, 15, 12, 11, 7, 8, 6, 4, 2, 3, 0, 0, 0], 0), new([1, 0, 1, 1, 1, 1, 2, 1, 2, 4, 3, 5, 5, 6, 12, 14, 6, 6, 5, 3, 3, 3, 2, 1, 1, 2, 0, 1, 0, 0], 164)],
    ["ybr"] = [new([0, 0, 2, 2, 2, 3, 5, 5, 7, 7, 8, 9, 13, 13, 19, 16, 14, 12, 9, 9, 7, 6, 6, 4, 4, 1, 2, 1, 0, 0], 70), new([1, 1, 0, 1, 0, 1, 0, 0, 1, 1, 2, 2, 3, 5, 5, 5, 5, 3, 2, 2, 1, 0, 1, 0, 0, 1, 0, 1, 0, 0], 212)],
    ["ybyr"] = [new([0, 0, 2, 2, 3, 3, 5, 5, 8, 8, 10, 10, 13, 15, 19, 18, 15, 12, 10, 10, 8, 7, 6, 5, 3, 2, 3, 0, 0, 0], 54), new([1, 1, 0, 1, 0, 1, 0, 1, 1, 2, 2, 3, 2, 5, 5, 5, 4, 3, 2, 2, 2, 1, 1, 1, 1, 0, 0, 1, 0, 0], 208)],
    ["byry"] = [new([0, 0, 2, 2, 2, 3, 5, 5, 7, 7, 8, 11, 10, 14, 19, 14, 16, 12, 10, 8, 7, 6, 6, 4, 4, 1, 2, 1, 0, 0], 70), new([1, 1, 0, 1, 0, 1, 0, 1, 2, 1, 2, 2, 3, 4, 6, 6, 4, 2, 3, 2, 1, 1, 1, 1, 1, 0, 0, 1, 0, 0], 208)],
    ["ybr10i"] = [new([0, 0, 1, 0, 3, 8, 9, 12, 19, 27, 27, 39, 50, 63, 93, 89, 64, 50, 38, 26, 26, 20, 12, 9, 8, 3, 0, 0, 0, 0], 328), new([0, 1, 1, 2, 2, 1, 2, 2, 4, 4, 6, 7, 9, 13, 28, 28, 12, 11, 6, 7, 5, 3, 3, 1, 1, 2, 2, 1, 0, 0], 860)],
    ["ybr10"] = [new([0, 0, 0, 1, 6, 6, 8, 12, 18, 21, 27, 29, 36, 47, 71, 72, 46, 36, 29, 27, 21, 17, 13, 7, 7, 5, 0, 0, 0, 0], 462), new([0, 1, 2, 1, 2, 1, 1, 1, 2, 3, 2, 5, 6, 10, 20, 20, 10, 6, 4, 3, 2, 2, 2, 1, 1, 1, 2, 1, 0, 0], 912)],
    ["rgbx"] = [new([0, 0, 0, 1, 3, 9, 10, 13, 19, 26, 28, 35, 40, 53, 77, 77, 50, 42, 34, 28, 25, 19, 13, 10, 8, 4, 0, 0, 0, 0], 400), new([0, 0, 1, 2, 6, 4, 3, 2, 3, 4, 6, 8, 10, 18, 39, 39, 18, 11, 8, 6, 4, 4, 1, 3, 5, 4, 3, 0, 0, 0], 812)],
    ["yry10"] = [new([0, 0, 0, 1, 6, 6, 8, 12, 18, 21, 27, 29, 36, 47, 71, 72, 46, 36, 29, 27, 21, 17, 13, 7, 7, 5, 0, 0, 0, 0], 462), new([0, 1, 2, 1, 1, 1, 2, 3, 2, 4, 5, 5, 8, 14, 16, 18, 11, 7, 7, 4, 4, 3, 2, 2, 1, 1, 2, 1, 0, 0], 896)],
    ["yry10i"] = [new([0, 0, 1, 0, 3, 8, 9, 12, 19, 27, 27, 40, 48, 64, 93, 89, 65, 49, 38, 26, 26, 20, 12, 9, 8, 3, 0, 0, 0, 0], 328), new([0, 1, 0, 3, 1, 3, 3, 3, 6, 7, 7, 12, 11, 19, 23, 20, 18, 12, 12, 8, 6, 5, 4, 3, 2, 2, 2, 1, 0, 0], 830)],
    ["ybri"] = [new([0, 0, 2, 2, 2, 3, 5, 5, 7, 10, 11, 13, 15, 13, 26, 20, 16, 17, 12, 11, 9, 7, 5, 5, 3, 3, 1, 1, 0, 0], 32), new([1, 0, 1, 0, 1, 1, 0, 2, 1, 2, 2, 2, 3, 6, 6, 5, 6, 3, 2, 2, 2, 1, 2, 0, 1, 1, 0, 0, 1, 0], 202)],
    ["byryi"] = [new([0, 0, 2, 2, 2, 2, 6, 5, 8, 8, 12, 12, 16, 14, 24, 20, 16, 18, 12, 12, 8, 7, 5, 6, 3, 1, 2, 1, 0, 0], 32), new([1, 0, 1, 1, 0, 2, 1, 2, 2, 3, 3, 4, 5, 4, 6, 7, 5, 4, 4, 3, 3, 2, 2, 2, 0, 1, 1, 1, 0, 0], 186)],
    ["rgbxi"] = [new([0, 0, 1, 3, 2, 3, 4, 6, 16, 23, 27, 29, 24, 29, 76, 78, 29, 21, 29, 27, 23, 15, 7, 4, 3, 2, 3, 0, 0, 0], 540), new([0, 1, 1, 2, 0, 2, 6, 4, 3, 9, 7, 12, 13, 16, 29, 32, 17, 14, 12, 7, 8, 4, 4, 6, 2, 0, 2, 1, 0, 0], 810)],
  };

  internal delegate void Rows(int width, int height, bool alt, int[][] planes, int[] strides, HqxVlc[] codes, ref HqxBitReader reader);

  internal static Rows Decoder(string name) => name switch {
      "decode_argb" => DecodeArgb,
      "decode_argbi" => DecodeArgbi,
      "decode_argx" => DecodeArgx,
      "decode_argxi" => DecodeArgxi,
      "decode_aybr" => DecodeAybr,
      "decode_aybri" => DecodeAybri,
      "decode_byry" => DecodeByry,
      "decode_byryi" => DecodeByryi,
      "decode_c82i" => DecodeC82i,
      "decode_c82p" => DecodeC82p,
      "decode_ca2i" => DecodeCa2i,
      "decode_ca2p" => DecodeCa2p,
      "decode_ca4i" => DecodeCa4i,
      "decode_ca4p" => DecodeCa4p,
      "decode_rgb" => DecodeRgb,
      "decode_rgbi" => DecodeRgbi,
      "decode_rgbx" => DecodeRgbx,
      "decode_rgbxi" => DecodeRgbxi,
      "decode_ybr" => DecodeYbr,
      "decode_ybr10" => DecodeYbr10,
      "decode_ybr10i" => DecodeYbr10i,
      "decode_ybri" => DecodeYbri,
      "decode_ybyr" => DecodeYbyr,
      "decode_yry10" => DecodeYry10,
      "decode_yry10i" => DecodeYry10i,
      _ => throw new ArgumentOutOfRangeException(nameof(name)),
  };

  /// <summary>FFmpeg's <c>decode_ca4i</c>, transcribed mechanically.</summary>
  private static void DecodeCa4i(int width, int height, bool alt, int[][] P, int[] L, HqxVlc[] V, ref HqxBitReader gb) {
      int x, row;

      var o_dst_a = 0;
      var o_dst_y = 0;
      var o_dst_u = 0;
      var o_dst_v = 0;

      for (row = 0; row < height; row++) {
          if (gb.Read(1) != 0) {
              for (x = 0; x < width; x++) {
                  P[3][o_dst_a + (x)] = (gb.Read(10)) & 0xFFFF;
                  P[0][o_dst_y + (x)] = (gb.Read(10)) & 0xFFFF;
                  P[1][o_dst_u + (x)] = (gb.Read(10)) & 0xFFFF;
                  P[2][o_dst_v + (x)] = (gb.Read(10)) & 0xFFFF;
              }
          } else {
              int[] pred = [502, 512, 512, 502];

              for (x = 0; x < width; x++) {
                  int y, u, v, a;

                  a = V[1].Read(ref gb);
                  y = V[0].Read(ref gb);
                  u = V[1].Read(ref gb);
                  v = V[1].Read(ref gb);

                  P[3][o_dst_a + (x)] = (pred[3] = (a + pred[3]) & 0x3ff) & 0xFFFF;
                  P[0][o_dst_y + (x)] = (pred[0] = (y + pred[0]) & 0x3ff) & 0xFFFF;
                  P[1][o_dst_u + (x)] = (pred[1] = (u + pred[1]) & 0x3ff) & 0xFFFF;
                  P[2][o_dst_v + (x)] = (pred[2] = (v + pred[2]) & 0x3ff) & 0xFFFF;
              }
          }

          o_dst_y += L[0];
          o_dst_u += L[1];
          o_dst_v += L[2];
          o_dst_a += L[3];
      }
  }

  /// <summary>FFmpeg's <c>decode_ca4p</c>, transcribed mechanically.</summary>
  private static void DecodeCa4p(int width, int height, bool alt, int[][] P, int[] L, HqxVlc[] V, ref HqxBitReader gb) {
      int x, row;

      var o_dst_a = 0;
      var o_dst_y = 0;
      var o_dst_u = 0;
      var o_dst_v = 0;

      if (gb.Read(1) != 0) {
          for (x = 0; x < width; x++) {
              P[3][o_dst_a + (x)] = (gb.Read(10)) & 0xFFFF;
              P[0][o_dst_y + (x)] = (gb.Read(10)) & 0xFFFF;
              P[1][o_dst_u + (x)] = (gb.Read(10)) & 0xFFFF;
              P[2][o_dst_v + (x)] = (gb.Read(10)) & 0xFFFF;
          }
      } else {
          int[] pred = [502, 512, 512, 502];

          for (x = 0; x < width; x++) {
              int y, u, v, a;

              a = V[1].Read(ref gb);
              y = V[0].Read(ref gb);
              u = V[1].Read(ref gb);
              v = V[1].Read(ref gb);

              P[3][o_dst_a + (x)] = (pred[3] = (a + pred[3]) & 0x3ff) & 0xFFFF;
              P[0][o_dst_y + (x)] = (pred[0] = (y + pred[0]) & 0x3ff) & 0xFFFF;
              P[1][o_dst_u + (x)] = (pred[1] = (u + pred[1]) & 0x3ff) & 0xFFFF;
              P[2][o_dst_v + (x)] = (pred[2] = (v + pred[2]) & 0x3ff) & 0xFFFF;
          }
      }

      o_dst_y += L[0];
      o_dst_u += L[1];
      o_dst_v += L[2];
      o_dst_a += L[3];

      for (row = 1; row < height; row++) {
          if (gb.Read(1) != 0) {
              for (x = 0; x < width; x++) {
                  P[3][o_dst_a + (x)] = (gb.Read(10)) & 0xFFFF;
                  P[0][o_dst_y + (x)] = (gb.Read(10)) & 0xFFFF;
                  P[1][o_dst_u + (x)] = (gb.Read(10)) & 0xFFFF;
                  P[2][o_dst_v + (x)] = (gb.Read(10)) & 0xFFFF;
              }
          } else {
              int[] pred_TL = new int[4], pred_L = new int[4], pred_T = new int[4];
              int y, u, v, a;

              pred_TL[0] = pred_L[0] = P[0][o_dst_y + (-L[0])];
              pred_TL[1] = pred_L[1] = P[1][o_dst_u + (-L[1])];
              pred_TL[2] = pred_L[2] = P[2][o_dst_v + (-L[2])];
              pred_TL[3] = pred_L[3] = P[3][o_dst_a + (-L[3])];

              for (x = 0; x < width; x++) {
                  pred_T[0] = P[0][o_dst_y + (-L[0] + x)];
                  pred_T[1] = P[1][o_dst_u + (-L[1] + x)];
                  pred_T[2] = P[2][o_dst_v + (-L[2] + x)];
                  pred_T[3] = P[3][o_dst_a + (-L[3] + x)];

                  a = V[1].Read(ref gb);
                  y = V[0].Read(ref gb);
                  u = V[1].Read(ref gb);
                  v = V[1].Read(ref gb);

                  P[3][o_dst_a + (x)] = (pred_L[3] = (a + ((3 * (pred_T[3] + pred_L[3]) - 2 * pred_TL[3]) >> 2)) & 0x3ff) & 0xFFFF;
                  P[0][o_dst_y + (x)] = (pred_L[0] = (y + ((3 * (pred_T[0] + pred_L[0]) - 2 * pred_TL[0]) >> 2)) & 0x3ff) & 0xFFFF;
                  P[1][o_dst_u + (x)] = (pred_L[1] = (u + ((3 * (pred_T[1] + pred_L[1]) - 2 * pred_TL[1]) >> 2)) & 0x3ff) & 0xFFFF;
                  P[2][o_dst_v + (x)] = (pred_L[2] = (v + ((3 * (pred_T[2] + pred_L[2]) - 2 * pred_TL[2]) >> 2)) & 0x3ff) & 0xFFFF;

                  pred_TL[0] = pred_T[0];
                  pred_TL[1] = pred_T[1];
                  pred_TL[2] = pred_T[2];
                  pred_TL[3] = pred_T[3];
              }
          }

          o_dst_y += L[0];
          o_dst_u += L[1];
          o_dst_v += L[2];
          o_dst_a += L[3];
      }
  }

  /// <summary>FFmpeg's <c>decode_ybr10i</c>, transcribed mechanically.</summary>
  private static void DecodeYbr10i(int width, int height, bool alt, int[][] P, int[] L, HqxVlc[] V, ref HqxBitReader gb) {
      int x, row;

      var o_dst_y = 0;
      var o_dst_u = 0;
      var o_dst_v = 0;

      for (row = 0; row < height; row++) {
          if (gb.Read(1) != 0) {
              for (x = 0; x < width; x++) {
                  P[0][o_dst_y + (x)] = (gb.Read(10)) & 0xFFFF;
                  P[1][o_dst_u + (x)] = (gb.Read(10)) & 0xFFFF;
                  P[2][o_dst_v + (x)] = (gb.Read(10)) & 0xFFFF;
              }
          } else {
              int[] pred = [502, 512, 512, 512];

              for (x = 0; x < width; x++) {
                  int y, u, v;

                  y = V[0].Read(ref gb);
                  u = V[1].Read(ref gb);
                  v = V[1].Read(ref gb);

                  P[0][o_dst_y + (x)] = (pred[0] = (y + pred[0]) & 0x3ff) & 0xFFFF;
                  P[1][o_dst_u + (x)] = (pred[1] = (u + pred[1]) & 0x3ff) & 0xFFFF;
                  P[2][o_dst_v + (x)] = (pred[2] = (v + pred[2]) & 0x3ff) & 0xFFFF;
              }
          }

          o_dst_y += L[0];
          o_dst_u += L[1];
          o_dst_v += L[2];
      }
  }

  /// <summary>FFmpeg's <c>decode_ybr10</c>, transcribed mechanically.</summary>
  private static void DecodeYbr10(int width, int height, bool alt, int[][] P, int[] L, HqxVlc[] V, ref HqxBitReader gb) {
      int x, row;

      var o_dst_y = 0;
      var o_dst_u = 0;
      var o_dst_v = 0;

      if (gb.Read(1) != 0) {
          for (x = 0; x < width; x++) {
              P[0][o_dst_y + (x)] = (gb.Read(10)) & 0xFFFF;
              P[1][o_dst_u + (x)] = (gb.Read(10)) & 0xFFFF;
              P[2][o_dst_v + (x)] = (gb.Read(10)) & 0xFFFF;
          }
      } else {
          int[] pred = [502, 512, 512, 512];

          for (x = 0; x < width; x++) {
              int y, u, v;

              y = V[0].Read(ref gb);
              u = V[1].Read(ref gb);
              v = V[1].Read(ref gb);

              P[0][o_dst_y + (x)] = (pred[0] = (y + pred[0]) & 0x3ff) & 0xFFFF;
              P[1][o_dst_u + (x)] = (pred[1] = (u + pred[1]) & 0x3ff) & 0xFFFF;
              P[2][o_dst_v + (x)] = (pred[2] = (v + pred[2]) & 0x3ff) & 0xFFFF;
          }
      }

      o_dst_y += L[0];
      o_dst_u += L[1];
      o_dst_v += L[2];

      for (row = 1; row < height; row++) {
          if (gb.Read(1) != 0) {
              for (x = 0; x < width; x++) {
                  P[0][o_dst_y + (x)] = (gb.Read(10)) & 0xFFFF;
                  P[1][o_dst_u + (x)] = (gb.Read(10)) & 0xFFFF;
                  P[2][o_dst_v + (x)] = (gb.Read(10)) & 0xFFFF;
              }
          } else {
              int[] pred_TL = new int[4], pred_L = new int[4], pred_T = new int[4];
              int y, u, v;

              pred_TL[0] = pred_L[0] = P[0][o_dst_y + (-L[0])];
              pred_TL[1] = pred_L[1] = P[1][o_dst_u + (-L[1])];
              pred_TL[2] = pred_L[2] = P[2][o_dst_v + (-L[2])];

              for (x = 0; x < width; x++) {
                  pred_T[0] = P[0][o_dst_y + (-L[0] + x)];
                  pred_T[1] = P[1][o_dst_u + (-L[1] + x)];
                  pred_T[2] = P[2][o_dst_v + (-L[2] + x)];

                  y = V[0].Read(ref gb);
                  u = V[1].Read(ref gb);
                  v = V[1].Read(ref gb);

                  P[0][o_dst_y + (x)] = (pred_L[0] = (y + ((3 * (pred_T[0] + pred_L[0]) - 2 * pred_TL[0]) >> 2)) & 0x3ff) & 0xFFFF;
                  P[1][o_dst_u + (x)] = (pred_L[1] = (u + ((3 * (pred_T[1] + pred_L[1]) - 2 * pred_TL[1]) >> 2)) & 0x3ff) & 0xFFFF;
                  P[2][o_dst_v + (x)] = (pred_L[2] = (v + ((3 * (pred_T[2] + pred_L[2]) - 2 * pred_TL[2]) >> 2)) & 0x3ff) & 0xFFFF;

                  pred_TL[0] = pred_T[0];
                  pred_TL[1] = pred_T[1];
                  pred_TL[2] = pred_T[2];
              }
          }

          o_dst_y += L[0];
          o_dst_u += L[1];
          o_dst_v += L[2];
      }
  }

  /// <summary>FFmpeg's <c>decode_yry10i</c>, transcribed mechanically.</summary>
  private static void DecodeYry10i(int width, int height, bool alt, int[][] P, int[] L, HqxVlc[] V, ref HqxBitReader gb) {
      int x, row;

      var o_dst_y = 0;
      var o_dst_u = 0;
      var o_dst_v = 0;

      for (row = 0; row < height; row++) {
          if (gb.Read(1) != 0) {
              for (x = 0; x < width; x += 2) {
                  P[0][o_dst_y + (x    )] = (gb.Read(10)) & 0xFFFF;
                  P[1][o_dst_u + (x / 2)] = (gb.Read(10)) & 0xFFFF;
                  P[0][o_dst_y + (x + 1)] = (gb.Read(10)) & 0xFFFF;
                  P[2][o_dst_v + (x / 2)] = (gb.Read(10)) & 0xFFFF;
              }
          } else {
              int[] pred = [502, 512, 512, 0];

              for (x = 0; x < width; x += 2) {
                  int y1, y2, u, v;

                  y1 = V[0].Read(ref gb);
                  u  = V[1].Read(ref gb);
                  y2 = V[0].Read(ref gb);
                  v  = V[1].Read(ref gb);

                  P[0][o_dst_y + (x    )] = (pred[0] = (y1 + pred[0]) & 0x3ff) & 0xFFFF;
                  P[1][o_dst_u + (x / 2)] = (pred[1] = (u  + pred[1]) & 0x3ff) & 0xFFFF;
                  P[0][o_dst_y + (x + 1)] = (pred[0] = (y2 + pred[0]) & 0x3ff) & 0xFFFF;
                  P[2][o_dst_v + (x / 2)] = (pred[2] = (v  + pred[2]) & 0x3ff) & 0xFFFF;
              }
          }

          o_dst_y += L[0];
          o_dst_u += L[1];
          o_dst_v += L[2];
      }
  }

  /// <summary>FFmpeg's <c>decode_yry10</c>, transcribed mechanically.</summary>
  private static void DecodeYry10(int width, int height, bool alt, int[][] P, int[] L, HqxVlc[] V, ref HqxBitReader gb) {
      int x, row;

      var o_dst_y = 0;
      var o_dst_u = 0;
      var o_dst_v = 0;

      if (gb.Read(1) != 0) {
          for (x = 0; x < width; x += 2) {
              P[0][o_dst_y + (x    )] = (gb.Read(10)) & 0xFFFF;
              P[1][o_dst_u + (x / 2)] = (gb.Read(10)) & 0xFFFF;
              P[0][o_dst_y + (x + 1)] = (gb.Read(10)) & 0xFFFF;
              P[2][o_dst_v + (x / 2)] = (gb.Read(10)) & 0xFFFF;
          }
      } else {
          int[] pred = [502, 512, 512, 0];

          for (x = 0; x < width; x += 2) {
              int y1, y2, u, v;

              y1 = V[0].Read(ref gb);
              u  = V[1].Read(ref gb);
              y2 = V[0].Read(ref gb);
              v  = V[1].Read(ref gb);

              P[0][o_dst_y + (x    )] = (pred[0] = (y1 + pred[0]) & 0x3ff) & 0xFFFF;
              P[1][o_dst_u + (x / 2)] = (pred[1] = (u  + pred[1]) & 0x3ff) & 0xFFFF;
              P[0][o_dst_y + (x + 1)] = (pred[0] = (y2 + pred[0]) & 0x3ff) & 0xFFFF;
              P[2][o_dst_v + (x / 2)] = (pred[2] = (v  + pred[2]) & 0x3ff) & 0xFFFF;
          }
      }

      o_dst_y += L[0];
      o_dst_u += L[1];
      o_dst_v += L[2];

      for (row = 1; row < height; row++) {
          if (gb.Read(1) != 0) {
              for (x = 0; x < width; x += 2) {
                  P[0][o_dst_y + (x    )] = (gb.Read(10)) & 0xFFFF;
                  P[1][o_dst_u + (x / 2)] = (gb.Read(10)) & 0xFFFF;
                  P[0][o_dst_y + (x + 1)] = (gb.Read(10)) & 0xFFFF;
                  P[2][o_dst_v + (x / 2)] = (gb.Read(10)) & 0xFFFF;
              }
          } else {
              int[] pred_TL = new int[6], pred_L = new int[6], pred_T = new int[6];
              int y1, y2, u, v;

              pred_TL[0] = pred_L[0] = P[0][o_dst_y + (-L[0])];
              pred_TL[1] = pred_L[1] = P[1][o_dst_u + (-L[1])];
              pred_TL[2] = pred_L[2] = P[2][o_dst_v + (-L[2])];

              for (x = 0; x < width; x += 2) {
                  pred_T[0] = P[0][o_dst_y + (-L[0] + x)];
                  pred_T[3] = P[0][o_dst_y + (-L[0] + x + 1)];
                  pred_T[1] = P[1][o_dst_u + (-L[1] + x / 2)];
                  pred_T[2] = P[2][o_dst_v + (-L[2] + x / 2)];

                  y1 = V[0].Read(ref gb);
                  u  = V[1].Read(ref gb);
                  y2 = V[0].Read(ref gb);
                  v  = V[1].Read(ref gb);

                  P[0][o_dst_y + (x    )] = (pred_L[0] = (y1 + ((3 * (pred_T[0] + pred_L[0]) - 2 * pred_TL[0]) >> 2)) & 0x3ff) & 0xFFFF;
                  P[1][o_dst_u + (x / 2)] = (pred_L[1] = (u + (((pred_L[1] - pred_TL[1]) >> 1) + pred_T[1])) & 0x3ff) & 0xFFFF;
                  P[0][o_dst_y + (x + 1)] = (pred_L[0] = (y2 + ((3 * (pred_T[3] + pred_L[0]) - 2 * pred_T[0]) >> 2)) & 0x3ff) & 0xFFFF;
                  P[2][o_dst_v + (x / 2)] = (pred_L[2] = (v + (((pred_L[2] - pred_TL[2]) >> 1) + pred_T[2])) & 0x3ff) & 0xFFFF;

                  pred_TL[0] = pred_T[3];
                  pred_TL[1] = pred_T[1];
                  pred_TL[2] = pred_T[2];
              }
          }

          o_dst_y += L[0];
          o_dst_u += L[1];
          o_dst_v += L[2];
      }
  }

  /// <summary>FFmpeg's <c>decode_ca2i</c>, transcribed mechanically.</summary>
  private static void DecodeCa2i(int width, int height, bool alt, int[][] P, int[] L, HqxVlc[] V, ref HqxBitReader gb) {
      int x, row;

      var o_dst_y = 0;
      var o_dst_u = 0;
      var o_dst_v = 0;
      var o_dst_a = 0;

      for (row = 0; row < height; row++) {
          if (gb.Read(1) != 0) {
              for (x = 0; x < width; x += 2) {
                  P[3][o_dst_a + (x    )] = (gb.Read(10)) & 0xFFFF;
                  P[0][o_dst_y + (x    )] = (gb.Read(10)) & 0xFFFF;
                  P[1][o_dst_u + (x / 2)] = (gb.Read(10)) & 0xFFFF;
                  P[3][o_dst_a + (x + 1)] = (gb.Read(10)) & 0xFFFF;
                  P[0][o_dst_y + (x + 1)] = (gb.Read(10)) & 0xFFFF;
                  P[2][o_dst_v + (x / 2)] = (gb.Read(10)) & 0xFFFF;
              }
          } else {
              int[] pred = [502, 512, 512, 502];

              for (x = 0; x < width; x += 2) {
                  int y1, y2, u, v, a1, a2;

                  a1 = V[1].Read(ref gb);
                  y1 = V[0].Read(ref gb);
                  u  = V[1].Read(ref gb);
                  a2 = V[1].Read(ref gb);
                  y2 = V[0].Read(ref gb);
                  v  = V[1].Read(ref gb);

                  P[0][o_dst_y + (x    )] = (pred[0] = (y1 + pred[0]) & 0x3ff) & 0xFFFF;
                  P[1][o_dst_u + (x / 2)] = (pred[1] = (u  + pred[1]) & 0x3ff) & 0xFFFF;
                  P[0][o_dst_y + (x + 1)] = (pred[0] = (y2 + pred[0]) & 0x3ff) & 0xFFFF;
                  P[3][o_dst_a + (x    )] = (pred[3] = (a1 + pred[3]) & 0x3ff) & 0xFFFF;
                  P[2][o_dst_v + (x / 2)] = (pred[2] = (v  + pred[2]) & 0x3ff) & 0xFFFF;
                  P[3][o_dst_a + (x + 1)] = (pred[3] = (a2 + pred[3]) & 0x3ff) & 0xFFFF;
              }
          }

          o_dst_y += L[0];
          o_dst_u += L[1];
          o_dst_v += L[2];
          o_dst_a += L[3];
      }
  }

  /// <summary>FFmpeg's <c>decode_ca2p</c>, transcribed mechanically.</summary>
  private static void DecodeCa2p(int width, int height, bool alt, int[][] P, int[] L, HqxVlc[] V, ref HqxBitReader gb) {
      int x, row;

      var o_dst_y = 0;
      var o_dst_u = 0;
      var o_dst_v = 0;
      var o_dst_a = 0;

      if (gb.Read(1) != 0) {
          for (x = 0; x < width; x += 2) {
              P[3][o_dst_a + (x    )] = (gb.Read(10)) & 0xFFFF;
              P[0][o_dst_y + (x    )] = (gb.Read(10)) & 0xFFFF;
              P[1][o_dst_u + (x / 2)] = (gb.Read(10)) & 0xFFFF;
              P[3][o_dst_a + (x + 1)] = (gb.Read(10)) & 0xFFFF;
              P[0][o_dst_y + (x + 1)] = (gb.Read(10)) & 0xFFFF;
              P[2][o_dst_v + (x / 2)] = (gb.Read(10)) & 0xFFFF;
          }
      } else {
          int[] pred = [502, 512, 512, 502];

          for (x = 0; x < width; x += 2) {
              int y1, y2, u, v, a1, a2;

              a1 = V[1].Read(ref gb);
              y1 = V[0].Read(ref gb);
              u  = V[1].Read(ref gb);
              a2 = V[1].Read(ref gb);
              y2 = V[0].Read(ref gb);
              v  = V[1].Read(ref gb);

              P[0][o_dst_y + (x    )] = (pred[0] = (y1 + pred[0]) & 0x3ff) & 0xFFFF;
              P[1][o_dst_u + (x / 2)] = (pred[1] = (u  + pred[1]) & 0x3ff) & 0xFFFF;
              P[0][o_dst_y + (x + 1)] = (pred[0] = (y2 + pred[0]) & 0x3ff) & 0xFFFF;
              P[3][o_dst_a + (x    )] = (pred[3] = (a1 + pred[3]) & 0x3ff) & 0xFFFF;
              P[2][o_dst_v + (x / 2)] = (pred[2] = (v  + pred[2]) & 0x3ff) & 0xFFFF;
              P[3][o_dst_a + (x + 1)] = (pred[3] = (a2 + pred[3]) & 0x3ff) & 0xFFFF;
          }
      }

      o_dst_y += L[0];
      o_dst_u += L[1];
      o_dst_v += L[2];
      o_dst_a += L[3];

      for (row = 1; row < height; row++) {
          if (gb.Read(1) != 0) {
              for (x = 0; x < width; x += 2) {
                  P[3][o_dst_a + (x    )] = (gb.Read(10)) & 0xFFFF;
                  P[0][o_dst_y + (x    )] = (gb.Read(10)) & 0xFFFF;
                  P[1][o_dst_u + (x / 2)] = (gb.Read(10)) & 0xFFFF;
                  P[3][o_dst_a + (x + 1)] = (gb.Read(10)) & 0xFFFF;
                  P[0][o_dst_y + (x + 1)] = (gb.Read(10)) & 0xFFFF;
                  P[2][o_dst_v + (x / 2)] = (gb.Read(10)) & 0xFFFF;
              }
          } else {
              int[] pred_TL = new int[6], pred_L = new int[6], pred_T = new int[6];
              int y1, y2, u, v, a1, a2;

              pred_TL[0] = pred_L[0] = P[0][o_dst_y + (-L[0])];
              pred_TL[1] = pred_L[1] = P[1][o_dst_u + (-L[1])];
              pred_TL[2] = pred_L[2] = P[2][o_dst_v + (-L[2])];
              pred_TL[4] = pred_L[4] = P[3][o_dst_a + (-L[3])];

              for (x = 0; x < width; x += 2) {
                  pred_T[0] = P[0][o_dst_y + (-L[0] + x)];
                  pred_T[3] = P[0][o_dst_y + (-L[0] + x + 1)];
                  pred_T[1] = P[1][o_dst_u + (-L[1] + x / 2)];
                  pred_T[2] = P[2][o_dst_v + (-L[2] + x / 2)];
                  pred_T[4] = P[3][o_dst_a + (-L[3] + x)];
                  pred_T[5] = P[3][o_dst_a + (-L[3] + x + 1)];

                  a1 = V[1].Read(ref gb);
                  y1 = V[0].Read(ref gb);
                  u  = V[1].Read(ref gb);
                  a2 = V[1].Read(ref gb);
                  y2 = V[0].Read(ref gb);
                  v  = V[1].Read(ref gb);

                  P[0][o_dst_y + (x    )] = (pred_L[0] = (y1 + ((3 * (pred_T[0] + pred_L[0]) - 2 * pred_TL[0]) >> 2)) & 0x3ff) & 0xFFFF;
                  P[1][o_dst_u + (x / 2)] = (pred_L[1] = (u + (((pred_L[1] - pred_TL[1]) >> 1) + pred_T[1])) & 0x3ff) & 0xFFFF;
                  P[0][o_dst_y + (x + 1)] = (pred_L[0] = (y2 + ((3 * (pred_T[3] + pred_L[0]) - 2 * pred_T[0]) >> 2)) & 0x3ff) & 0xFFFF;
                  P[2][o_dst_v + (x / 2)] = (pred_L[2] = (v + (((pred_L[2] - pred_TL[2]) >> 1) + pred_T[2])) & 0x3ff) & 0xFFFF;
                  P[3][o_dst_a + (x    )] = (pred_L[4] = (a1 + ((3 * (pred_T[4] + pred_L[4]) - 2 * pred_TL[4]) >> 2)) & 0x3ff) & 0xFFFF;
                  P[3][o_dst_a + (x + 1)] = (pred_L[4] = (a2 + ((3 * (pred_T[5] + pred_L[4]) - 2 * pred_T[4]) >> 2)) & 0x3ff) & 0xFFFF;

                  pred_TL[0] = pred_T[3];
                  pred_TL[1] = pred_T[1];
                  pred_TL[2] = pred_T[2];
                  pred_TL[4] = pred_T[5];
              }
          }

          o_dst_y += L[0];
          o_dst_u += L[1];
          o_dst_v += L[2];
          o_dst_a += L[3];
      }
  }

  /// <summary>FFmpeg's <c>decode_c82i</c>, transcribed mechanically.</summary>
  private static void DecodeC82i(int width, int height, bool alt, int[][] P, int[] L, HqxVlc[] V, ref HqxBitReader gb) {
      int x, row;

      var o_dst_y = 0;
      var o_dst_u = 0;
      var o_dst_v = 0;
      var o_dst_a = 0;

      for (row = 0; row < height; row++) {
          if (gb.Read(1) != 0) {
              for (x = 0; x < width; x += 2) {
                  P[3][o_dst_a + (x    )] = (gb.Read(8)) & 0xFF;
                  P[0][o_dst_y + (x    )] = (gb.Read(8)) & 0xFF;
                  P[1][o_dst_u + (x / 2)] = (gb.Read(8)) & 0xFF;
                  P[3][o_dst_a + (x + 1)] = (gb.Read(8)) & 0xFF;
                  P[0][o_dst_y + (x + 1)] = (gb.Read(8)) & 0xFF;
                  P[2][o_dst_v + (x / 2)] = (gb.Read(8)) & 0xFF;
              }
          } else {
              int[] pred = [125, -128, -128, 125];

              for (x = 0; x < width; x += 2) {
                  int y1, y2, u, v, a1, a2;

                  a1 = V[1].Read(ref gb);
                  y1 = V[0].Read(ref gb);
                  u  = V[1].Read(ref gb);
                  a2 = V[1].Read(ref gb);
                  y2 = V[0].Read(ref gb);
                  v  = V[1].Read(ref gb);

                  P[0][o_dst_y + (x    )] = (pred[0] = (y1 + pred[0]) & 0xff) & 0xFF;
                  P[0][o_dst_y + (x + 1)] = (pred[0] = (y2 + pred[0]) & 0xff) & 0xFF;
                  P[1][o_dst_u + (x / 2)] = (pred[1] = (u  + pred[1]) & 0xff) & 0xFF;
                  P[2][o_dst_v + (x / 2)] = (pred[2] = (v  + pred[2]) & 0xff) & 0xFF;
                  P[3][o_dst_a + (x    )] = (pred[3] = (a1 + pred[3]) & 0xff) & 0xFF;
                  P[3][o_dst_a + (x + 1)] = (pred[3] = (a2 + pred[3]) & 0xff) & 0xFF;
              }
          }

          o_dst_y += L[0];
          o_dst_u += L[1];
          o_dst_v += L[2];
          o_dst_a += L[3];
      }
  }

  /// <summary>FFmpeg's <c>decode_c82p</c>, transcribed mechanically.</summary>
  private static void DecodeC82p(int width, int height, bool alt, int[][] P, int[] L, HqxVlc[] V, ref HqxBitReader gb) {
      int x, row;

      var o_dst_y = 0;
      var o_dst_u = 0;
      var o_dst_v = 0;
      var o_dst_a = 0;

      if (gb.Read(1) != 0) {
          for (x = 0; x < width; x += 2) {
              P[3][o_dst_a + (x    )] = (gb.Read(8)) & 0xFF;
              P[0][o_dst_y + (x    )] = (gb.Read(8)) & 0xFF;
              P[1][o_dst_u + (x / 2)] = (gb.Read(8)) & 0xFF;
              P[3][o_dst_a + (x + 1)] = (gb.Read(8)) & 0xFF;
              P[0][o_dst_y + (x + 1)] = (gb.Read(8)) & 0xFF;
              P[2][o_dst_v + (x / 2)] = (gb.Read(8)) & 0xFF;
          }
      } else {
          int[] pred = [125, -128, -128, 125];

          for (x = 0; x < width; x += 2) {
              int y1, y2, u, v, a1, a2;

              a1 = V[1].Read(ref gb);
              y1 = V[0].Read(ref gb);
              u  = V[1].Read(ref gb);
              a2 = V[1].Read(ref gb);
              y2 = V[0].Read(ref gb);
              v  = V[1].Read(ref gb);

              P[0][o_dst_y + (x    )] = (pred[0] = (y1 + pred[0]) & 0xff) & 0xFF;
              P[1][o_dst_u + (x / 2)] = (pred[1] = (u  + pred[1]) & 0xff) & 0xFF;
              P[0][o_dst_y + (x + 1)] = (pred[0] = (y2 + pred[0]) & 0xff) & 0xFF;
              P[3][o_dst_a + (x    )] = (pred[3] = (a1 + pred[3]) & 0xff) & 0xFF;
              P[2][o_dst_v + (x / 2)] = (pred[2] = (v  + pred[2]) & 0xff) & 0xFF;
              P[3][o_dst_a + (x + 1)] = (pred[3] = (a2 + pred[3]) & 0xff) & 0xFF;
          }
      }

      o_dst_y += L[0];
      o_dst_u += L[1];
      o_dst_v += L[2];
      o_dst_a += L[3];

      for (row = 1; row < height; row++) {
          if (gb.Read(1) != 0) {
              for (x = 0; x < width; x += 2) {
                  P[3][o_dst_a + (x    )] = (gb.Read(8)) & 0xFF;
                  P[0][o_dst_y + (x    )] = (gb.Read(8)) & 0xFF;
                  P[1][o_dst_u + (x / 2)] = (gb.Read(8)) & 0xFF;
                  P[3][o_dst_a + (x + 1)] = (gb.Read(8)) & 0xFF;
                  P[0][o_dst_y + (x + 1)] = (gb.Read(8)) & 0xFF;
                  P[2][o_dst_v + (x / 2)] = (gb.Read(8)) & 0xFF;
              }
          } else {
              int[] pred_TL = new int[6], pred_L = new int[6], pred_T = new int[6];
              int y1, y2, u, v, a1, a2;

              pred_TL[0] = pred_L[0] = P[0][o_dst_y + (-L[0])];
              pred_TL[1] = pred_L[1] = P[1][o_dst_u + (-L[1])];
              pred_TL[2] = pred_L[2] = P[2][o_dst_v + (-L[2])];
              pred_TL[4] = pred_L[4] = P[3][o_dst_a + (-L[3])];

              for (x = 0; x < width; x += 2) {
                  pred_T[0] = P[0][o_dst_y + (-L[0] + x)];
                  pred_T[3] = P[0][o_dst_y + (-L[0] + x + 1)];
                  pred_T[1] = P[1][o_dst_u + (-L[1] + x / 2)];
                  pred_T[2] = P[2][o_dst_v + (-L[2] + x / 2)];
                  pred_T[4] = P[3][o_dst_a + (-L[3] + x)];
                  pred_T[5] = P[3][o_dst_a + (-L[3] + x + 1)];

                  a1 = V[1].Read(ref gb);
                  y1 = V[0].Read(ref gb);
                  u  = V[1].Read(ref gb);
                  a2 = V[1].Read(ref gb);
                  y2 = V[0].Read(ref gb);
                  v  = V[1].Read(ref gb);

                  P[0][o_dst_y + (x    )] = (pred_L[0] = (y1 + ((3 * (pred_T[0] + pred_L[0]) - 2 * pred_TL[0]) >> 2)) & 0xff) & 0xFF;
                  P[1][o_dst_u + (x / 2)] = (pred_L[1] = (u + (((pred_L[1] - pred_TL[1]) >> 1) + pred_T[1])) & 0xff) & 0xFF;
                  P[0][o_dst_y + (x + 1)] = (pred_L[0] = (y2 + ((3 * (pred_T[3] + pred_L[0]) - 2 * pred_T[0]) >> 2)) & 0xff) & 0xFF;
                  P[2][o_dst_v + (x / 2)] = (pred_L[2] = (v + (((pred_L[2] - pred_TL[2]) >> 1) + pred_T[2])) & 0xff) & 0xFF;
                  P[3][o_dst_a + (x    )] = (pred_L[4] = (a1 + ((3 * (pred_T[4] + pred_L[4]) - 2 * pred_TL[4]) >> 2)) & 0xff) & 0xFF;
                  P[3][o_dst_a + (x + 1)] = (pred_L[4] = (a2 + ((3 * (pred_T[5] + pred_L[4]) - 2 * pred_T[4]) >> 2)) & 0xff) & 0xFF;

                  pred_TL[0] = pred_T[3];
                  pred_TL[1] = pred_T[1];
                  pred_TL[2] = pred_T[2];
                  pred_TL[4] = pred_T[5];
              }
          }

          o_dst_y += L[0];
          o_dst_u += L[1];
          o_dst_v += L[2];
          o_dst_a += L[3];
      }
  }

  /// <summary>FFmpeg's <c>decode_ybyr</c>, transcribed mechanically.</summary>
  private static void DecodeYbyr(int width, int height, bool alt, int[][] P, int[] L, HqxVlc[] V, ref HqxBitReader gb) {
      int x, row;

      var o_dst_y = 0;
      var o_dst_u = 0;
      var o_dst_v = 0;

      if (gb.Read(1) != 0) {
          for (x = 0; x < width; x += 2) {
              P[0][o_dst_y + (x    )] = (gb.Read(8)) & 0xFF;
              P[1][o_dst_u + (x / 2)] = (gb.Read(8) + 128) & 0xFF;
              P[0][o_dst_y + (x + 1)] = (gb.Read(8)) & 0xFF;
              P[2][o_dst_v + (x / 2)] = (gb.Read(8) + 128) & 0xFF;
          }
      } else {
          int[] pred = [-128, 128, 128, 0];

          for (x = 0; x < width; x += 2) {
              int y1, y2, u, v;

              y1 = V[0].Read(ref gb);
              u  = V[1].Read(ref gb);
              y2 = V[0].Read(ref gb);
              v  = V[1].Read(ref gb);

              P[0][o_dst_y + (x    )] = (pred[0] = (y1 + pred[0]) & 0xff) & 0xFF;
              P[1][o_dst_u + (x / 2)] = (pred[1] = (u  + pred[1]) & 0xff) & 0xFF;
              P[0][o_dst_y + (x + 1)] = (pred[0] = (y2 + pred[0]) & 0xff) & 0xFF;
              P[2][o_dst_v + (x / 2)] = (pred[2] = (v  + pred[2]) & 0xff) & 0xFF;
          }
      }

      o_dst_y += L[0];
      o_dst_u += L[1];
      o_dst_v += L[2];

      for (row = 1; row < height; row++) {
          if (gb.Read(1) != 0) {
              for (x = 0; x < width; x += 2) {
                  P[0][o_dst_y + (x    )] = (gb.Read(8)) & 0xFF;
                  P[1][o_dst_u + (x / 2)] = (gb.Read(8) + 128) & 0xFF;
                  P[0][o_dst_y + (x + 1)] = (gb.Read(8)) & 0xFF;
                  P[2][o_dst_v + (x / 2)] = (gb.Read(8) + 128) & 0xFF;
              }
          } else {
              int[] pred_TL = new int[4], pred_L = new int[4], pred_T = new int[4];
              int y1, y2, u, v;

              pred_TL[0] = pred_L[0] = P[0][o_dst_y + (-L[0])];
              pred_TL[1] = pred_L[1] = P[1][o_dst_u + (-L[1])];
              pred_TL[2] = pred_L[2] = P[2][o_dst_v + (-L[2])];

              for (x = 0; x < width; x += 2) {
                  pred_T[0] = P[0][o_dst_y + (-L[0] + x)];
                  pred_T[3] = P[0][o_dst_y + (-L[0] + x + 1)];
                  pred_T[1] = P[1][o_dst_u + (-L[1] + x / 2)];
                  pred_T[2] = P[2][o_dst_v + (-L[2] + x / 2)];

                  y1 = V[0].Read(ref gb);
                  u  = V[1].Read(ref gb);
                  y2 = V[0].Read(ref gb);
                  v  = V[1].Read(ref gb);

                  P[0][o_dst_y + (x    )] = (pred_L[0] = (y1 + ((3 * (pred_T[0] + pred_L[0]) - 2 * pred_TL[0]) >> 2)) & 0xff) & 0xFF;
                  P[1][o_dst_u + (x / 2)] = (pred_L[1] = (u + (((pred_L[1] - pred_TL[1]) >> 1) + pred_T[1])) & 0xff) & 0xFF;
                  P[0][o_dst_y + (x + 1)] = (pred_L[0] = (y2 + ((3 * (pred_T[3] + pred_L[0]) - 2 * pred_T[0]) >> 2)) & 0xff) & 0xFF;
                  P[2][o_dst_v + (x / 2)] = (pred_L[2] = (v + (((pred_L[2] - pred_TL[2]) >> 1) + pred_T[2])) & 0xff) & 0xFF;

                  pred_TL[0] = pred_T[3];
                  pred_TL[1] = pred_T[1];
                  pred_TL[2] = pred_T[2];
              }
          }

          o_dst_y += L[0];
          o_dst_u += L[1];
          o_dst_v += L[2];
      }
  }

  /// <summary>FFmpeg's <c>decode_byryi</c>, transcribed mechanically.</summary>
  private static void DecodeByryi(int width, int height, bool alt, int[][] P, int[] L, HqxVlc[] V, ref HqxBitReader gb) {
      int x, row;

      var o_dst_y = 0;
      var o_dst_u = 0;
      var o_dst_v = 0;

      if (gb.Read(1) != 0) {
          for (x = 0; x < width; x += 2) {
              P[0][o_dst_y + (x    )] = (gb.Read(8)) & 0xFF;
              P[1][o_dst_u + (x / 2)] = (gb.Read(8)) & 0xFF;
              P[0][o_dst_y + (x + 1)] = (gb.Read(8)) & 0xFF;
              P[2][o_dst_v + (x / 2)] = (gb.Read(8)) & 0xFF;
          }
      } else {
          int[] pred = [125, -128, -128, 0];

          for (x = 0; x < width; x += 2) {
              int y1, y2, u, v;

              y1 = V[0].Read(ref gb);
              u  = V[1].Read(ref gb);
              y2 = V[0].Read(ref gb);
              v  = V[1].Read(ref gb);

              P[0][o_dst_y + (x    )] = (pred[0] = (y1 + pred[0]) & 0xff) & 0xFF;
              P[1][o_dst_u + (x / 2)] = (pred[1] = (u  + pred[1]) & 0xff) & 0xFF;
              P[0][o_dst_y + (x + 1)] = (pred[0] = (y2 + pred[0]) & 0xff) & 0xFF;
              P[2][o_dst_v + (x / 2)] = (pred[2] = (v  + pred[2]) & 0xff) & 0xFF;
          }
      }

      o_dst_y += L[0];
      o_dst_u += L[1];
      o_dst_v += L[2];

      for (row = 1; row < height; row++) {
          if (gb.Read(1) != 0) {
              for (x = 0; x < width; x += 2) {
                  P[0][o_dst_y + (x    )] = (gb.Read(8)) & 0xFF;
                  P[1][o_dst_u + (x / 2)] = (gb.Read(8)) & 0xFF;
                  P[0][o_dst_y + (x + 1)] = (gb.Read(8)) & 0xFF;
                  P[2][o_dst_v + (x / 2)] = (gb.Read(8)) & 0xFF;
              }
          } else {
              int[] pred_L = new int[4];
              int y1, y2, u, v;

              pred_L[0] = P[0][o_dst_y + (-L[0])];
              pred_L[1] = P[1][o_dst_u + (-L[1])];
              pred_L[2] = P[2][o_dst_v + (-L[2])];

              for (x = 0; x < width; x += 2) {
                  y1 = V[0].Read(ref gb);
                  u  = V[1].Read(ref gb);
                  y2 = V[0].Read(ref gb);
                  v  = V[1].Read(ref gb);

                  P[0][o_dst_y + (x    )] = (pred_L[0] = (y1 + pred_L[0]) & 0xff) & 0xFF;
                  P[1][o_dst_u + (x / 2)] = (pred_L[1] = (u  + pred_L[1]) & 0xff) & 0xFF;
                  P[0][o_dst_y + (x + 1)] = (pred_L[0] = (y2 + pred_L[0]) & 0xff) & 0xFF;
                  P[2][o_dst_v + (x / 2)] = (pred_L[2] = (v +  pred_L[2]) & 0xff) & 0xFF;
              }
          }

          o_dst_y += L[0];
          o_dst_u += L[1];
          o_dst_v += L[2];
      }
  }

  /// <summary>FFmpeg's <c>decode_byry</c>, transcribed mechanically.</summary>
  private static void DecodeByry(int width, int height, bool alt, int[][] P, int[] L, HqxVlc[] V, ref HqxBitReader gb) {
      int x, row;

      var o_dst_y = 0;
      var o_dst_u = 0;
      var o_dst_v = 0;

      if (gb.Read(1) != 0) {
          for (x = 0; x < width; x += 2) {
              P[0][o_dst_y + (x    )] = (gb.Read(8)) & 0xFF;
              P[1][o_dst_u + (x / 2)] = (gb.Read(8)) & 0xFF;
              P[0][o_dst_y + (x + 1)] = (gb.Read(8)) & 0xFF;
              P[2][o_dst_v + (x / 2)] = (gb.Read(8)) & 0xFF;
          }
      } else {
          int[] pred = [125, -128, -128, 0];

          for (x = 0; x < width; x += 2) {
              int y1, y2, u, v;

              y1 = V[0].Read(ref gb);
              u  = V[1].Read(ref gb);
              y2 = V[0].Read(ref gb);
              v  = V[1].Read(ref gb);

              P[0][o_dst_y + (x    )] = (pred[0] = (y1 + pred[0]) & 0xff) & 0xFF;
              P[1][o_dst_u + (x / 2)] = (pred[1] = (u  + pred[1]) & 0xff) & 0xFF;
              P[0][o_dst_y + (x + 1)] = (pred[0] = (y2 + pred[0]) & 0xff) & 0xFF;
              P[2][o_dst_v + (x / 2)] = (pred[2] = (v  + pred[2]) & 0xff) & 0xFF;
          }
      }

      o_dst_y += L[0];
      o_dst_u += L[1];
      o_dst_v += L[2];

      for (row = 1; row < height; row++) {
          if (gb.Read(1) != 0) {
              for (x = 0; x < width; x += 2) {
                  P[0][o_dst_y + (x    )] = (gb.Read(8)) & 0xFF;
                  P[1][o_dst_u + (x / 2)] = (gb.Read(8)) & 0xFF;
                  P[0][o_dst_y + (x + 1)] = (gb.Read(8)) & 0xFF;
                  P[2][o_dst_v + (x / 2)] = (gb.Read(8)) & 0xFF;
              }
          } else {
              int[] pred_TL = new int[4], pred_L = new int[4], pred_T = new int[4];
              int y1, y2, u, v;

              pred_TL[0] = pred_L[0] = P[0][o_dst_y + (-L[0])];
              pred_TL[1] = pred_L[1] = P[1][o_dst_u + (-L[1])];
              pred_TL[2] = pred_L[2] = P[2][o_dst_v + (-L[2])];

              for (x = 0; x < width; x += 2) {
                  pred_T[0] = P[0][o_dst_y + (-L[0] + x)];
                  pred_T[3] = P[0][o_dst_y + (-L[0] + x + 1)];
                  pred_T[1] = P[1][o_dst_u + (-L[1] + x / 2)];
                  pred_T[2] = P[2][o_dst_v + (-L[2] + x / 2)];

                  y1 = V[0].Read(ref gb);
                  u  = V[1].Read(ref gb);
                  y2 = V[0].Read(ref gb);
                  v  = V[1].Read(ref gb);

                  P[0][o_dst_y + (x    )] = (pred_L[0] = (y1 + ((3 * (pred_T[0] + pred_L[0]) - 2 * pred_TL[0]) >> 2)) & 0xff) & 0xFF;
                  P[1][o_dst_u + (x / 2)] = (pred_L[1] = (u + (((pred_L[1] - pred_TL[1]) >> 1) + pred_T[1])) & 0xff) & 0xFF;
                  P[0][o_dst_y + (x + 1)] = (pred_L[0] = (y2 + ((3 * (pred_T[3] + pred_L[0]) - 2 * pred_T[0]) >> 2)) & 0xff) & 0xFF;
                  P[2][o_dst_v + (x / 2)] = (pred_L[2] = (v + (((pred_L[2] - pred_TL[2]) >> 1) + pred_T[2])) & 0xff) & 0xFF;

                  pred_TL[0] = pred_T[3];
                  pred_TL[1] = pred_T[1];
                  pred_TL[2] = pred_T[2];
              }
          }

          o_dst_y += L[0];
          o_dst_u += L[1];
          o_dst_v += L[2];
      }
  }

  /// <summary>FFmpeg's <c>decode_ybri</c>, transcribed mechanically.</summary>
  private static void DecodeYbri(int width, int height, bool alt, int[][] P, int[] L, HqxVlc[] V, ref HqxBitReader gb) {
      int x, row;

      var o_dst_y = 0;
      var o_dst_u = 0;
      var o_dst_v = 0;

      if (gb.Read(1) != 0) {
          for (x = 0; x < width; x++) {
              P[0][o_dst_y + (x)] = (gb.Read(8)) & 0xFF;
              P[1][o_dst_u + (x)] = (gb.Read(8)) & 0xFF;
              P[2][o_dst_v + (x)] = (gb.Read(8)) & 0xFF;
          }
      } else {
          int[] pred = [alt ? 125 : -146, -128, -128, -128];

          for (x = 0; x < width; x++) {
              int y, u, v;

              y = V[0].Read(ref gb);
              u = V[1].Read(ref gb);
              v = V[1].Read(ref gb);

              P[0][o_dst_y + (x)] = (pred[0] = (y + pred[0]) & 0xff) & 0xFF;
              P[1][o_dst_u + (x)] = (pred[1] = (u + pred[1]) & 0xff) & 0xFF;
              P[2][o_dst_v + (x)] = (pred[2] = (v + pred[2]) & 0xff) & 0xFF;
          }
      }

      o_dst_y += L[0];
      o_dst_u += L[1];
      o_dst_v += L[2];

      for (row = 1; row < height; row++) {
          if (gb.Read(1) != 0) {
              for (x = 0; x < width; x++) {
                  P[0][o_dst_y + (x)] = (gb.Read(8)) & 0xFF;
                  P[1][o_dst_u + (x)] = (gb.Read(8)) & 0xFF;
                  P[2][o_dst_v + (x)] = (gb.Read(8)) & 0xFF;
              }
          } else {
              int[] pred_L = new int[4];
              int y, u, v;

              pred_L[0] = P[0][o_dst_y + (-L[0])];
              pred_L[1] = P[1][o_dst_u + (-L[1])];
              pred_L[2] = P[2][o_dst_v + (-L[2])];

              for (x = 0; x < width; x++) {
                  y = V[0].Read(ref gb);
                  u = V[1].Read(ref gb);
                  v = V[1].Read(ref gb);

                  P[0][o_dst_y + (x)] = (pred_L[0] = (y + pred_L[0]) & 0xff) & 0xFF;
                  P[1][o_dst_u + (x)] = (pred_L[1] = (u + pred_L[1]) & 0xff) & 0xFF;
                  P[2][o_dst_v + (x)] = (pred_L[2] = (v + pred_L[2]) & 0xff) & 0xFF;
              }
          }

          o_dst_y += L[0];
          o_dst_u += L[1];
          o_dst_v += L[2];
      }
  }

  /// <summary>FFmpeg's <c>decode_ybr</c>, transcribed mechanically.</summary>
  private static void DecodeYbr(int width, int height, bool alt, int[][] P, int[] L, HqxVlc[] V, ref HqxBitReader gb) {
      int x, row;

      var o_dst_y = 0;
      var o_dst_u = 0;
      var o_dst_v = 0;

      if (gb.Read(1) != 0) {
          for (x = 0; x < width; x++) {
              P[0][o_dst_y + (x)] = (gb.Read(8)) & 0xFF;
              P[1][o_dst_u + (x)] = (gb.Read(8)) & 0xFF;
              P[2][o_dst_v + (x)] = (gb.Read(8)) & 0xFF;
          }
      } else {
          int[] pred = [alt ? 125 : -146, -128, -128, -128];

          for (x = 0; x < width; x++) {
              int y, u, v;

              y = V[0].Read(ref gb);
              u = V[1].Read(ref gb);
              v = V[1].Read(ref gb);

              P[0][o_dst_y + (x)] = (pred[0] = (y + pred[0]) & 0xff) & 0xFF;
              P[1][o_dst_u + (x)] = (pred[1] = (u + pred[1]) & 0xff) & 0xFF;
              P[2][o_dst_v + (x)] = (pred[2] = (v + pred[2]) & 0xff) & 0xFF;
          }
      }

      o_dst_y += L[0];
      o_dst_u += L[1];
      o_dst_v += L[2];

      for (row = 1; row < height; row++) {
          if (gb.Read(1) != 0) {
              for (x = 0; x < width; x++) {
                  P[0][o_dst_y + (x)] = (gb.Read(8)) & 0xFF;
                  P[1][o_dst_u + (x)] = (gb.Read(8)) & 0xFF;
                  P[2][o_dst_v + (x)] = (gb.Read(8)) & 0xFF;
              }
          } else {
              int[] pred_TL = new int[4], pred_L = new int[4], pred_T = new int[4];
              int y, u, v;

              pred_TL[0] = pred_L[0] = P[0][o_dst_y + (-L[0])];
              pred_TL[1] = pred_L[1] = P[1][o_dst_u + (-L[1])];
              pred_TL[2] = pred_L[2] = P[2][o_dst_v + (-L[2])];

              for (x = 0; x < width; x++) {
                  pred_T[0] = P[0][o_dst_y + (-L[0] + x)];
                  pred_T[1] = P[1][o_dst_u + (-L[1] + x)];
                  pred_T[2] = P[2][o_dst_v + (-L[2] + x)];

                  y = V[0].Read(ref gb);
                  u = V[1].Read(ref gb);
                  v = V[1].Read(ref gb);

                  P[0][o_dst_y + (x)] = (pred_L[0] = (y + ((3 * (pred_T[0] + pred_L[0]) - 2 * pred_TL[0]) >> 2)) & 0xff) & 0xFF;
                  P[1][o_dst_u + (x)] = (pred_L[1] = (u + ((3 * (pred_T[1] + pred_L[1]) - 2 * pred_TL[1]) >> 2)) & 0xff) & 0xFF;
                  P[2][o_dst_v + (x)] = (pred_L[2] = (v + ((3 * (pred_T[2] + pred_L[2]) - 2 * pred_TL[2]) >> 2)) & 0xff) & 0xFF;

                  pred_TL[0] = pred_T[0];
                  pred_TL[1] = pred_T[1];
                  pred_TL[2] = pred_T[2];
              }
          }

          o_dst_y += L[0];
          o_dst_u += L[1];
          o_dst_v += L[2];
      }
  }

  /// <summary>FFmpeg's <c>decode_aybri</c>, transcribed mechanically.</summary>
  private static void DecodeAybri(int width, int height, bool alt, int[][] P, int[] L, HqxVlc[] V, ref HqxBitReader gb) {
      int x, row;

      var o_dst_a = 0;
      var o_dst_y = 0;
      var o_dst_u = 0;
      var o_dst_v = 0;

      if (gb.Read(1) != 0) {
          for (x = 0; x < width; x++) {
              P[3][o_dst_a + (x)] = (gb.Read(8)) & 0xFF;
              P[0][o_dst_y + (x)] = (gb.Read(8)) & 0xFF;
              P[1][o_dst_u + (x)] = (gb.Read(8)) & 0xFF;
              P[2][o_dst_v + (x)] = (gb.Read(8)) & 0xFF;
          }
      } else {
          int[] pred = [125, alt ? 125 : -146, -128, -128];

          for (x = 0; x < width; x++) {
              int a, y, u, v;

              a = V[1].Read(ref gb);
              y = V[0].Read(ref gb);
              u = V[1].Read(ref gb);
              v = V[1].Read(ref gb);

              P[3][o_dst_a + (x)] = (pred[0] = (a + pred[0]) & 0xff) & 0xFF;
              P[0][o_dst_y + (x)] = (pred[1] = (y + pred[1]) & 0xff) & 0xFF;
              P[1][o_dst_u + (x)] = (pred[2] = (u + pred[2]) & 0xff) & 0xFF;
              P[2][o_dst_v + (x)] = (pred[3] = (v + pred[3]) & 0xff) & 0xFF;
          }
      }

      o_dst_a += L[3];
      o_dst_y += L[0];
      o_dst_u += L[1];
      o_dst_v += L[2];

      for (row = 1; row < height; row++) {
          if (gb.Read(1) != 0) {
              for (x = 0; x < width; x++) {
                  P[3][o_dst_a + (x)] = (gb.Read(8)) & 0xFF;
                  P[0][o_dst_y + (x)] = (gb.Read(8)) & 0xFF;
                  P[1][o_dst_u + (x)] = (gb.Read(8)) & 0xFF;
                  P[2][o_dst_v + (x)] = (gb.Read(8)) & 0xFF;
              }
          } else {
              int[] pred_L = new int[4];
              int a, y, u, v;

              pred_L[0] = P[3][o_dst_a + (-L[3])];
              pred_L[1] = P[0][o_dst_y + (-L[0])];
              pred_L[2] = P[1][o_dst_u + (-L[1])];
              pred_L[3] = P[2][o_dst_v + (-L[2])];

              for (x = 0; x < width; x++) {
                  a = V[1].Read(ref gb);
                  y = V[0].Read(ref gb);
                  u = V[1].Read(ref gb);
                  v = V[1].Read(ref gb);

                  P[3][o_dst_a + (x)] = (pred_L[0] = (a + pred_L[0]) & 0xff) & 0xFF;
                  P[0][o_dst_y + (x)] = (pred_L[1] = (y + pred_L[1]) & 0xff) & 0xFF;
                  P[1][o_dst_u + (x)] = (pred_L[2] = (u + pred_L[2]) & 0xff) & 0xFF;
                  P[2][o_dst_v + (x)] = (pred_L[3] = (v + pred_L[3]) & 0xff) & 0xFF;
              }
          }

          o_dst_a += L[3];
          o_dst_y += L[0];
          o_dst_u += L[1];
          o_dst_v += L[2];
      }
  }

  /// <summary>FFmpeg's <c>decode_aybr</c>, transcribed mechanically.</summary>
  private static void DecodeAybr(int width, int height, bool alt, int[][] P, int[] L, HqxVlc[] V, ref HqxBitReader gb) {
      int x, row;

      var o_dst_a = 0;
      var o_dst_y = 0;
      var o_dst_u = 0;
      var o_dst_v = 0;

      if (gb.Read(1) != 0) {
          for (x = 0; x < width; x++) {
              P[3][o_dst_a + (x)] = (gb.Read(8)) & 0xFF;
              P[0][o_dst_y + (x)] = (gb.Read(8)) & 0xFF;
              P[1][o_dst_u + (x)] = (gb.Read(8)) & 0xFF;
              P[2][o_dst_v + (x)] = (gb.Read(8)) & 0xFF;
          }
      } else {
          int[] pred = [125, alt ? 125 : -146, -128, -128];

          for (x = 0; x < width; x++) {
              int a, y, u, v;

              a = V[1].Read(ref gb);
              y = V[0].Read(ref gb);
              u = V[1].Read(ref gb);
              v = V[1].Read(ref gb);

              P[3][o_dst_a + (x)] = (pred[0] = (a + pred[0]) & 0xff) & 0xFF;
              P[0][o_dst_y + (x)] = (pred[1] = (y + pred[1]) & 0xff) & 0xFF;
              P[1][o_dst_u + (x)] = (pred[2] = (u + pred[2]) & 0xff) & 0xFF;
              P[2][o_dst_v + (x)] = (pred[3] = (v + pred[3]) & 0xff) & 0xFF;
          }
      }

      o_dst_a += L[3];
      o_dst_y += L[0];
      o_dst_u += L[1];
      o_dst_v += L[2];

      for (row = 1; row < height; row++) {
          if (gb.Read(1) != 0) {
              for (x = 0; x < width; x++) {
                  P[3][o_dst_a + (x)] = (gb.Read(8)) & 0xFF;
                  P[0][o_dst_y + (x)] = (gb.Read(8)) & 0xFF;
                  P[1][o_dst_u + (x)] = (gb.Read(8)) & 0xFF;
                  P[2][o_dst_v + (x)] = (gb.Read(8)) & 0xFF;
              }
          } else {
              int[] pred_TL = new int[4], pred_L = new int[4], pred_T = new int[4];
              int a, y, u, v;

              pred_TL[0] = pred_L[0] = P[3][o_dst_a + (-L[3])];
              pred_TL[1] = pred_L[1] = P[0][o_dst_y + (-L[0])];
              pred_TL[2] = pred_L[2] = P[1][o_dst_u + (-L[1])];
              pred_TL[3] = pred_L[3] = P[2][o_dst_v + (-L[2])];

              for (x = 0; x < width; x++) {
                  pred_T[0] = P[3][o_dst_a + (-L[3] + x)];
                  pred_T[1] = P[0][o_dst_y + (-L[0] + x)];
                  pred_T[2] = P[1][o_dst_u + (-L[1] + x)];
                  pred_T[3] = P[2][o_dst_v + (-L[2] + x)];

                  a = V[1].Read(ref gb);
                  y = V[0].Read(ref gb);
                  u = V[1].Read(ref gb);
                  v = V[1].Read(ref gb);

                  P[3][o_dst_a + (x)] = (pred_L[0] = (a + ((3 * (pred_T[0] + pred_L[0]) - 2 * pred_TL[0]) >> 2)) & 0xff) & 0xFF;
                  P[0][o_dst_y + (x)] = (pred_L[1] = (y + ((3 * (pred_T[1] + pred_L[1]) - 2 * pred_TL[1]) >> 2)) & 0xff) & 0xFF;
                  P[1][o_dst_u + (x)] = (pred_L[2] = (u + ((3 * (pred_T[2] + pred_L[2]) - 2 * pred_TL[2]) >> 2)) & 0xff) & 0xFF;
                  P[2][o_dst_v + (x)] = (pred_L[3] = (v + ((3 * (pred_T[3] + pred_L[3]) - 2 * pred_TL[3]) >> 2)) & 0xff) & 0xFF;

                  pred_TL[0] = pred_T[0];
                  pred_TL[1] = pred_T[1];
                  pred_TL[2] = pred_T[2];
                  pred_TL[3] = pred_T[3];
              }
          }

          o_dst_a += L[3];
          o_dst_y += L[0];
          o_dst_u += L[1];
          o_dst_v += L[2];
      }
  }

  /// <summary>FFmpeg's <c>decode_argxi</c>, transcribed mechanically.</summary>
  private static void DecodeArgxi(int width, int height, bool alt, int[][] P, int[] L, HqxVlc[] V, ref HqxBitReader gb) {
      int x, row;

      var o_dst_r = 0;
      var o_dst_g = 0;
      var o_dst_b = 0;
      var o_dst_a = 0;

      for (row = 0; row < height; row++) {
          if (gb.Read(1) != 0) {
              for (x = 0; x < width; x++) {
                  P[3][o_dst_a + (x)] = (gb.Read(10)) & 0xFFFF;
                  P[2][o_dst_r + (x)] = (gb.Read(10)) & 0xFFFF;
                  P[0][o_dst_g + (x)] = (gb.Read(10)) & 0xFFFF;
                  P[1][o_dst_b + (x)] = (gb.Read(10)) & 0xFFFF;
              }
          } else {
              int[] pred = [512, 512, 512, 512];

              for (x = 0; x < width; x++) {
                  int r, g, b, a;

                  a = V[1].Read(ref gb);
                  r = V[0].Read(ref gb);
                  g = V[1].Read(ref gb);
                  b = V[1].Read(ref gb);

                  P[3][o_dst_a + (x)] = (pred[3] = (a + pred[3]) & 0x3ff) & 0xFFFF;
                  P[2][o_dst_r + (x)] = (pred[0] = (r + pred[0]) & 0x3ff) & 0xFFFF;
                  P[0][o_dst_g + (x)] = (pred[1] = (r + g + pred[1]) & 0x3ff) & 0xFFFF;
                  P[1][o_dst_b + (x)] = (pred[2] = (r + g + b + pred[2]) & 0x3ff) & 0xFFFF;
              }
          }

          o_dst_r += L[2];
          o_dst_g += L[0];
          o_dst_b += L[1];
          o_dst_a += L[3];
      }
  }

  /// <summary>FFmpeg's <c>decode_argx</c>, transcribed mechanically.</summary>
  private static void DecodeArgx(int width, int height, bool alt, int[][] P, int[] L, HqxVlc[] V, ref HqxBitReader gb) {
      int x, row;

      var o_dst_r = 0;
      var o_dst_g = 0;
      var o_dst_b = 0;
      var o_dst_a = 0;

      if (gb.Read(1) != 0) {
          for (x = 0; x < width; x++) {
              P[3][o_dst_a + (x)] = (gb.Read(10)) & 0xFFFF;
              P[2][o_dst_r + (x)] = (gb.Read(10)) & 0xFFFF;
              P[0][o_dst_g + (x)] = (gb.Read(10)) & 0xFFFF;
              P[1][o_dst_b + (x)] = (gb.Read(10)) & 0xFFFF;
          }
      } else {
          int[] pred = [512, 512, 512, 512];

          for (x = 0; x < width; x++) {
              int r, g, b, a;

              a = V[1].Read(ref gb);
              r = V[0].Read(ref gb);
              g = V[1].Read(ref gb);
              b = V[1].Read(ref gb);

              P[3][o_dst_a + (x)] = (pred[3] = (a + pred[3]) & 0x3ff) & 0xFFFF;
              P[2][o_dst_r + (x)] = (pred[0] = (r + pred[0]) & 0x3ff) & 0xFFFF;
              P[0][o_dst_g + (x)] = (pred[1] = (r + g + pred[1]) & 0x3ff) & 0xFFFF;
              P[1][o_dst_b + (x)] = (pred[2] = (r + g + b + pred[2]) & 0x3ff) & 0xFFFF;
          }
      }

      o_dst_r += L[2];
      o_dst_g += L[0];
      o_dst_b += L[1];
      o_dst_a += L[3];

      for (row = 1; row < height; row++) {
          if (gb.Read(1) != 0) {
              for (x = 0; x < width; x++) {
                  P[3][o_dst_a + (x)] = (gb.Read(10)) & 0xFFFF;
                  P[2][o_dst_r + (x)] = (gb.Read(10)) & 0xFFFF;
                  P[0][o_dst_g + (x)] = (gb.Read(10)) & 0xFFFF;
                  P[1][o_dst_b + (x)] = (gb.Read(10)) & 0xFFFF;
              }
          } else {
              int[] pred_TL = new int[4], pred_L = new int[4], pred_T = new int[4];
              int r, g, b, a;

              pred_TL[0] = pred_L[0] = P[2][o_dst_r + (-L[2])];
              pred_TL[1] = pred_L[1] = P[0][o_dst_g + (-L[0])];
              pred_TL[2] = pred_L[2] = P[1][o_dst_b + (-L[1])];
              pred_TL[3] = pred_L[3] = P[3][o_dst_a + (-L[3])];

              for (x = 0; x < width; x++) {
                  pred_T[0] = P[2][o_dst_r + (-L[2] + x)];
                  pred_T[1] = P[0][o_dst_g + (-L[0] + x)];
                  pred_T[2] = P[1][o_dst_b + (-L[1] + x)];
                  pred_T[3] = P[3][o_dst_a + (-L[3] + x)];

                  a = V[1].Read(ref gb);
                  r = V[0].Read(ref gb);
                  g = V[1].Read(ref gb);
                  b = V[1].Read(ref gb);

                  P[3][o_dst_a + (x)] = (pred_L[3] = (a + ((3 * (pred_T[3] + pred_L[3]) - 2 * pred_TL[3]) >> 2)) & 0x3ff) & 0xFFFF;
                  P[2][o_dst_r + (x)] = (pred_L[0] = (r + ((3 * (pred_T[0] + pred_L[0]) - 2 * pred_TL[0]) >> 2)) & 0x3ff) & 0xFFFF;
                  P[0][o_dst_g + (x)] = (pred_L[1] = (r + g + ((3 * (pred_T[1] + pred_L[1]) - 2 * pred_TL[1]) >> 2)) & 0x3ff) & 0xFFFF;
                  P[1][o_dst_b + (x)] = (pred_L[2] = (r + g + b + ((3 * (pred_T[2] + pred_L[2]) - 2 * pred_TL[2]) >> 2)) & 0x3ff) & 0xFFFF;

                  pred_TL[0] = pred_T[0];
                  pred_TL[1] = pred_T[1];
                  pred_TL[2] = pred_T[2];
                  pred_TL[3] = pred_T[3];
              }
          }

          o_dst_r += L[2];
          o_dst_g += L[0];
          o_dst_b += L[1];
          o_dst_a += L[3];
      }
  }

  /// <summary>FFmpeg's <c>decode_rgbxi</c>, transcribed mechanically.</summary>
  private static void DecodeRgbxi(int width, int height, bool alt, int[][] P, int[] L, HqxVlc[] V, ref HqxBitReader gb) {
      int x, row;

      var o_dst_r = 0;
      var o_dst_g = 0;
      var o_dst_b = 0;

      for (row = 0; row < height; row++) {
          if (gb.Read(1) != 0) {
              for (x = 0; x < width; x++) {
                  P[2][o_dst_r + (x)] = (gb.Read(10)) & 0xFFFF;
                  P[0][o_dst_g + (x)] = (gb.Read(10)) & 0xFFFF;
                  P[1][o_dst_b + (x)] = (gb.Read(10)) & 0xFFFF;
              }
          } else {
              int[] pred = [512, 512, 512, 0];

              for (x = 0; x < width; x++) {
                  int r, g, b;

                  r = V[0].Read(ref gb);
                  g = V[1].Read(ref gb);
                  b = V[1].Read(ref gb);

                  P[2][o_dst_r + (x)] = (pred[0] = (r + pred[0]) & 0x3ff) & 0xFFFF;
                  P[0][o_dst_g + (x)] = (pred[1] = (r + g + pred[1]) & 0x3ff) & 0xFFFF;
                  P[1][o_dst_b + (x)] = (pred[2] = (r + g + b + pred[2]) & 0x3ff) & 0xFFFF;
              }
          }

          o_dst_r += L[2];
          o_dst_g += L[0];
          o_dst_b += L[1];
      }
  }

  /// <summary>FFmpeg's <c>decode_rgbx</c>, transcribed mechanically.</summary>
  private static void DecodeRgbx(int width, int height, bool alt, int[][] P, int[] L, HqxVlc[] V, ref HqxBitReader gb) {
      int x, row;

      var o_dst_r = 0;
      var o_dst_g = 0;
      var o_dst_b = 0;

      if (gb.Read(1) != 0) {
          for (x = 0; x < width; x++) {
              P[2][o_dst_r + (x)] = (gb.Read(10)) & 0xFFFF;
              P[0][o_dst_g + (x)] = (gb.Read(10)) & 0xFFFF;
              P[1][o_dst_b + (x)] = (gb.Read(10)) & 0xFFFF;
          }
      } else {
          int[] pred = [512, 512, 512, 0];

          for (x = 0; x < width; x++) {
              int r, g, b;

              r = V[0].Read(ref gb);
              g = V[1].Read(ref gb);
              b = V[1].Read(ref gb);

              P[2][o_dst_r + (x)] = (pred[0] = (r + pred[0]) & 0x3ff) & 0xFFFF;
              P[0][o_dst_g + (x)] = (pred[1] = (r + g + pred[1]) & 0x3ff) & 0xFFFF;
              P[1][o_dst_b + (x)] = (pred[2] = (r + g + b + pred[2]) & 0x3ff) & 0xFFFF;
          }
      }

      o_dst_r += L[2];
      o_dst_g += L[0];
      o_dst_b += L[1];

      for (row = 1; row < height; row++) {
          if (gb.Read(1) != 0) {
              for (x = 0; x < width; x++) {
                  P[2][o_dst_r + (x)] = (gb.Read(10)) & 0xFFFF;
                  P[0][o_dst_g + (x)] = (gb.Read(10)) & 0xFFFF;
                  P[1][o_dst_b + (x)] = (gb.Read(10)) & 0xFFFF;
              }
          } else {
              int[] pred_TL = new int[4], pred_L = new int[4], pred_T = new int[4];
              int r, g, b;

              pred_TL[0] = pred_L[0] = P[2][o_dst_r + (-L[2])];
              pred_TL[1] = pred_L[1] = P[0][o_dst_g + (-L[0])];
              pred_TL[2] = pred_L[2] = P[1][o_dst_b + (-L[1])];

              for (x = 0; x < width; x++) {
                  pred_T[0] = P[2][o_dst_r + (-L[2] + x)];
                  pred_T[1] = P[0][o_dst_g + (-L[0] + x)];
                  pred_T[2] = P[1][o_dst_b + (-L[1] + x)];

                  r = V[0].Read(ref gb);
                  g = V[1].Read(ref gb);
                  b = V[1].Read(ref gb);

                  P[2][o_dst_r + (x)] = (pred_L[0] = (r + ((3 * (pred_T[0] + pred_L[0]) - 2 * pred_TL[0]) >> 2)) & 0x3ff) & 0xFFFF;
                  P[0][o_dst_g + (x)] = (pred_L[1] = (r + g + ((3 * (pred_T[1] + pred_L[1]) - 2 * pred_TL[1]) >> 2)) & 0x3ff) & 0xFFFF;
                  P[1][o_dst_b + (x)] = (pred_L[2] = (r + g + b + ((3 * (pred_T[2] + pred_L[2]) - 2 * pred_TL[2]) >> 2)) & 0x3ff) & 0xFFFF;

                  pred_TL[0] = pred_T[0];
                  pred_TL[1] = pred_T[1];
                  pred_TL[2] = pred_T[2];
              }
          }

          o_dst_r += L[2];
          o_dst_g += L[0];
          o_dst_b += L[1];
      }
  }

  /// <summary>FFmpeg's <c>decode_argbi</c>, transcribed mechanically.</summary>
  private static void DecodeArgbi(int width, int height, bool alt, int[][] P, int[] L, HqxVlc[] V, ref HqxBitReader gb) {
      int x, row;

      var o_dst = 0;
      if (gb.Read(1) != 0) {
          for (x = 0; x < width; x++) {
              P[0][o_dst + (x * 4 + 0)] = (gb.Read(8)) & 0xFF;
              P[0][o_dst + (x * 4 + 1)] = (gb.Read(8)) & 0xFF;
              P[0][o_dst + (x * 4 + 2)] = (gb.Read(8)) & 0xFF;
              P[0][o_dst + (x * 4 + 3)] = (gb.Read(8)) & 0xFF;
          }
      } else {
          int[] pred = [-128, -128, -128, -128];

          for (x = 0; x < width; x++) {
              int a, r, g, b;

              a = V[1].Read(ref gb);
              r = V[0].Read(ref gb);
              g = V[1].Read(ref gb);
              b = V[1].Read(ref gb);

              P[0][o_dst + (4 * x + 0)] = (pred[0] = (a + pred[0]) & 0xff) & 0xFF;
              P[0][o_dst + (4 * x + 1)] = (pred[1] = (r + pred[1]) & 0xff) & 0xFF;
              P[0][o_dst + (4 * x + 2)] = (pred[2] = (r + g + pred[2]) & 0xff) & 0xFF;
              P[0][o_dst + (4 * x + 3)] = (pred[3] = (r + g + b + pred[3]) & 0xff) & 0xFF;
          }
      }

      o_dst += L[0];
      for (row = 1; row < height; row++) {
          if (gb.Read(1) != 0) {
              for (x = 0; x < width; x++) {
                  P[0][o_dst + (x * 4 + 0)] = (gb.Read(8)) & 0xFF;
                  P[0][o_dst + (x * 4 + 1)] = (gb.Read(8)) & 0xFF;
                  P[0][o_dst + (x * 4 + 2)] = (gb.Read(8)) & 0xFF;
                  P[0][o_dst + (x * 4 + 3)] = (gb.Read(8)) & 0xFF;
              }
          } else {
              int[] pred_L = new int[4];
              int a, r, g, b;

              pred_L[0] = P[0][o_dst + (-L[0] + 0)];
              pred_L[1] = P[0][o_dst + (-L[0] + 1)];
              pred_L[2] = P[0][o_dst + (-L[0] + 2)];
              pred_L[3] = P[0][o_dst + (-L[0] + 3)];

              for (x = 0; x < width; x++) {
                  a = V[1].Read(ref gb);
                  r = V[0].Read(ref gb);
                  g = V[1].Read(ref gb);
                  b = V[1].Read(ref gb);

                  P[0][o_dst + (4 * x + 0)] = (pred_L[0] = (a + pred_L[0]) & 0xff) & 0xFF;
                  P[0][o_dst + (4 * x + 1)] = (pred_L[1] = (r + pred_L[1]) & 0xff) & 0xFF;
                  P[0][o_dst + (4 * x + 2)] = (pred_L[2] = (r + g + pred_L[2]) & 0xff) & 0xFF;
                  P[0][o_dst + (4 * x + 3)] = (pred_L[3] = (r + g + b + pred_L[3]) & 0xff) & 0xFF;
              }
          }
          o_dst += L[0];
      }
  }

  /// <summary>FFmpeg's <c>decode_argb</c>, transcribed mechanically.</summary>
  private static void DecodeArgb(int width, int height, bool alt, int[][] P, int[] L, HqxVlc[] V, ref HqxBitReader gb) {
      int x, row;

      var o_dst = 0;
      if (gb.Read(1) != 0) {
          for (x = 0; x < width; x++) {
              P[0][o_dst + (x * 4 + 0)] = (gb.Read(8)) & 0xFF;
              P[0][o_dst + (x * 4 + 1)] = (gb.Read(8)) & 0xFF;
              P[0][o_dst + (x * 4 + 2)] = (gb.Read(8)) & 0xFF;
              P[0][o_dst + (x * 4 + 3)] = (gb.Read(8)) & 0xFF;
          }
      } else {
          int[] pred = [-128, -128, -128, -128];

          for (x = 0; x < width; x++) {
              int a, r, g, b;

              a = V[1].Read(ref gb);
              r = V[0].Read(ref gb);
              g = V[1].Read(ref gb);
              b = V[1].Read(ref gb);

              P[0][o_dst + (4 * x + 0)] = (pred[0] = (a + pred[0]) & 0xff) & 0xFF;
              P[0][o_dst + (4 * x + 1)] = (pred[1] = (r + pred[1]) & 0xff) & 0xFF;
              P[0][o_dst + (4 * x + 2)] = (pred[2] = (r + g + pred[2]) & 0xff) & 0xFF;
              P[0][o_dst + (4 * x + 3)] = (pred[3] = (r + g + b + pred[3]) & 0xff) & 0xFF;
          }
      }

      o_dst += L[0];
      for (row = 1; row < height; row++) {
          if (gb.Read(1) != 0) {
              for (x = 0; x < width; x++) {
                  P[0][o_dst + (x * 4 + 0)] = (gb.Read(8)) & 0xFF;
                  P[0][o_dst + (x * 4 + 1)] = (gb.Read(8)) & 0xFF;
                  P[0][o_dst + (x * 4 + 2)] = (gb.Read(8)) & 0xFF;
                  P[0][o_dst + (x * 4 + 3)] = (gb.Read(8)) & 0xFF;
              }
          } else {
              int[] pred_TL = new int[4], pred_L = new int[4], pred_T = new int[4];
              int a, r, g, b;

              pred_TL[0] = pred_L[0] = P[0][o_dst + (-L[0] + 0)];
              pred_TL[1] = pred_L[1] = P[0][o_dst + (-L[0] + 1)];
              pred_TL[2] = pred_L[2] = P[0][o_dst + (-L[0] + 2)];
              pred_TL[3] = pred_L[3] = P[0][o_dst + (-L[0] + 3)];

              for (x = 0; x < width; x++) {
                  pred_T[0] = P[0][o_dst + (-L[0] + 4 * x + 0)];
                  pred_T[1] = P[0][o_dst + (-L[0] + 4 * x + 1)];
                  pred_T[2] = P[0][o_dst + (-L[0] + 4 * x + 2)];
                  pred_T[3] = P[0][o_dst + (-L[0] + 4 * x + 3)];

                  a = V[1].Read(ref gb);
                  r = V[0].Read(ref gb);
                  g = V[1].Read(ref gb);
                  b = V[1].Read(ref gb);

                  P[0][o_dst + (4 * x + 0)] = (pred_L[0] = (a + ((3 * (pred_T[0] + pred_L[0]) - 2 * pred_TL[0]) >> 2)) & 0xff) & 0xFF;
                  P[0][o_dst + (4 * x + 1)] = (pred_L[1] = (r + ((3 * (pred_T[1] + pred_L[1]) - 2 * pred_TL[1]) >> 2)) & 0xff) & 0xFF;
                  P[0][o_dst + (4 * x + 2)] = (pred_L[2] = (r + g + ((3 * (pred_T[2] + pred_L[2]) - 2 * pred_TL[2]) >> 2)) & 0xff) & 0xFF;
                  P[0][o_dst + (4 * x + 3)] = (pred_L[3] = (r + g + b + ((3 * (pred_T[3] + pred_L[3]) - 2 * pred_TL[3]) >> 2)) & 0xff) & 0xFF;

                  pred_TL[0] = pred_T[0];
                  pred_TL[1] = pred_T[1];
                  pred_TL[2] = pred_T[2];
                  pred_TL[3] = pred_T[3];
              }
          }
          o_dst += L[0];
      }
  }

  /// <summary>FFmpeg's <c>decode_rgbi</c>, transcribed mechanically.</summary>
  private static void DecodeRgbi(int width, int height, bool alt, int[][] P, int[] L, HqxVlc[] V, ref HqxBitReader gb) {
      int x, row;

      var o_dst = 0;
      if (gb.Read(1) != 0) {
          for (x = 0; x < width; x++) {
              P[0][o_dst + (x * 4 + 0)] = (gb.Read(8)) & 0xFF;
              P[0][o_dst + (x * 4 + 1)] = (gb.Read(8)) & 0xFF;
              P[0][o_dst + (x * 4 + 2)] = (gb.Read(8)) & 0xFF;
          }
      } else {
          int[] pred = [-128, -128, -128, -128];

          for (x = 0; x < width; x++) {
              int r, g, b;

              r = V[0].Read(ref gb);
              g = V[1].Read(ref gb);
              b = V[1].Read(ref gb);

              P[0][o_dst + (4 * x + 0)] = (pred[0] = (r + pred[0]) & 0xff) & 0xFF;
              P[0][o_dst + (4 * x + 1)] = (pred[1] = (r + g + pred[1]) & 0xff) & 0xFF;
              P[0][o_dst + (4 * x + 2)] = (pred[2] = (r + g + b + pred[2]) & 0xff) & 0xFF;
          }
      }

      o_dst += L[0];
      for (row = 1; row < height; row++) {
          if (gb.Read(1) != 0) {
              for (x = 0; x < width; x++) {
                  P[0][o_dst + (x * 4 + 0)] = (gb.Read(8)) & 0xFF;
                  P[0][o_dst + (x * 4 + 1)] = (gb.Read(8)) & 0xFF;
                  P[0][o_dst + (x * 4 + 2)] = (gb.Read(8)) & 0xFF;
              }
          } else {
              int[] pred_L = new int[4];
              int r, g, b;

              pred_L[0] = P[0][o_dst + (-L[0] + 0)];
              pred_L[1] = P[0][o_dst + (-L[0] + 1)];
              pred_L[2] = P[0][o_dst + (-L[0] + 2)];

              for (x = 0; x < width; x++) {
                  r = V[0].Read(ref gb);
                  g = V[1].Read(ref gb);
                  b = V[1].Read(ref gb);

                  P[0][o_dst + (4 * x + 0)] = (pred_L[0] = (r + pred_L[0]) & 0xff) & 0xFF;
                  P[0][o_dst + (4 * x + 1)] = (pred_L[1] = (r + g + pred_L[1]) & 0xff) & 0xFF;
                  P[0][o_dst + (4 * x + 2)] = (pred_L[2] = (r + g + b + pred_L[2]) & 0xff) & 0xFF;
              }
          }
          o_dst += L[0];
      }
  }

  /// <summary>FFmpeg's <c>decode_rgb</c>, transcribed mechanically.</summary>
  private static void DecodeRgb(int width, int height, bool alt, int[][] P, int[] L, HqxVlc[] V, ref HqxBitReader gb) {
      int x, row;

      var o_dst = 0;
      if (gb.Read(1) != 0) {
          for (x = 0; x < width; x++) {
              P[0][o_dst + (x * 4 + 0)] = (gb.Read(8)) & 0xFF;
              P[0][o_dst + (x * 4 + 1)] = (gb.Read(8)) & 0xFF;
              P[0][o_dst + (x * 4 + 2)] = (gb.Read(8)) & 0xFF;
          }
      } else {
          int[] pred = [-128, -128, -128, -128];

          for (x = 0; x < width; x++) {
              int r, g, b;

              r = V[0].Read(ref gb);
              g = V[1].Read(ref gb);
              b = V[1].Read(ref gb);

              P[0][o_dst + (4 * x + 0)] = (pred[0] = (r + pred[0]) & 0xff) & 0xFF;
              P[0][o_dst + (4 * x + 1)] = (pred[1] = (r + g + pred[1]) & 0xff) & 0xFF;
              P[0][o_dst + (4 * x + 2)] = (pred[2] = (r + g + b + pred[2]) & 0xff) & 0xFF;
          }
      }

      o_dst += L[0];
      for (row = 1; row < height; row++) {
          if (gb.Read(1) != 0) {
              for (x = 0; x < width; x++) {
                  P[0][o_dst + (x * 4 + 0)] = (gb.Read(8)) & 0xFF;
                  P[0][o_dst + (x * 4 + 1)] = (gb.Read(8)) & 0xFF;
                  P[0][o_dst + (x * 4 + 2)] = (gb.Read(8)) & 0xFF;
              }
          } else {
              int[] pred_TL = new int[4], pred_L = new int[4], pred_T = new int[4];
              int r, g, b;

              pred_TL[0] = pred_L[0] = P[0][o_dst + (-L[0] + 0)];
              pred_TL[1] = pred_L[1] = P[0][o_dst + (-L[0] + 1)];
              pred_TL[2] = pred_L[2] = P[0][o_dst + (-L[0] + 2)];

              for (x = 0; x < width; x++) {
                  pred_T[0] = P[0][o_dst + (-L[0] + 4 * x + 0)];
                  pred_T[1] = P[0][o_dst + (-L[0] + 4 * x + 1)];
                  pred_T[2] = P[0][o_dst + (-L[0] + 4 * x + 2)];

                  r = V[0].Read(ref gb);
                  g = V[1].Read(ref gb);
                  b = V[1].Read(ref gb);

                  P[0][o_dst + (4 * x + 0)] = (pred_L[0] = (r + ((3 * (pred_T[0] + pred_L[0]) - 2 * pred_TL[0]) >> 2)) & 0xff) & 0xFF;
                  P[0][o_dst + (4 * x + 1)] = (pred_L[1] = (r + g + ((3 * (pred_T[1] + pred_L[1]) - 2 * pred_TL[1]) >> 2)) & 0xff) & 0xFF;
                  P[0][o_dst + (4 * x + 2)] = (pred_L[2] = (r + g + b + ((3 * (pred_T[2] + pred_L[2]) - 2 * pred_TL[2]) >> 2)) & 0xff) & 0xFF;

                  pred_TL[0] = pred_T[0];
                  pred_TL[1] = pred_T[1];
                  pred_TL[2] = pred_T[2];
              }
          }
          o_dst += L[0];
      }
  }
}
