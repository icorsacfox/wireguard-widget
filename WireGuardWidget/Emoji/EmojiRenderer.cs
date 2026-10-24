using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Runtime.InteropServices;

namespace WireGuardWidget.Emoji
{
    /// <summary>
    /// Рисует цветные эмодзи в Bitmap. GDI/GDI+ умеют только монохромные глифы, поэтому используется
    /// Direct2D + DirectWrite (флаг ENABLE_COLOR_FONT) через минимальный COM-interop по таблицам виртуальных методов.
    /// Если Direct2D недоступен — запасной вариант на GDI+ (монохромный).
    /// Результат обрезается по непрозрачным пикселям и вписывается в квадрат, поэтому все эмодзи одного размера.
    /// Использовать только из UI-потока.
    /// </summary>
    internal sealed class EmojiRenderer : IDisposable
    {
        public static readonly EmojiRenderer Shared = new EmojiRenderer();

        const string FontFamily = "Segoe UI Emoji";

        readonly Dictionary<string, Bitmap> cache = new Dictionary<string, Bitmap>();
        readonly Dictionary<int, IntPtr> textFormats = new Dictionary<int, IntPtr>();
        readonly Dictionary<IntPtr, Delegate> delegates = new Dictionary<IntPtr, Delegate>();
        IntPtr d2dFactory, dwriteFactory, target, brush;
        bool d2dUnavailable;

        /// <summary>Эмодзи размером size×size пикселей. Bitmap кэшируется и принадлежит рендереру — не освобождайте его.</summary>
        public Bitmap Get(string emoji, int size)
        {
            if (string.IsNullOrEmpty(emoji))
                emoji = "❔";
            size = Math.Max(1, size);
            string key = size + "\u0001" + emoji;
            Bitmap bitmap;
            if (cache.TryGetValue(key, out bitmap))
                return bitmap;
            bitmap = Render(emoji, size);
            cache[key] = bitmap;
            return bitmap;
        }

        public bool UsesDirect2D { get { return !d2dUnavailable; } }

        Bitmap Render(string text, int size)
        {
            // Рисуем крупно и уменьшаем — так мелкие иконки (16 px в трее) получаются чище.
            int em = Math.Min(256, Math.Max(64, size * 2));
            int width = em * 3, height = em * 2;
            using (var canvas = FlagPainter.TryRender(text, em * 2) ?? RenderCanvas(text, em, width, height))
                return CropAndFit(canvas, size);
        }

        Bitmap RenderCanvas(string text, int em, int width, int height)
        {
            if (!d2dUnavailable)
            {
                try
                {
                    var bitmap = RenderDirect2D(text, em, width, height);
                    if (bitmap != null)
                        return bitmap;
                }
                catch (Exception ex)
                {
                    Log.Error("Direct2D недоступен, эмодзи будут монохромными", ex);
                }
                d2dUnavailable = true;
                ReleaseNative();
            }
            return RenderGdiPlus(text, em, width, height);
        }

        static Bitmap RenderGdiPlus(string text, int em, int width, int height)
        {
            var bitmap = new Bitmap(width, height, PixelFormat.Format32bppPArgb);
            using (var g = Graphics.FromImage(bitmap))
            using (var font = new Font(FontFamily, em, GraphicsUnit.Pixel))
            using (var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center, FormatFlags = StringFormatFlags.NoWrap })
            {
                g.TextRenderingHint = TextRenderingHint.AntiAlias;
                g.DrawString(text, font, Brushes.White, new RectangleF(0, 0, width, height), format);
            }
            return bitmap;
        }

        static Bitmap CropAndFit(Bitmap canvas, int size)
        {
            var result = new Bitmap(size, size, PixelFormat.Format32bppPArgb);
            Rectangle bounds = OpaqueBounds(canvas);
            if (bounds.Width <= 0 || bounds.Height <= 0)
                return result;
            bounds.Inflate(1, 1);
            bounds.Intersect(new Rectangle(0, 0, canvas.Width, canvas.Height));

            float scale = Math.Min((float)size / bounds.Width, (float)size / bounds.Height);
            float w = bounds.Width * scale, h = bounds.Height * scale;
            using (var g = Graphics.FromImage(result))
            {
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.CompositingQuality = CompositingQuality.HighQuality;
                g.DrawImage(canvas, new RectangleF((size - w) / 2f, (size - h) / 2f, w, h), bounds, GraphicsUnit.Pixel);
            }
            return result;
        }

