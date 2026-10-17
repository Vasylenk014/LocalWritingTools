using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using Clipboard = System.Windows.Clipboard;

namespace WritingTools.Services;

public class GlobalHotkeyService : IDisposable
{
    private const int HOTKEY_ALT_SHIFT_G = 9001;
    private const int HOTKEY_CTRL_ALT_G = 9002;

    private HwndSource? _source;
    private IntPtr _hwnd;
    private bool _isDisposed;

    public event Action<string?>? HotkeyTriggered;

    public void Register(Window window)
    {
        var helper = new WindowInteropHelper(window);
        _hwnd = helper.Handle;

        _source = HwndSource.FromHwnd(_hwnd);
        _source?.AddHook(HwndHook);

        // Register Alt+Shift+G (0x47 = 'G')
        NativeMethods.RegisterHotKey(
            _hwnd,
            HOTKEY_ALT_SHIFT_G,
            NativeMethods.MOD_ALT | NativeMethods.MOD_SHIFT | NativeMethods.MOD_NOREPEAT,
            0x47
        );

        // Register Ctrl+Alt+G as alternative
        NativeMethods.RegisterHotKey(
            _hwnd,
            HOTKEY_CTRL_ALT_G,
            NativeMethods.MOD_CONTROL | NativeMethods.MOD_ALT | NativeMethods.MOD_NOREPEAT,
            0x47
        );
    }

    public void Unregister()
    {
        if (_hwnd != IntPtr.Zero)
        {
            NativeMethods.UnregisterHotKey(_hwnd, HOTKEY_ALT_SHIFT_G);
            NativeMethods.UnregisterHotKey(_hwnd, HOTKEY_CTRL_ALT_G);
            _source?.RemoveHook(HwndHook);
            _source = null;
            _hwnd = IntPtr.Zero;
        }
    }

    private IntPtr HwndHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == NativeMethods.WM_HOTKEY)
        {
            int id = wParam.ToInt32();
            if (id == HOTKEY_ALT_SHIFT_G || id == HOTKEY_CTRL_ALT_G)
            {
                handled = true;
                _ = OnHotkeyPressedAsync();
            }
        }
        return IntPtr.Zero;
    }

    private async Task OnHotkeyPressedAsync()
    {
        // 1. Simulate Ctrl+C to copy current selection in active app
        NativeMethods.SimulateCopy();

        // 2. Short delay for clipboard update in external app
        await Task.Delay(100);

        string? copiedText = null;
        try
        {
            if (Clipboard.ContainsText())
            {
                copiedText = Clipboard.GetText();
            }
        }
        catch
        {
            // Clipboard access might fail if locked
        }

        HotkeyTriggered?.Invoke(copiedText);
    }

    public void Dispose()
    {
        if (!_isDisposed)
        {
            Unregister();
            _isDisposed = true;
        }
    }
}
