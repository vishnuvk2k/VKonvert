using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;

namespace VKonvert.Engine
{
    /// <summary>
    /// Draws PDF pages to bitmaps with Google's PDFium (pdfium.dll next to the exe).
    /// PDFium isn't thread-safe, so every call takes one lock.
    /// </summary>
    public static class Pdfium
    {
        const string Dll = "pdfium.dll";
        static readonly object Gate = new object();
        static bool initialised;

        [DllImport(Dll)] static extern void FPDF_InitLibrary();
        [DllImport(Dll)] static extern IntPtr FPDF_LoadMemDocument(IntPtr data, int size, [MarshalAs(UnmanagedType.LPStr)] string password);
        [DllImport(Dll)] static extern void FPDF_CloseDocument(IntPtr doc);
        [DllImport(Dll)] static extern int FPDF_GetPageCount(IntPtr doc);
        [DllImport(Dll)] static extern uint FPDF_GetLastError();
        [DllImport(Dll)] static extern IntPtr FPDF_LoadPage(IntPtr doc, int index);
        [DllImport(Dll)] static extern void FPDF_ClosePage(IntPtr page);
        [DllImport(Dll)] static extern double FPDF_GetPageWidth(IntPtr page);
        [DllImport(Dll)] static extern double FPDF_GetPageHeight(IntPtr page);
        [DllImport(Dll)] static extern IntPtr FPDFBitmap_Create(int w, int h, int alpha);
        [DllImport(Dll)] static extern void FPDFBitmap_FillRect(IntPtr bmp, int x, int y, int w, int h, uint color);
        [DllImport(Dll)] static extern IntPtr FPDFBitmap_GetBuffer(IntPtr bmp);
        [DllImport(Dll)] static extern int FPDFBitmap_GetStride(IntPtr bmp);
        [DllImport(Dll)] static extern void FPDFBitmap_Destroy(IntPtr bmp);
        [DllImport(Dll)] static extern void FPDF_RenderPageBitmap(IntPtr bmp, IntPtr page, int x, int y, int w, int h, int rotate, int flags);

        const int FPDF_ANNOT = 0x01, FPDF_PRINTING = 0x800;
        const uint ErrPassword = 4;

        /// <summary>An open PDF. Keep it short-lived: it holds the whole file in memory.</summary>
        public sealed class Doc : IDisposable
        {
            GCHandle pin;
            IntPtr handle;
            public int PageCount { get; }

            public Doc(string path)
            {
                var bytes = File.ReadAllBytes(path);
                lock (Gate)
                {
                    if (!initialised) { FPDF_InitLibrary(); initialised = true; }
                    pin = GCHandle.Alloc(bytes, GCHandleType.Pinned);
                    handle = FPDF_LoadMemDocument(pin.AddrOfPinnedObject(), bytes.Length, null);
                    if (handle == IntPtr.Zero)
                    {
                        var err = FPDF_GetLastError();
                        pin.Free();
                        throw new InvalidDataException(err == ErrPassword
                            ? "This PDF is locked with a password."
                            : "This file isn’t a PDF VKonvert can read.");
                    }
                    PageCount = FPDF_GetPageCount(handle);
                }
            }

            /// <summary>Page size in points (1/72 inch).</summary>
            public SizeF PageSize(int index)
            {
                lock (Gate)
                {
                    var page = FPDF_LoadPage(handle, index);
                    try { return new SizeF((float)FPDF_GetPageWidth(page), (float)FPDF_GetPageHeight(page)); }
                    finally { FPDF_ClosePage(page); }
                }
            }

            /// <summary>Renders a page at the given DPI (or to fit maxSide pixels when maxSide &gt; 0), on white.</summary>
            public Bitmap Render(int index, float dpi, int maxSide = 0, bool forPrint = false)
            {
                lock (Gate)
                {
                    var page = FPDF_LoadPage(handle, index);
                    if (page == IntPtr.Zero) throw new InvalidDataException($"Page {index + 1} can’t be read.");
                    try
                    {
                        double wpt = FPDF_GetPageWidth(page), hpt = FPDF_GetPageHeight(page);
                        var scale = dpi / 72.0;
                        if (maxSide > 0) scale = maxSide / Math.Max(wpt, hpt);
                        int w = Math.Max(1, (int)Math.Round(wpt * scale)), h = Math.Max(1, (int)Math.Round(hpt * scale));
                        var bmp = FPDFBitmap_Create(w, h, 0);
                        if (bmp == IntPtr.Zero) throw new OutOfMemoryException("This page is too large to turn into an image at this quality.");
                        try
                        {
                            FPDFBitmap_FillRect(bmp, 0, 0, w, h, 0xFFFFFFFF);
                            FPDF_RenderPageBitmap(bmp, page, 0, 0, w, h, 0, FPDF_ANNOT | (forPrint ? FPDF_PRINTING : 0));
                            var stride = FPDFBitmap_GetStride(bmp);
                            var result = new Bitmap(w, h, PixelFormat.Format32bppRgb);
                            var data = result.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.WriteOnly, PixelFormat.Format32bppRgb);
                            try
                            {
                                var src = FPDFBitmap_GetBuffer(bmp);
                                var row = new byte[w * 4];
                                for (int y = 0; y < h; y++)
                                {
                                    Marshal.Copy(src + y * stride, row, 0, row.Length);
                                    Marshal.Copy(row, 0, data.Scan0 + y * data.Stride, row.Length);
                                }
                            }
                            finally { result.UnlockBits(data); }
                            return result;
                        }
                        finally { FPDFBitmap_Destroy(bmp); }
                    }
                    finally { FPDF_ClosePage(page); }
                }
            }

            public void Dispose()
            {
                lock (Gate)
                {
                    if (handle != IntPtr.Zero) { FPDF_CloseDocument(handle); handle = IntPtr.Zero; }
                    if (pin.IsAllocated) pin.Free();
                }
            }
        }
    }
}