        static Rectangle OpaqueBounds(Bitmap bitmap)
        {
            var data = bitmap.LockBits(new Rectangle(0, 0, bitmap.Width, bitmap.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppPArgb);
            try
            {
                int stride = data.Stride, w = bitmap.Width, h = bitmap.Height;
                var pixels = new byte[stride * h];
                Marshal.Copy(data.Scan0, pixels, 0, pixels.Length);
                int left = w, top = h, right = -1, bottom = -1;
                for (int y = 0; y < h; y++)
                {
                    int row = y * stride;
                    for (int x = 0; x < w; x++)
                    {
                        if (pixels[row + x * 4 + 3] <= 12)
                            continue;
                        if (x < left) left = x;
                        if (x > right) right = x;
                        if (y < top) top = y;
                        if (y > bottom) bottom = y;
                    }
                }
                return right < 0 ? Rectangle.Empty : Rectangle.FromLTRB(left, top, right + 1, bottom + 1);
            }
            finally
            {
                bitmap.UnlockBits(data);
            }
        }

        #region Direct2D

        Bitmap RenderDirect2D(string text, int em, int width, int height)
        {
            EnsureFactories();
            IntPtr format = GetTextFormat(em);

            var info = new BITMAPINFO { biSize = 40, biWidth = width, biHeight = -height, biPlanes = 1, biBitCount = 32 };
            IntPtr dc = CreateCompatibleDC(IntPtr.Zero);
            IntPtr bits;
            IntPtr dib = CreateDIBSection(dc, ref info, 0, out bits, IntPtr.Zero, 0);
            if (dib == IntPtr.Zero)
            {
                DeleteDC(dc);
                throw new InvalidOperationException("CreateDIBSection failed");
            }
            IntPtr old = SelectObject(dc, dib);
            try
            {
                for (int attempt = 0; attempt < 2; attempt++)
                {
                    EnsureTarget();
                    var rect = new RECT { right = width, bottom = height };
                    Check(Call<BindDCFn>(target, 57)(target, dc, ref rect), "BindDC");
                    Call<VoidFn>(target, 48)(target); // BeginDraw
                    var transparent = new D2D1_COLOR_F();
                    Call<ClearFn>(target, 47)(target, ref transparent);
                    var layout = new D2D1_RECT_F { right = width, bottom = height };
                    Call<DrawTextFn>(target, 27)(target, text, text.Length, format, ref layout, brush,
                        D2D1_DRAW_TEXT_OPTIONS_ENABLE_COLOR_FONT, DWRITE_MEASURING_MODE_NATURAL);
                    int hr = Call<EndDrawFn>(target, 49)(target, IntPtr.Zero, IntPtr.Zero);
                    if (hr == D2DERR_RECREATE_TARGET)
                    {
                        ReleaseTarget();
                        continue;
                    }
                    Check(hr, "EndDraw");
                    GdiFlush();
                    return CopyPixels(bits, width, height);
                }
                return null;
            }
            finally
            {
                SelectObject(dc, old);
                DeleteObject(dib);
                DeleteDC(dc);
            }
        }

        static Bitmap CopyPixels(IntPtr bits, int width, int height)
        {
            var bitmap = new Bitmap(width, height, PixelFormat.Format32bppPArgb);
            var data = bitmap.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.WriteOnly, PixelFormat.Format32bppPArgb);
            try
            {
                var buffer = new byte[width * 4];
                for (int y = 0; y < height; y++)
                {
                    Marshal.Copy(bits + y * width * 4, buffer, 0, buffer.Length);
                    Marshal.Copy(buffer, 0, data.Scan0 + y * data.Stride, buffer.Length);
                }
            }
            finally
            {
                bitmap.UnlockBits(data);
            }
            return bitmap;
        }

