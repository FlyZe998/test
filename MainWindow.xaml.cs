using System;
using System.Windows;
using System.Windows.Media;

namespace KeySpammer;

public partial class MainWindow : Window
{
    private readonly SpamEngine _engine = new();
    private Native.LowLevelMouseProc? _hookProc; // keep alive -- GC will collect a local delegate mid-hook otherwise
    private IntPtr _hookHandle = IntPtr.Zero;
    private readonly Settings _settings;
    private bool _loadingSettings; // suppresses save-on-load feedback while restoring saved values

    public MainWindow()
    {
        InitializeComponent();

        _settings = Settings.Load();
        _loadingSettings = true;

        KeyCombo.ItemsSource = KeyMap.All;
        KeyCombo.DisplayMemberPath = "Key";
        // Fall back to F6 if the saved key name no longer exists (e.g. an older settings file)
        var savedKey = KeyMap.All.Find(kv => kv.Key == _settings.KeyName);
        KeyCombo.SelectedItem = savedKey.Key is not null ? savedKey : KeyMap.All.Find(kv => kv.Key == "F6");

        // Setting these triggers KeyCombo_SelectionChanged/KpsBox_TextChanged
        // above, which apply the values to _engine directly -- _loadingSettings
        // just keeps that from re-triggering a save of the values we just loaded.
        KpsBox.Text = _settings.Kps.ToString();
        _loadingSettings = false;

        _engine.KpsUpdated += kps => Dispatcher.Invoke(() =>
        {
            KpsText.Text = kps.ToString("0.0");
            // re-assert normal ACTIVE status each healthy tick so a past
            // BLOCKED warning clears itself once sends start succeeding again
            if (_engine.IsActive)
            {
                StatusText.Text = "ACTIVE";
                StatusText.Foreground = Brushes.LimeGreen;
            }
        });
        _engine.SendFailed += _ => Dispatcher.Invoke(() =>
        {
            // Most common cause: target window is running elevated and this
            // app isn't -- Windows (UIPI) silently blocks synthetic input
            // from a lower-integrity process into a higher one.
            StatusText.Text = "BLOCKED (admin?)";
            StatusText.Foreground = Brushes.OrangeRed;
        });
        _engine.Start();

        try
        {
            using var proc = System.Diagnostics.Process.GetCurrentProcess();
            proc.PriorityClass = System.Diagnostics.ProcessPriorityClass.High;
        }
        catch
        {
            // best-effort -- lack of permission here isn't fatal, thread priority alone still helps
        }

        InstallMouseHook();
        Closed += (_, _) => { _engine.Stop(); UninstallMouseHook(); };
        KeyDown += (_, e) => { if (e.Key == System.Windows.Input.Key.Escape) Close(); };
    }

    private void Window_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e) => DragMove();

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

    private void KpsBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        if (int.TryParse(KpsBox.Text, out int kps) && kps > 0)
        {
            _engine.SetKps(kps);
            KpsBox.BorderBrush = Brushes.Gray;

            if (!_loadingSettings)
            {
                _settings.Kps = kps;
                _settings.Save();
            }
        }
        else
        {
            KpsBox.BorderBrush = Brushes.Red; // invalid input, ignored until fixed
        }
    }

    private void KeyCombo_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (KeyCombo.SelectedItem is System.Collections.Generic.KeyValuePair<string, ushort> kv)
        {
            _engine.SetKey(kv.Value);

            if (!_loadingSettings)
            {
                _settings.KeyName = kv.Key;
                _settings.Save();
            }
        }
    }

    private void ToggleButton_Click(object sender, RoutedEventArgs e) => SetActive(!_engine.IsActive);

    private void SetActive(bool active)
    {
        _engine.SetActive(active);
        Dispatcher.Invoke(() =>
        {
            if (active)
            {
                StatusText.Text = "ACTIVE";
                StatusText.Foreground = Brushes.LimeGreen;
                ToggleButton.Content = "ON · XButton1";
            }
            else
            {
                StatusText.Text = "READY";
                StatusText.Foreground = Brushes.Gray;
                ToggleButton.Content = "OFF · XButton1";
            }
        });
    }

    // ---- Global XButton1 (mouse button 4) toggle, same as the Python version ----

    private void InstallMouseHook()
    {
        _hookProc = HookCallback;
        using var curProcess = System.Diagnostics.Process.GetCurrentProcess();
        using var curModule = curProcess.MainModule!;
        _hookHandle = Native.SetWindowsHookEx(Native.WH_MOUSE_LL, _hookProc,
            Native.GetModuleHandle(curModule.ModuleName), 0);
    }

    private void UninstallMouseHook()
    {
        if (_hookHandle != IntPtr.Zero)
            Native.UnhookWindowsHookEx(_hookHandle);
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && wParam == (IntPtr)Native.WM_XBUTTONDOWN)
        {
            var hookStruct = System.Runtime.InteropServices.Marshal.PtrToStructure<Native.MSLLHOOKSTRUCT>(lParam);
            short button = (short)(hookStruct.mouseData >> 16);
            if (button == Native.XBUTTON1)
            {
                SetActive(!_engine.IsActive);
            }
        }
        return Native.CallNextHookEx(_hookHandle, nCode, wParam, lParam);
    }
}
