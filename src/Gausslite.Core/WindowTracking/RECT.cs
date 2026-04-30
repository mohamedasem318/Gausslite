using System.Runtime.InteropServices;

namespace Gausslite.Core.WindowTracking;

[StructLayout(LayoutKind.Sequential)]
public struct RECT
{
    public int Left;
    public int Top;
    public int Right;
    public int Bottom;
}
