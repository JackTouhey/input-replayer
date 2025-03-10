﻿using System;
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

        // Hook-related fields
        private IntPtr _keyboardHookHandle = IntPtr.Zero;
        private IntPtr _mouseHookHandle = IntPtr.Zero;
        private NativeMethods.LowLevelKeyboardProc _keyboardHookProcedure;
        private NativeMethods.LowLevelMouseProc _mouseHookProcedure;

        // Recording management
        private List<RecordedInputEvent> _recordedInputEvents = new List<RecordedInputEvent>();
        private bool _isRecordingInputEvents = false;

        //Speed Management fields
        private static readonly Regex _regex = new Regex(@"^[0-9]+$");
        private bool SpeedButtonLastClicked = false;
        private string VerifiedSpeedInput = "";
        public MainWindow()
        {
            InitializeComponent();
        }

        private bool IsTextAllowed(string text)
        {
            return _regex.IsMatch(text);
        }
        void OnSpeedSliderClick(object sender, RoutedEventArgs e)
        {
            SpeedButtonLastClicked = false;
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
            if (!_isRecordingInputEvents)
            {
                _recordedInputEvents.Clear();
                _isRecordingInputEvents = true;

                // Setup keyboard hook
                _keyboardHookProcedure = ProcessKeyboardInput;
                _keyboardHookHandle = SetKeyboardHook(_keyboardHookProcedure);

                // Setup mouse hook
                _mouseHookProcedure = ProcessMouseInput;
                _mouseHookHandle = SetMouseHook(_mouseHookProcedure);

                StatusText.Text = "Recording global input events...";
            }
        }

        private void StopRecording_Click(object sender, RoutedEventArgs e)
        {
            if (_isRecordingInputEvents)
            {
                // Unhook both keyboard and mouse
                NativeMethods.UnhookWindowsHookEx(_keyboardHookHandle);
                NativeMethods.UnhookWindowsHookEx(_mouseHookHandle);

                _isRecordingInputEvents = false;
                StatusText.Text = $"Recording stopped. Captured {_recordedInputEvents.Count} events.";
            }
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
                        await Task.Delay(replaySpeed);
                        SetCursorPos(inputEvent.PositionX, inputEvent.PositionY);
                        mouse_event(MOUSEEVENTF_LEFTDOWN, 0, 0, 0, 0);
                        mouse_event(MOUSEEVENTF_LEFTUP, 0, 0, 0, 0);
                        break;

                    case InputEventType.MouseRightClick:
                        await Task.Delay(replaySpeed);
                        SetCursorPos(inputEvent.PositionX, inputEvent.PositionY);
                        mouse_event(MOUSEEVENTF_RIGHTDOWN, 0, 0, 0, 0);
                        mouse_event(MOUSEEVENTF_RIGHTUP, 0, 0, 0, 0);
                        break;

                    case InputEventType.KeyPress:
                        await Task.Delay(replaySpeed);
                        keybd_event((byte)inputEvent.VirtualKeyCode, 0, 0, 0);
                        break;

                    case InputEventType.KeyRelease:
                        await Task.Delay(replaySpeed);
                        keybd_event((byte)inputEvent.VirtualKeyCode, 0, KEYEVENTF_KEYUP, 0);
                        break;
                }
            }

            MessageBox.Show("Replay completed.");
        }

        // Native methods class (kept from previous implementation)
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