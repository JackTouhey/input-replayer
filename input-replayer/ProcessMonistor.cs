using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Management;
using System.Linq;

namespace input_replayer
{
    public class ProcessMonitor : IDisposable
    {
        // Win32 API to check if window is visible
        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool IsWindowVisible(IntPtr hWnd);

        // Dictionary to track processes and their visibility status
        private Dictionary<int, bool> processVisibilityStatus = new Dictionary<int, bool>();

        // Track recently started processes
        private List<ProcessInfo> recentlyStartedProcesses = new List<ProcessInfo>();

        // Lock object for thread safety
        private readonly object _lock = new object();

        private ManagementEventWatcher processStartWatcher;
        private bool isMonitoring = false;

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
            // Initialize by taking a snapshot of current processes
            CaptureInitialProcessState();

            // Set up WMI event watcher for process start events
            SetupProcessWatcher();
        }

        private void CaptureInitialProcessState()
        {
            lock (_lock)
            {
                processVisibilityStatus.Clear();
                foreach (Process process in Process.GetProcesses())
                {
                    try
                    {
                        bool isVisible = process.MainWindowHandle != IntPtr.Zero &&
                                         IsWindowVisible(process.MainWindowHandle);
                        processVisibilityStatus[process.Id] = isVisible;
                    }
                    catch
                    {
                        // Ignore any processes we can't access
                    }
                }
            }
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

        private void ProcessStartWatcher_EventArrived(object sender, EventArrivedEventArgs e)
        {
            try
            {
                ManagementBaseObject targetInstance = (ManagementBaseObject)e.NewEvent["TargetInstance"];
                string processName = targetInstance["Name"].ToString();
                int processId = Convert.ToInt32(targetInstance["ProcessId"]);

                string processNameWithoutExt = System.IO.Path.GetFileNameWithoutExtension(processName);

                Debug.WriteLine($"New process detected: {processNameWithoutExt} (PID: {processId})");

                // Record this as a newly started process
                lock (_lock)
                {
                    recentlyStartedProcesses.Add(new ProcessInfo
                    {
                        ProcessId = processId,
                        ProcessName = processNameWithoutExt,
                        StartTime = DateTime.Now,
                        IsFullyLoaded = false
                    });

                    // Add to visibility tracking
                    processVisibilityStatus[processId] = false;
                }
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

        // Check if any new processes are starting
        public bool IsAnyProcessStarting()
        {
            CleanupOldProcesses();

            lock (_lock)
            {
                // Check if we have any recently started processes that aren't fully loaded
                if (recentlyStartedProcesses.Any(p => !p.IsFullyLoaded))
                    return true;

                // Also check if any processes exist but aren't visible yet
                foreach (var pid in processVisibilityStatus.Keys.ToList())
                {
                    try
                    {
                        Process process = Process.GetProcessById(pid);

                        // If process has a window but it's not visible yet, it's still starting
                        if (process.MainWindowHandle != IntPtr.Zero &&
                            !IsWindowVisible(process.MainWindowHandle) &&
                            !processVisibilityStatus[pid])
                        {
                            return true;
                        }
                    }
                    catch
                    {
                        // Process might have exited, remove it from our tracking
                        processVisibilityStatus.Remove(pid);
                    }
                }
            }

            return false;
        }

        // Remove processes that have been tracked for too long
        private void CleanupOldProcesses()
        {
            lock (_lock)
            {
                // Remove processes older than 30 seconds from our tracking
                var now = DateTime.Now;
                recentlyStartedProcesses.RemoveAll(p =>
                    (now - p.StartTime).TotalSeconds > 30 || p.IsFullyLoaded);
            }
        }

        // Wait for all starting processes to be fully loaded
        public bool WaitForAllProcessesToLoad(int timeoutMs = 10000)
        {
            var startTime = DateTime.Now;
            bool allLoaded = false;

            while ((DateTime.Now - startTime).TotalMilliseconds < timeoutMs && !allLoaded)
            {
                allLoaded = true;
                CleanupOldProcesses();

                lock (_lock)
                {
                    // Check all recently started processes
                    foreach (var processInfo in recentlyStartedProcesses.ToList())
                    {
                        try
                        {
                            Process process = Process.GetProcessById(processInfo.ProcessId);

                            // Check if the main window handle is valid and visible
                            bool isVisible = process.MainWindowHandle != IntPtr.Zero &&
                                            IsWindowVisible(process.MainWindowHandle);

                            if (isVisible)
                            {
                                // Process is fully loaded
                                processInfo.IsFullyLoaded = true;
                                processVisibilityStatus[process.Id] = true;
                                Debug.WriteLine($"Process {processInfo.ProcessName} is now loaded and visible!");
                            }
                            else
                            {
                                // At least one process is still loading
                                allLoaded = false;
                            }
                        }
                        catch
                        {
                            // Process might have exited, mark it as done
                            processInfo.IsFullyLoaded = true;
                        }
                    }

                    // Also check for any processes that might be starting but weren't caught by our event watcher
                    foreach (var pid in processVisibilityStatus.Keys.ToList())
                    {
                        if (!processVisibilityStatus[pid])
                        {
                            try
                            {
                                Process process = Process.GetProcessById(pid);
                                bool isVisible = process.MainWindowHandle != IntPtr.Zero &&
                                                IsWindowVisible(process.MainWindowHandle);

                                processVisibilityStatus[pid] = isVisible;

                                if (!isVisible && process.MainWindowHandle != IntPtr.Zero)
                                {
                                    // Process has a window but it's not visible yet
                                    allLoaded = false;
                                }
                            }
                            catch
                            {
                                // Process might have exited, remove it from our tracking
                                processVisibilityStatus.Remove(pid);
                            }
                        }
                    }
                }

                // If we're still waiting, sleep a bit
                if (!allLoaded)
                    Thread.Sleep(100);
            }

            if (!allLoaded)
                Debug.WriteLine("Timeout waiting for all processes to load");

            return allLoaded;
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