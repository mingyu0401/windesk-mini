using System;
using System.Collections.Generic;
using System.IO;

namespace WindeskMini11.Services
{
    /// <summary>
    /// 生成“完全透明”的图标文件，供 Shell Icons 覆盖槽（29 小箭头 / 77 盾牌）使用。
    ///
    /// 实测教训（2026-11-17）：**1bpp + AND 掩码全 1** 的图标在部分渲染路径下会被当成
    /// 不透明位图，桌面图标上会出现一整块黑色方块（覆盖层被按原尺寸画了上去）。
    /// 32bpp + Alpha=0 才是各路径都尊重的写法：叠加层按 Alpha 合成，天然透明。
    ///
    /// 因此这里统一输出 32bpp；AND 掩码仍填全 1，兼容仍走旧式掩码合成的地方。
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
                w.Write((ushort)32);             // bpp
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
            var xorSize = size * size * 4;      // 32bpp：每像素 4 字节（BGRA）
            var andSize = rowBytes * size;      // 掩码行按 4 字节对齐

            using var ms = new MemoryStream();
            using var w = new BinaryWriter(ms);

            // BITMAPINFOHEADER（高度翻倍：XOR 位图 + AND 掩码）
            w.Write(40);
            w.Write(size);
            w.Write(size * 2);
            w.Write((ushort)1);
            w.Write((ushort)32);                    // 32bpp
            w.Write(0u);                            // BI_RGB
            w.Write((uint)(xorSize + andSize));
            w.Write(0);
            w.Write(0);
            w.Write(0u);
            w.Write(0u);

            w.Write(new byte[xorSize]);             // 全 0 => BGRA 全 0，Alpha=0

            var mask = new byte[andSize];
            for (var i = 0; i < mask.Length; i++) mask[i] = 0xFF;  // AND 全 1 => 保持屏幕像素
            w.Write(mask);

            w.Flush();
            return ms.ToArray();
        }

        /// <summary>结构校验：确认每个帧都是 32bpp、像素 Alpha 全 0、掩码全 1。</summary>
        public static bool IsBlank32Bpp(byte[] data)
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
                    if (bpp != 32) return false;

                    var bytes = (int)BitConverter.ToUInt32(data, entry + 8);
                    var offset = (int)BitConverter.ToUInt32(data, entry + 12);
                    if (offset <= 0 || bytes <= 0 || offset + bytes > data.Length) return false;

                    var xorSize = size * size * 4;
                    var rowBytes = (size + 31) / 32 * 4;
                    var andSize = rowBytes * size;

                    if (40 + xorSize + andSize > bytes) return false;

                    // 每个像素的第 4 字节是 Alpha，必须为 0
                    for (var p = offset + 40 + 3; p < offset + 40 + xorSize; p += 4)
                    {
                        if (data[p] != 0) return false;
                    }

                    var maskStart = offset + 40 + xorSize;
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
