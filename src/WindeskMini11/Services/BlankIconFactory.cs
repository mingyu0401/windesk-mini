using System;
using System.Collections.Generic;
using System.IO;

namespace WindeskMini11.Services
{
    /// <summary>
    /// 生成"完全透明"的图标文件（用于 Shell Icons 覆盖槽）。
    ///
    /// 关键点：不能用 32bpp + Alpha=0 的图标。Windows 在绘制图标叠加层（箭头 29 / 盾牌 77）时
    /// 并不总是遵循 Alpha 通道，32bpp 全透明图标可能被忽略或渲染成不透明黑块。
    /// 安全做法是 1bpp 位图 + AND 掩码全 1：没有 Alpha 通道参与合成，"保持屏幕像素不变" 完全由掩码表达。
    /// </summary>
    public static class BlankIconFactory
    {
        // 覆盖 Explorer 会用到的全部尺寸，避免缩放导致边缘残留
        private static readonly int[] Sizes = { 16, 20, 24, 32, 40, 48, 64, 96, 128, 256 };

        public static byte[] Build()
        {
            var images = new List<byte[]>();
            foreach (var size in Sizes) images.Add(BuildImage(size));

            using var ms = new MemoryStream();
            using var w = new BinaryWriter(ms);

            w.Write((ushort)0);                  // reserved
            w.Write((ushort)1);                  // type = icon
            w.Write((ushort)images.Count);

            var offset = 6 + 16 * images.Count;
            for (var i = 0; i < images.Count; i++)
            {
                var size = Sizes[i];
                var dim = (byte)(size >= 256 ? 0 : size);
                w.Write(dim);
                w.Write(dim);
                w.Write((byte)0);                // palette count
                w.Write((byte)0);                // reserved
                w.Write((ushort)1);              // planes
                w.Write((ushort)1);              // bpp
                w.Write((uint)images[i].Length);
                w.Write((uint)offset);
                offset += images[i].Length;
            }

            foreach (var image in images) w.Write(image);
            w.Flush();
            return ms.ToArray();
        }

        private static byte[] BuildImage(int size)
        {
            var rowBytes = (size + 31) / 32 * 4;
            var xorSize = rowBytes * size;
            var andSize = rowBytes * size;

            using var ms = new MemoryStream();
            using var w = new BinaryWriter(ms);

            // BITMAPINFOHEADER（高度翻倍：XOR 位图 + AND 掩码）
            w.Write(40);
            w.Write(size);
            w.Write(size * 2);
            w.Write((ushort)1);
            w.Write((ushort)1);                                   // 1bpp
            w.Write(0u);                                           // BI_RGB
            w.Write((uint)(xorSize + andSize));
            w.Write(0);
            w.Write(0);
            w.Write(0u);
            w.Write(0u);

            // 1bpp 调色板：黑、白（内容无关紧要，掩码全 1 时全部透明）
            w.Write((byte)0); w.Write((byte)0); w.Write((byte)0); w.Write((byte)0);
            w.Write((byte)255); w.Write((byte)255); w.Write((byte)255); w.Write((byte)0);

            w.Write(new byte[xorSize]);                            // XOR 全 0

            var mask = new byte[andSize];
            for (var i = 0; i < mask.Length; i++) mask[i] = 0xFF;  // AND 全 1 => 透明
            w.Write(mask);

            w.Flush();
            return ms.ToArray();
        }

        /// <summary>结构校验：确认是 1bpp 且掩码全为 1（即真正透明）的 ICO。</summary>
        public static bool IsBlank1Bpp(byte[] data)
        {
            try
            {
                if (data.Length < 6) return false;
                if (BitConverter.ToUInt16(data, 0) != 0 || BitConverter.ToUInt16(data, 2) != 1) return false;

                var count = BitConverter.ToUInt16(data, 4);
                if (count < 4) return false;

                for (var i = 0; i < count; i++)
                {
                    var entry = 6 + 16 * i;
                    if (entry + 16 > data.Length) return false;

                    var size = data[entry] == 0 ? 256 : data[entry];
                    var bpp = BitConverter.ToUInt16(data, entry + 6);
                    if (bpp != 1) return false;

                    var bytes = (int)BitConverter.ToUInt32(data, entry + 8);
                    var offset = (int)BitConverter.ToUInt32(data, entry + 12);
                    if (offset <= 0 || bytes <= 0 || offset + bytes > data.Length) return false;

                    var rowBytes = (size + 31) / 32 * 4;
                    var xorSize = rowBytes * size;
                    var andSize = rowBytes * size;

                    // 头部 40 + 调色板 8 + XOR + AND
                    var maskStart = offset + 40 + 8 + xorSize;
                    if (maskStart + andSize > data.Length) return false;

                    for (var p = maskStart; p < maskStart + andSize; p++)
                    {
                        if (data[p] != 0xFF) return false;
                    }
                }

                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
