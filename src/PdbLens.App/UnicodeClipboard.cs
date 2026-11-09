using System.Runtime.InteropServices;

namespace PdbLens.App;

internal static class UnicodeClipboard
{
    private const uint GmemMoveable = 0x0002;
    private const uint CfUnicodeText = 13;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool OpenClipboard(nint newOwner);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool EmptyClipboard();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern nint SetClipboardData(uint format, nint memory);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool CloseClipboard();

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern nint GlobalAlloc(uint flags, nuint bytes);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern nint GlobalLock(nint memory);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GlobalUnlock(nint memory);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern nint GlobalFree(nint memory);

    public static bool TrySetText(string text)
    {
        if (!OpenClipboard(nint.Zero))
        {
            return false;
        }

        nint memory = nint.Zero;
        try
        {
            if (!EmptyClipboard())
            {
                return false;
            }

            var bytes = checked((nuint)((text.Length + 1) * sizeof(char)));
            memory = GlobalAlloc(GmemMoveable, bytes);
            if (memory == nint.Zero)
            {
                return false;
            }

            var target = GlobalLock(memory);
            if (target == nint.Zero)
            {
                return false;
            }

            try
            {
                Marshal.Copy(text.ToCharArray(), 0, target, text.Length);
                Marshal.WriteInt16(target, text.Length * sizeof(char), 0);
            }
            finally
            {
                GlobalUnlock(memory);
            }

            if (SetClipboardData(CfUnicodeText, memory) == nint.Zero)
            {
                return false;
            }

            memory = nint.Zero;
            return true;
        }
        finally
        {
            if (memory != nint.Zero)
            {
                GlobalFree(memory);
            }
            CloseClipboard();
        }
    }
}