        void EnsureFactories()
        {
            if (d2dFactory == IntPtr.Zero)
            {
                Guid iid = IID_ID2D1Factory;
                Check(D2D1CreateFactory(D2D1_FACTORY_TYPE_SINGLE_THREADED, ref iid, IntPtr.Zero, out d2dFactory), "D2D1CreateFactory");
            }
            if (dwriteFactory == IntPtr.Zero)
            {
                Guid iid = IID_IDWriteFactory;
                Check(DWriteCreateFactory(DWRITE_FACTORY_TYPE_SHARED, ref iid, out dwriteFactory), "DWriteCreateFactory");
            }
        }

        void EnsureTarget()
        {
            if (target != IntPtr.Zero)
                return;
            var props = new D2D1_RENDER_TARGET_PROPERTIES
            {
                type = D2D1_RENDER_TARGET_TYPE_SOFTWARE,
                pixelFormat = DXGI_FORMAT_B8G8R8A8_UNORM,
                alphaMode = D2D1_ALPHA_MODE_PREMULTIPLIED,
                dpiX = 96,
                dpiY = 96,
            };
            Check(Call<CreateDCRenderTargetFn>(d2dFactory, 16)(d2dFactory, ref props, out target), "CreateDCRenderTarget");
            Call<IntArgVoidFn>(target, 34)(target, D2D1_TEXT_ANTIALIAS_MODE_GRAYSCALE); // SetTextAntialiasMode
            var white = new D2D1_COLOR_F { r = 0.91f, g = 0.92f, b = 0.93f, a = 1 };
            Check(Call<CreateSolidColorBrushFn>(target, 8)(target, ref white, IntPtr.Zero, out brush), "CreateSolidColorBrush");
        }

        IntPtr GetTextFormat(int em)
        {
            IntPtr format;
            if (textFormats.TryGetValue(em, out format))
                return format;
            Check(Call<CreateTextFormatFn>(dwriteFactory, 15)(dwriteFactory, FontFamily, IntPtr.Zero,
                400 /*NORMAL*/, 0 /*NORMAL*/, 5 /*NORMAL*/, em, "en-us", out format), "CreateTextFormat");
            Call<IntArgFn>(format, 3)(format, 2); // SetTextAlignment(CENTER)
            Call<IntArgFn>(format, 4)(format, 2); // SetParagraphAlignment(CENTER)
            Call<IntArgFn>(format, 5)(format, 1); // SetWordWrapping(NO_WRAP)
            textFormats[em] = format;
            return format;
        }

        /// <summary>Делегат на метод COM-объекта по номеру слота в его vtable.</summary>
        T Call<T>(IntPtr comObject, int slot) where T : class
        {
            IntPtr vtable = Marshal.ReadIntPtr(comObject);
            IntPtr function = Marshal.ReadIntPtr(vtable, slot * IntPtr.Size);
            Delegate cached;
            if (!delegates.TryGetValue(function, out cached))
            {
                cached = Marshal.GetDelegateForFunctionPointer(function, typeof(T));
                delegates[function] = cached;
            }
            return (T)(object)cached;
        }

        void ReleaseTarget()
        {
            Release(ref brush);
            Release(ref target);
        }

        void ReleaseNative()
        {
            ReleaseTarget();
            foreach (var format in textFormats.Values)
            {
                IntPtr f = format;
                Release(ref f);
            }
            textFormats.Clear();
            Release(ref dwriteFactory);
            Release(ref d2dFactory);
        }

        void Release(ref IntPtr comObject)
        {
            if (comObject == IntPtr.Zero)
                return;
            try { Call<ReleaseFn>(comObject, 2)(comObject); } catch { }
            comObject = IntPtr.Zero;
        }

        static void Check(int hr, string what)
        {
            if (hr < 0)
                throw new COMException(what + " failed", hr);
        }

        public void Dispose()
        {
            ReleaseNative();
            foreach (var bitmap in cache.Values)
                bitmap.Dispose();
            cache.Clear();
        }

        #endregion

        #region Interop

