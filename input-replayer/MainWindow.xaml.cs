using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.Timers;
using System.Windows.Threading;
using Microsoft.Win32;
using Newtonsoft.Json;
using System.Text.RegularExpressions;
using System.Windows.Interop;
using System.Diagnostics;

namespace input_replayer
{
    public enum InputEventType
    {
        MouseMove,
        MouseLeftClick,
        MouseRightClick,
        KeyPress,
        KeyRelease
    }

    public class RecordedInputEvent
    {
        public DateTime Timestamp { get; set; }
        public InputEventType EventType { get; set; }
        public int PositionX { get; set; }
        public int PositionY { get; set; }
        public int VirtualKeyCode { get; set; }
        public bool IsExtendedKey { get; set; }
    }

    public partial class MainWindow : Window
    {
        // Win32 API Constants
        private const int WH_KEYBOARD_LL = 13;
        private const int WH_MOUSE_LL = 14;
        private const int WM_KEYDOWN = 0x0100;
        private const int WM_KEYUP = 0x0101;
        private const int WM_MOUSEMOVE = 0x0200;
        private const int WM_LBUTTONDOWN = 0x0201;
        private const int WM_LBUTTONUP = 0x0202;
        private const int WM_RBUTTONDOWN = 0x0204;
        private const int WM_RBUTTONUP = 0x0205;

        // Input simulation flags
        private const int MOUSEEVENTF_MOVE = 0x0001;
        private const int MOUSEEVENTF_LEFTDOWN = 0x0002;
        private const int MOUSEEVENTF_LEFTUP = 0x0004;
        private const int MOUSEEVENTF_RIGHTDOWN = 0x0008;
        private const int MOUSEEVENTF_RIGHTUP = 0x0010;
        private const int KEYEVENTF_KEYUP = 0x0002;

        // Native method imports for input simulation and hooking
        [DllImport("user32.dll")]
        private static extern int SetCursorPos(int x, int y);

        [DllImport("user32.dll")]
        private static extern void mouse_event(int dwFlags, int dx, int dy, int dwData, int dwExtraInfo);

        [DllImport("user32.dll")]
        private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, int dwExtraInfo);

        [DllImport("user32.dll")]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll")]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        // Hook-related fields
        private IntPtr _keyboardHookHandle = IntPtr.Zero;
        private IntPtr _mouseHookHandle = IntPtr.Zero;
        private NativeMethods.LowLevelKeyboardProc _keyboardHookProcedure;
        private NativeMethods.LowLevelMouseProc _mouseHookProcedure;
        private const uint MOD_CONTROL = 0x0002;
        private const uint MOD_SHIFT = 0x0004;
        private const int VK_R = 0x52;
        private const int VK_CONTROL = 0x11;
        private const int VK_LSHIFT = 0xA0;
        private const int HOTKEY_ID = 9000;
        private IntPtr _windowHandle;
        private HwndSource _source;

        // Recording management
        private List<RecordedInputEvent> _recordedInputEvents = new List<RecordedInputEvent>();
        private bool _isRecordingInputEvents = false;
        private bool _isReplayingEvents = false;

        //Speed Management fields
        private static readonly Regex _regex = new Regex(@"^[0-9]+$");
        private bool SpeedButtonLastClicked = false;
        private string VerifiedSpeedInput = "";
        public MainWindow()
        {
            InitializeComponent();
            this.Loaded += (sender, e) => RegisterGlobalHotKey();
            this.Closed += (sender, e) => UnregisterGlobalHotKey();
        }

        private void RegisterGlobalHotKey()
        {
            _windowHandle = new WindowInteropHelper(this).Handle;
            _source = HwndSource.FromHwnd(_windowHandle);
            _source.AddHook(HwndHook);
            RegisterHotKey(_windowHandle, HOTKEY_ID, MOD_CONTROL | MOD_SHIFT, VK_R);
        }

