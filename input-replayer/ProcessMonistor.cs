using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Management;
using System.Linq;
using System.Threading.Tasks;

namespace input_replayer
{
    public class ProcessMonitor : IDisposable
    {
        // Win32 API to check if window is visible
        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool IsWindowVisible(IntPtr hWnd);

        // Lock object for thread safety
        private readonly object _lock = new object();

        private ManagementEventWatcher processStartWatcher;
        private bool isMonitoring = false;

        private bool isNewProcess = false;

        // Class to store process information
        private class ProcessInfo
        {
            public int ProcessId { get; set; }
            public string ProcessName { get; set; }
            public DateTime StartTime { get; set; }
            public bool IsFullyLoaded { get; set; }
        }

        public ProcessMonitor()
        {
            SetupProcessWatcher();
        }

        private void SetupProcessWatcher()
        {
            try
            {
                // WMI query to monitor for new process creation
                string query = "SELECT * FROM __InstanceCreationEvent WITHIN 1 WHERE TargetInstance ISA 'Win32_Process'";
                processStartWatcher = new ManagementEventWatcher(query);
                processStartWatcher.EventArrived += ProcessStartWatcher_EventArrived;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error setting up process watcher: {ex.Message}");
            }
        }

        private async void ProcessStartWatcher_EventArrived(object sender, EventArrivedEventArgs e)
        {
            try
            {
                ManagementBaseObject targetInstance = (ManagementBaseObject)e.NewEvent["TargetInstance"];
                string processName = targetInstance["Name"].ToString();
                int processId = Convert.ToInt32(targetInstance["ProcessId"]);

                string processNameWithoutExt = System.IO.Path.GetFileNameWithoutExtension(processName);

                Debug.WriteLine($"New process detected: {processNameWithoutExt} (PID: {processId})");
                isNewProcess = true;
                Console.WriteLine("isNewProcess: " + isNewProcess);
                await Task.Delay(2000);
                isNewProcess = false;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error in process event handler: {ex.Message}");
            }
        }

        // Start monitoring for new processes
        public void StartMonitoring()
        {
            if (!isMonitoring)
            {
                try
                {
                    processStartWatcher.Start();
                    isMonitoring = true;
                    Debug.WriteLine("Process monitoring started");
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error starting process monitoring: {ex.Message}");
                }
            }
        }

        public bool GetIsNewProcess()
        {
            return isNewProcess;
        }   

        // Stop monitoring for new processes
        public void StopMonitoring()
        {
            if (isMonitoring)
            {
                try
                {
                    processStartWatcher.Stop();
                    isMonitoring = false;
                    Debug.WriteLine("Process monitoring stopped");
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error stopping process monitoring: {ex.Message}");
                }
            }
        }

        // Clean up resources
        public void Dispose()
        {
            StopMonitoring();
            if (processStartWatcher != null)
            {
                processStartWatcher.EventArrived -= ProcessStartWatcher_EventArrived;
                processStartWatcher.Dispose();
            }
        }
    }
}