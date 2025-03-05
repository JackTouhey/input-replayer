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

namespace input_replayer
{
    // Structure to store recorded input events
    public class InputEvent
    {
        public DateTime Timestamp { get; set; }
        public InputType Type { get; set; }
        public int X { get; set; }
        public int Y { get; set; }
        public Key? KeyPressed { get; set; }
        public MouseButton? MouseButton { get; set; }
    }

    public enum InputType
    {
        MouseMove,
        MouseClick,
        KeyPress
    }

    public partial class MainWindow : Window
    {
        private List<InputEvent> recordedEvents = new List<InputEvent>();
        private bool isRecording = false;
        private bool isReplaying = false;

        // Win32 API imports for low-level input simulation
        [DllImport("user32.dll")]
        static extern bool SetCursorPos(int X, int Y);

        [DllImport("user32.dll")]
        static extern void mouse_event(uint dwFlags, int dx, int dy, uint dwData, int dwExtraInfo);

        [DllImport("user32.dll")]
        static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, int dwExtraInfo);

        public MainWindow()
        {
            InitializeComponent();
            SetupEventHandlers();
        }

        private void SetupEventHandlers()
        {
            // Mouse move tracking
            MouseMove += (s, e) =>
            {
                if (isRecording)
                {
                    recordedEvents.Add(new InputEvent
                    {
                        Timestamp = DateTime.Now,
                        Type = InputType.MouseMove,
                        X = (int)e.GetPosition(this).X,
                        Y = (int)e.GetPosition(this).Y
                    });
                }
            };

            // Mouse click tracking
            MouseDown += (s, e) =>
            {
                if (isRecording)
                {
                    recordedEvents.Add(new InputEvent
                    {
                        Timestamp = DateTime.Now,
                        Type = InputType.MouseClick,
                        X = (int)e.GetPosition(this).X,
                        Y = (int)e.GetPosition(this).Y,
                        MouseButton = e.ChangedButton
                    });
                }
            };

            // Keyboard tracking
            PreviewKeyDown += (s, e) =>
            {
                if (isRecording)
                {
                    recordedEvents.Add(new InputEvent
                    {
                        Timestamp = DateTime.Now,
                        Type = InputType.KeyPress,
                        KeyPressed = e.Key
                    });
                }
            };
        }

        private void StartRecording_Click(object sender, RoutedEventArgs e)
        {
            recordedEvents.Clear();
            isRecording = true;
            MessageBox.Show("Recording started. Perform actions you want to record.");
        }

        private void StopRecording_Click(object sender, RoutedEventArgs e)
        {
            isRecording = false;
            MessageBox.Show($"Recording stopped. {recordedEvents.Count} events recorded.");
        }

        private async void ReplayRecording_Click(object sender, RoutedEventArgs e)
        {
            if (recordedEvents.Count == 0)
            {
                MessageBox.Show("No recording to replay.");
                return;
            }

            isReplaying = true;
            DateTime baseTime = recordedEvents[0].Timestamp;

            foreach (var inputEvent in recordedEvents)
            {
                // Calculate time since first event
                TimeSpan delay = inputEvent.Timestamp - baseTime;
                await Task.Delay(delay);

                // Simulate input based on event type
                switch (inputEvent.Type)
                {
                    case InputType.MouseMove:
                        SetCursorPos(inputEvent.X, inputEvent.Y);
                        break;

                    case InputType.MouseClick:
                        // Simulate mouse click (you might need to add more complex mouse event simulation)
                        SetCursorPos(inputEvent.X, inputEvent.Y);
                        mouse_event(0x0002, 0, 0, 0, 0); // Left button down
                        mouse_event(0x0004, 0, 0, 0, 0); // Left button up
                        break;

                    case InputType.KeyPress:
                        // Basic key press simulation (incomplete, requires more robust implementation)
                        // This is a simplified example and won't work for all keys
                        if (inputEvent.KeyPressed.HasValue)
                        {
                            // You'd need to convert Key to virtual key code
                            // This is just a placeholder
                            // keybd_event(VkKeyScan((char)inputEvent.KeyPressed.Value), 0, 0, 0);
                        }
                        break;
                }

                if (!isReplaying) break;
            }

            isReplaying = false;
            MessageBox.Show("Replay completed.");
        }

        private void ClearRecording_Click(object sender, RoutedEventArgs e)
        {
            recordedEvents.Clear();
            MessageBox.Show("Recorded events cleared.");
        }
    }
}