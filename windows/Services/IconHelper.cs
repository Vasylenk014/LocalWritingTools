using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Media.Imaging;

namespace WritingTools.Services;

public static class IconHelper
{
    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern bool DestroyIcon(IntPtr handle);

    public static Bitmap CreatePenBitmap(int size = 32)
    {
        var bitmap = new Bitmap(size, size);
        using (var g = Graphics.FromImage(bitmap))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.Clear(Color.Transparent);

            // Circular accent badge with vibrant gradient (Violet -> Indigo -> Electric Blue)
            using var bgBrush = new LinearGradientBrush(
                new Rectangle(1, 1, size - 2, size - 2),
                Color.FromArgb(139, 92, 246), // #8B5CF6 Vivid Violet
                Color.FromArgb(59, 130, 246), // #3B82F6 Electric Blue
                LinearGradientMode.ForwardDiagonal
            );
            g.FillEllipse(bgBrush, 1, 1, size - 3, size - 3);

            // Contrast rim (soft white/cyan outline for OLED black and light taskbar visibility)
            using var rimPen = new Pen(Color.FromArgb(160, 255, 255, 255), Math.Max(1f, size * 0.035f));
            g.DrawEllipse(rimPen, 1, 1, size - 3, size - 3);

            // Fountain pen nib in crisp white
            using var penTip = new SolidBrush(Color.White);
            PointF[] nib = new PointF[]
            {
                new PointF(size * 0.22f, size * 0.78f), // Tip
                new PointF(size * 0.39f, size * 0.49f),
                new PointF(size * 0.52f, size * 0.36f),
                new PointF(size * 0.76f, size * 0.22f), // Top back
                new PointF(size * 0.78f, size * 0.24f),
                new PointF(size * 0.64f, size * 0.49f),
                new PointF(size * 0.51f, size * 0.61f),
            };
            g.FillPolygon(penTip, nib);

            // Nib slit detail in dark indigo
            using var slitPen = new Pen(Color.FromArgb(49, 46, 129), Math.Max(1.2f, size * 0.04f));
            g.DrawLine(slitPen, size * 0.24f, size * 0.76f, size * 0.44f, size * 0.56f);

            // Breather hole
            using var holeBrush = new SolidBrush(Color.FromArgb(49, 46, 129));
            float holeSize = Math.Max(2f, size * 0.07f);
            g.FillEllipse(holeBrush, size * 0.42f, size * 0.53f, holeSize, holeSize);

            // AI Sparkle star at the tip in gold
            using var starBrush = new SolidBrush(Color.FromArgb(250, 204, 21));
            float starRadius = Math.Max(2.5f, size * 0.12f);
            float cx = size * 0.18f;
            float cy = size * 0.75f;
            g.FillEllipse(starBrush, cx - starRadius, cy - starRadius, starRadius * 2, starRadius * 2);

            // Inner bright white spark center
            using var whiteCenter = new SolidBrush(Color.White);
            float innerRadius = starRadius * 0.45f;
            g.FillEllipse(whiteCenter, cx - innerRadius, cy - innerRadius, innerRadius * 2, innerRadius * 2);
        }
        return bitmap;
    }

    public static Icon CreatePenIcon(int size = 32)
    {
        using var bitmap = CreatePenBitmap(size);
        IntPtr hIcon = bitmap.GetHicon();
        var icon = (Icon)Icon.FromHandle(hIcon).Clone();
        DestroyIcon(hIcon);
        return icon;
    }

    public static void SaveMultiResIco(string filePath)
    {
        int[] sizes = new[] { 16, 24, 32, 48, 64, 128, 256 };
        var pngDataList = new List<byte[]>();

        foreach (var s in sizes)
        {
            using var bmp = CreatePenBitmap(s);
            using var ms = new MemoryStream();
            bmp.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
            pngDataList.Add(ms.ToArray());
        }

        using var fs = new FileStream(filePath, FileMode.Create, FileAccess.Write);
        using var bw = new BinaryWriter(fs);

        // Header: ICONDIR
        bw.Write((ushort)0); // Reserved
        bw.Write((ushort)1); // Type 1 = Icon
        bw.Write((ushort)sizes.Length); // Count

        // Offset to image data starts after all directory entries (6 + 16*N)
        uint offset = (uint)(6 + (16 * sizes.Length));

        for (int i = 0; i < sizes.Length; i++)
        {
            int s = sizes[i];
            byte w = (byte)(s >= 256 ? 0 : s);
            byte h = (byte)(s >= 256 ? 0 : s);
            byte[] data = pngDataList[i];

            bw.Write(w);
            bw.Write(h);
            bw.Write((byte)0); // Color count
            bw.Write((byte)0); // Reserved
            bw.Write((ushort)1); // Color planes
            bw.Write((ushort)32); // Bits per pixel
            bw.Write((uint)data.Length); // Size of image data
            bw.Write(offset); // Offset to image data

            offset += (uint)data.Length;
        }

        // Write raw PNG data streams
        foreach (var data in pngDataList)
        {
            bw.Write(data);
        }
    }

    public static BitmapSource CreatePenImageSource(int size = 32)
    {
        using var bmp = CreatePenBitmap(size);
        var hBmp = bmp.GetHbitmap();
        try
        {
            return System.Windows.Interop.Imaging.CreateBitmapSourceFromHBitmap(
                hBmp,
                IntPtr.Zero,
                System.Windows.Int32Rect.Empty,
                BitmapSizeOptions.FromEmptyOptions()
            );
        }
        finally
        {
            DeleteObject(hBmp);
        }
    }

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern bool DeleteObject(IntPtr hObject);
}