        private void UnregisterGlobalHotKey()
        {
            UnregisterHotKey(_windowHandle, HOTKEY_ID);
            _source.RemoveHook(HwndHook);

        }
        private IntPtr HwndHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            const int WM_HOTKEY = 0x0312;

            // Listen for hotkey message
            if (msg == WM_HOTKEY && wParam.ToInt32() == HOTKEY_ID)
            {
                // Execute your hotkey action here
                OnHotkeyPressed();
                handled = true;
            }
            return IntPtr.Zero;
        }
        private void OnHotkeyPressed()
        {
            if (_isRecordingInputEvents && !_isReplayingEvents)
            {
                Console.WriteLine("HotkeyPressed: Stopping recording, _isReplayingEvents: " + _isReplayingEvents);
                StopRecording_Click(null, null);
                _isRecordingInputEvents = false;
            }
            else if (!_isReplayingEvents)
            {
                Console.WriteLine("HotkeyPressed: Starting recording, _isReplayingEvents: " + _isReplayingEvents); 
                StartRecording_Click(null, null);
                _isRecordingInputEvents = true;
            }
        }

        private bool IsTextAllowed(string text)
        {
            return _regex.IsMatch(text);
        }
        void OnSpeedSliderClick(object sender, RoutedEventArgs e)
        {
            try
            {
                SpeedButtonLastClicked = false;
                Slider speedSlider = sender as Slider;
                if(speedSlider != null)
                {
                    double speedSliderValue = speedSlider.Value;
                    if(SpeedDisplay != null)
                    {
                        SpeedDisplay.Text = speedSliderValue.ToString("F0");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("OnSpeedSliderClick Error: " + ex);
            }            
        }
        void OnSpeedInputClick(object sender, RoutedEventArgs e)
        {
            string SpeedInputText = SpeedInput.Text;
            if (IsTextAllowed(SpeedInputText))
            {
                VerifiedSpeedInput = SpeedInputText;
                SpeedDisplay.Text = SpeedInputText;
                SpeedButtonLastClicked = true;
            }
            else
            {
                MessageBox.Show("Please enter a valid integer");
            }
        }

        private IntPtr ProcessMouseInput(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0 && _isRecordingInputEvents)
            {
                var mouseData = (NativeMethods.MSLLHOOKSTRUCT)Marshal.PtrToStructure(lParam, typeof(NativeMethods.MSLLHOOKSTRUCT));

                InputEventType eventType;
                if (wParam == (IntPtr)WM_MOUSEMOVE)
                {
                    eventType = InputEventType.MouseMove;
                }
                else if (wParam == (IntPtr)WM_LBUTTONDOWN)
                {
                    eventType = InputEventType.MouseLeftClick;
                }
                else if (wParam == (IntPtr)WM_RBUTTONDOWN)
                {
                    eventType = InputEventType.MouseRightClick;
                }
                else
                {
                    eventType = InputEventType.MouseMove;
                }

                _recordedInputEvents.Add(new RecordedInputEvent
                {
                    Timestamp = DateTime.Now,
                    EventType = eventType,
                    PositionX = mouseData.pt.x,
                    PositionY = mouseData.pt.y
                });
            }

            return NativeMethods.CallNextHookEx(_mouseHookHandle, nCode, wParam, lParam);
        }

        private void StartRecording_Click(object sender, RoutedEventArgs e)
        {
            Console.WriteLine("StartRecording_click");
            if (!_isRecordingInputEvents)
            {
                if (sender != "Appending Method")
                {
                    Console.WriteLine("Not Appending Method");
                    _recordedInputEvents.Clear();
                }
                _isRecordingInputEvents = true;
                _keyboardHookProcedure = ProcessKeyboardInput;
                _keyboardHookHandle = SetKeyboardHook(_keyboardHookProcedure);

                _mouseHookProcedure = ProcessMouseInput;
                _mouseHookHandle = SetMouseHook(_mouseHookProcedure);

                StatusText.Text = "Recording global input events...";
            }
        }

        private void StopRecording_Click(object sender, RoutedEventArgs e)
        {
            Console.WriteLine("StopRecording_Click"); 
            if (_isRecordingInputEvents)
            {
                NativeMethods.UnhookWindowsHookEx(_keyboardHookHandle);
                NativeMethods.UnhookWindowsHookEx(_mouseHookHandle);

                _isRecordingInputEvents = false;
                StatusText.Text = $"Recording stopped. Captured {_recordedInputEvents.Count} events.";
            }
        }

        private void AppendInput_Click(object sender, RoutedEventArgs e)
        {
            StartRecording_Click("Appending Method", null);
        }

        private IntPtr SetKeyboardHook(NativeMethods.LowLevelKeyboardProc procedure)
        {
            using (var currentProcess = System.Diagnostics.Process.GetCurrentProcess())
            using (var currentModule = currentProcess.MainModule)
            {
                return NativeMethods.SetWindowsHookEx(
                    WH_KEYBOARD_LL,
                    procedure,
                    NativeMethods.GetModuleHandle(currentModule.ModuleName),
                    0
                );
            }
        }

        private IntPtr SetMouseHook(NativeMethods.LowLevelMouseProc procedure)
        {
            using (var currentProcess = System.Diagnostics.Process.GetCurrentProcess())
            using (var currentModule = currentProcess.MainModule)
            {
                return NativeMethods.SetWindowsHookEx(
                    WH_MOUSE_LL,
                    procedure,
                    NativeMethods.GetModuleHandle(currentModule.ModuleName),
                    0
                );
            }
        }

        private IntPtr ProcessKeyboardInput(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0 && _isRecordingInputEvents)
            {
                var keyboardData = (NativeMethods.KBDLLHOOKSTRUCT)Marshal.PtrToStructure(lParam, typeof(NativeMethods.KBDLLHOOKSTRUCT));

                InputEventType eventType = (wParam == (IntPtr)WM_KEYDOWN)
                    ? InputEventType.KeyPress
                    : InputEventType.KeyRelease;

                _recordedInputEvents.Add(new RecordedInputEvent
                {
                    Timestamp = DateTime.Now,
                    EventType = eventType,
                    VirtualKeyCode = (int)keyboardData.vkCode,
                    IsExtendedKey = (keyboardData.flags & 0x01) != 0
                });
            }

            return NativeMethods.CallNextHookEx(_keyboardHookHandle, nCode, wParam, lParam);
        }

        private void SaveRecording_Click(object sender, RoutedEventArgs e)
        {
            var saveFileDialog = new SaveFileDialog
            {
                Filter = "JSON files (*.json)|*.json|All files (*.*)|*.*",
                DefaultExt = "json"
            };

            if (saveFileDialog.ShowDialog() == true)
            {
                var jsonEvents = JsonConvert.SerializeObject(_recordedInputEvents, Formatting.Indented);
                System.IO.File.WriteAllText(saveFileDialog.FileName, jsonEvents);
                MessageBox.Show("Events saved successfully!");
            }
        }

        private void LoadRecording_Click(object sender, RoutedEventArgs e)
        {
            var openFileDialog = new OpenFileDialog
            {
                Filter = "JSON files (*.json)|*.json|All files (*.*)|*.*"
            };

            if (openFileDialog.ShowDialog() == true)
            {
                var jsonContent = System.IO.File.ReadAllText(openFileDialog.FileName);
                _recordedInputEvents = JsonConvert.DeserializeObject<List<RecordedInputEvent>>(jsonContent);
                StatusText.Text = $"Loaded {_recordedInputEvents.Count} events.";
            }
        }
        
        private async void ReplayRecording_Click(object sender, RoutedEventArgs e)
        {
            _isReplayingEvents = true;
            Console.WriteLine("ReplayRecording_click, _isReplayingEvents: " + _isReplayingEvents);
            int replaySpeed = 100;

            if (SpeedButtonLastClicked)
            {
                replaySpeed = Convert.ToInt32(VerifiedSpeedInput);
            }
            else
            {
                double replaySpeedDouble = SpeedSlider.Value;
                replaySpeed = Convert.ToInt32(replaySpeedDouble);
            }

            if (_recordedInputEvents.Count == 0)
            {
                MessageBox.Show("No events to replay.");
                return;
            }
            
            foreach (var inputEvent in _recordedInputEvents)
            {
                
                switch (inputEvent.EventType)
                {
                    case InputEventType.MouseMove:
                        Console.WriteLine("Replaying Input: Mouse Move");
                        SetCursorPos(inputEvent.PositionX, inputEvent.PositionY);
                        int nextItem = _recordedInputEvents.IndexOf(inputEvent) + 1;
                        if (nextItem < _recordedInputEvents.Count)
                        {
                            if (_recordedInputEvents[nextItem].EventType != InputEventType.MouseMove)
                            {
                                await Task.Delay(replaySpeed);
                            }
                        }
                        
                        break;

                    case InputEventType.MouseLeftClick:
                        Console.WriteLine("Replaying Input: Left Click");
                        await Task.Delay(replaySpeed);
                        SetCursorPos(inputEvent.PositionX, inputEvent.PositionY);
                        mouse_event(MOUSEEVENTF_LEFTDOWN, 0, 0, 0, 0);
                        mouse_event(MOUSEEVENTF_LEFTUP, 0, 0, 0, 0);
                        break;

                    case InputEventType.MouseRightClick:
                        Console.WriteLine("Replaying Input: Right Click");
                        await Task.Delay(replaySpeed);
                        SetCursorPos(inputEvent.PositionX, inputEvent.PositionY);
                        mouse_event(MOUSEEVENTF_RIGHTDOWN, 0, 0, 0, 0);
                        mouse_event(MOUSEEVENTF_RIGHTUP, 0, 0, 0, 0);
                        break;

                    case InputEventType.KeyPress:
                        Console.WriteLine("Replaying Input: Key Press - " + inputEvent.VirtualKeyCode);
                        await Task.Delay(replaySpeed);
                        keybd_event((byte)inputEvent.VirtualKeyCode, 0, 0, 0);
                        break;

                    case InputEventType.KeyRelease:
                        Console.WriteLine("Replaying Input: Key Release - " + inputEvent.VirtualKeyCode);
                        await Task.Delay(replaySpeed);
                        keybd_event((byte)inputEvent.VirtualKeyCode, 0, KEYEVENTF_KEYUP, 0);
                        break;
                }
            }
            await Task.Delay(2000);
            _isReplayingEvents = false;
            Console.WriteLine("Ending Replay, _isReplayingEvents: " + _isReplayingEvents);
        }

        private static class NativeMethods
        {
            [StructLayout(LayoutKind.Sequential)]
            public struct POINT
            {
                public int x;
                public int y;
            }

            [StructLayout(LayoutKind.Sequential)]
            public struct MSLLHOOKSTRUCT
            {
                public POINT pt;
                public uint mouseData;
                public uint flags;
                public uint time;
                public IntPtr dwExtraInfo;
            }

            [StructLayout(LayoutKind.Sequential)]
            public struct KBDLLHOOKSTRUCT
            {
                public uint vkCode;
                public uint scanCode;
                public uint flags;
                public uint time;
                public IntPtr dwExtraInfo;
            }

            public delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);
            public delegate IntPtr LowLevelMouseProc(int nCode, IntPtr wParam, IntPtr lParam);

            [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
            public static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

            [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
            public static extern IntPtr SetWindowsHookEx(int idHook, LowLevelMouseProc lpfn, IntPtr hMod, uint dwThreadId);

            [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
            [return: MarshalAs(UnmanagedType.Bool)]
            public static extern bool UnhookWindowsHookEx(IntPtr hhk);

            [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
            public static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

            [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
            public static extern IntPtr GetModuleHandle(string lpModuleName);
        }
    }
}