        const int D2D1_FACTORY_TYPE_SINGLE_THREADED = 0;
        const int DWRITE_FACTORY_TYPE_SHARED = 0;
        const int D2D1_RENDER_TARGET_TYPE_SOFTWARE = 1;
        const int DXGI_FORMAT_B8G8R8A8_UNORM = 87;
        const int D2D1_ALPHA_MODE_PREMULTIPLIED = 1;
        const int D2D1_TEXT_ANTIALIAS_MODE_GRAYSCALE = 2;
        const int D2D1_DRAW_TEXT_OPTIONS_ENABLE_COLOR_FONT = 4;
        const int DWRITE_MEASURING_MODE_NATURAL = 0;
        const int D2DERR_RECREATE_TARGET = unchecked((int)0x8899000C);

        static readonly Guid IID_ID2D1Factory = new Guid("06152247-6f50-465a-9245-118bfd3b6007");
        static readonly Guid IID_IDWriteFactory = new Guid("b859ee5a-d838-4b5b-a2e8-1adc7d93db48");

        [StructLayout(LayoutKind.Sequential)]
        struct D2D1_RENDER_TARGET_PROPERTIES
        {
            public int type;
            public int pixelFormat;
            public int alphaMode;
            public float dpiX;
            public float dpiY;
            public int usage;
            public int minLevel;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct D2D1_COLOR_F { public float r, g, b, a; }

        [StructLayout(LayoutKind.Sequential)]
        struct D2D1_RECT_F { public float left, top, right, bottom; }

        [StructLayout(LayoutKind.Sequential)]
        struct RECT { public int left, top, right, bottom; }

        [StructLayout(LayoutKind.Sequential)]
        struct BITMAPINFO
        {
            public int biSize, biWidth, biHeight;
            public short biPlanes, biBitCount;
            public int biCompression, biSizeImage, biXPelsPerMeter, biYPelsPerMeter, biClrUsed, biClrImportant;
            public int bmiColors;
        }

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        delegate uint ReleaseFn(IntPtr self);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        delegate int CreateDCRenderTargetFn(IntPtr self, ref D2D1_RENDER_TARGET_PROPERTIES props, out IntPtr renderTarget);

        [UnmanagedFunctionPointer(CallingConvention.StdCall, CharSet = CharSet.Unicode)]
        delegate int CreateTextFormatFn(IntPtr self, string family, IntPtr collection, int weight, int style, int stretch,
            float size, string locale, out IntPtr textFormat);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        delegate int IntArgFn(IntPtr self, int value);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        delegate void IntArgVoidFn(IntPtr self, int value);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        delegate int CreateSolidColorBrushFn(IntPtr self, ref D2D1_COLOR_F color, IntPtr properties, out IntPtr solidBrush);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        delegate int BindDCFn(IntPtr self, IntPtr dc, ref RECT rect);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        delegate void VoidFn(IntPtr self);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        delegate void ClearFn(IntPtr self, ref D2D1_COLOR_F color);

        [UnmanagedFunctionPointer(CallingConvention.StdCall, CharSet = CharSet.Unicode)]
        delegate void DrawTextFn(IntPtr self, string text, int length, IntPtr textFormat, ref D2D1_RECT_F layoutRect,
            IntPtr defaultBrush, int options, int measuringMode);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        delegate int EndDrawFn(IntPtr self, IntPtr tag1, IntPtr tag2);

        [DllImport("d2d1.dll")]
        static extern int D2D1CreateFactory(int factoryType, ref Guid riid, IntPtr options, out IntPtr factory);

        [DllImport("dwrite.dll")]
        static extern int DWriteCreateFactory(int factoryType, ref Guid iid, out IntPtr factory);

        [DllImport("gdi32.dll")]
        static extern IntPtr CreateCompatibleDC(IntPtr dc);

        [DllImport("gdi32.dll")]
        static extern IntPtr CreateDIBSection(IntPtr dc, ref BITMAPINFO info, uint usage, out IntPtr bits, IntPtr section, uint offset);

        [DllImport("gdi32.dll")]
        static extern IntPtr SelectObject(IntPtr dc, IntPtr obj);

        [DllImport("gdi32.dll")]
        static extern bool DeleteObject(IntPtr obj);

        [DllImport("gdi32.dll")]
        static extern bool DeleteDC(IntPtr dc);

        [DllImport("gdi32.dll")]
        static extern bool GdiFlush();

        #endregion
    }
}
