using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace AutoSwitching;

/// <summary>
/// 运行时生成托盘图标，避免依赖外部资源文件。
/// </summary>
internal static class AppIcon
{
    private static Icon? _cached;

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr hIcon);

    public static Icon Create()
    {
        if (_cached is not null)
        {
            return _cached;
        }

        using var bitmap = new Bitmap(32, 32);
        using (Graphics g = Graphics.FromImage(bitmap))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
            g.Clear(Color.Transparent);

            using var background = new SolidBrush(Color.FromArgb(0, 120, 215));
            g.FillEllipse(background, 1, 1, 30, 30);

            using var font = new Font("Microsoft YaHei UI", 17f, FontStyle.Bold, GraphicsUnit.Pixel);
            using var textBrush = new SolidBrush(Color.White);
            using var format = new StringFormat
            {
                Alignment = StringAlignment.Center,
                LineAlignment = StringAlignment.Center,
            };
            g.DrawString("中", font, textBrush, new RectangleF(0, 0, 32, 32), format);
        }

        // Icon.FromHandle 不接管句柄所有权，克隆后必须销毁原句柄，否则泄漏 GDI 对象。
        IntPtr hIcon = bitmap.GetHicon();
        try
        {
            using Icon temp = Icon.FromHandle(hIcon);
            _cached = (Icon)temp.Clone();
        }
        finally
        {
            DestroyIcon(hIcon);
        }

        return _cached;
    }
}
