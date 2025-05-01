using System;
using System.Diagnostics;
using System.Linq;
using System.Management;
using System.Threading;

class Program
{
    static int lastWidth = Console.WindowWidth;
    static int lastHeight = Console.WindowHeight;
    static CpuUsageTracker cpuTracker = new();

    static void Main()
    {
        // if the console window size has changed, clear the screen
        if (Console.WindowWidth != lastWidth || Console.WindowHeight != lastHeight)
        {
            Console.Clear();
            lastWidth = Console.WindowWidth;
            lastHeight = Console.WindowHeight;
        }

        // Set console title and hide cursor
        Console.Title = "WinTop - System Monitor";
        Console.CursorVisible = false;

        // Get the number of logical CPU cores available
        var cpuCores = Environment.ProcessorCount;

        // Create a PerformanceCounter for each CPU core to track its % usage
        var cpuCounters = Enumerable.Range(0, cpuCores)
            .Select(i => new PerformanceCounter("Processor", "% Processor Time", i.ToString()))
            .ToArray();

        // Get total physical memory in MB (custom method)
        var ramTotalMb = GetTotalMemoryInMB();

        // Prepare system uptime counter
        var uptimeCounter = new PerformanceCounter("System", "System Up Time");
        uptimeCounter.NextValue(); // Prime the counter (first call returns 0)

        // Retrieve active network adapter name and its maximum speed (Mbps)
        (string adapterName, float maxMbps) = GetActiveNetworkAdapter();



        while (true)
        {
            // Check if the console window size has changed
            CheckAndResetScreen();
            Console.SetCursorPosition(0, 0);

            // Title Bar
            Console.BackgroundColor = ConsoleColor.Green;
            Console.ForegroundColor = ConsoleColor.Black;
            Console.WriteLine("WinTop".PadLeft((Console.WindowWidth - 1) / 2 + 1).PadRight(Console.WindowWidth - 1));
            Console.ResetColor();

            // Per-core CPU usage
            for (int i = 0; i < cpuCores; i++)
            {
                float usage = cpuCounters[i].NextValue();
                DrawCpuLine(i, usage);
            }

            // System info block
            DrawMemoryAndSystemBlock();

            // Add --- line for separation
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine(new string('-', Console.WindowWidth));
            Console.ResetColor();

            // Uptime
            DrawSysStats(uptimeCounter, adapterName, maxMbps);

            // System stats
            DrawProcessHeader();

            // Process list
            int maxRows = Math.Min(30, Console.WindowHeight - 15);

            // Get the top 30 processes by memory usage
            var processes = Process.GetProcesses()
                .OrderByDescending(p => p.WorkingSet64) // Sort processes by memory usage in descending order
                .Take(30)                               // Take only the top 30 memory-consuming processes
                .ToList();

            cpuTracker.Update(processes);              // Update CPU usage only for the selected 30 processes
            DrawProcessList(cpuTracker.CurrentCpuUsages);

            Thread.Sleep(1000);
        }


    }


    static void CheckAndResetScreen()
    {
        // Check if the console window size has changed
        if (Console.WindowWidth != lastWidth || Console.WindowHeight != lastHeight)
        {
            Console.Clear();
            lastWidth = Console.WindowWidth;
            lastHeight = Console.WindowHeight;
        }
        else
        {
            Console.SetCursorPosition(0, 0); // reset draw head
        }
    }

    static void DrawCpuLine(int core, float usage)
    {
        // Format the core number as two digits, e.g., 01, 02...
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.Write($"{core:00}[");
        Console.ResetColor();

        // Draw usage bar and right-aligned percentage
        Console.ForegroundColor = GetUsageColor(usage);
        int barLength = 20;
        string bars = new string('|', (int)(usage / 6.25f)); // Scale to 16-char bar
        string barLine = bars.PadRight(barLength - 5) + $"{usage,4:0.0}%";
        Console.Write(barLine);
        Console.ResetColor();

        Console.Write("]");

        if ((core + 1) % 4 == 0)
            Console.WriteLine();
        else
            Console.Write("  ");


    }


    static void DrawMemLine()
    {
        // Add empty line for separation
        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.WriteLine(new string('-', Console.WindowWidth));
        Console.ResetColor();

        // Get RAM usage percentage and total memory
        float memUsage = GetRamUsagePercent(out float usedMb, out float totalMb);

        Console.ForegroundColor = ConsoleColor.DarkYellow;
        Console.Write("Mem[");
        Console.ResetColor();

        Console.ForegroundColor = GetUsageColor(memUsage);

        // Draw usage bar and right-aligned percentage
        int barLength = 20;
        string bars = new string('|', (int)(memUsage / 6.25f)); // Scale to 16 units
        string barLine = bars.PadRight(barLength - 5) + $"{memUsage,3:0}%";
        Console.Write(barLine);

        Console.ResetColor();
        Console.ForegroundColor = ConsoleColor.DarkYellow;
        Console.Write($"] {usedMb / 1024f:0.0}G/{totalMb / 1024f:0.0}G");
        Console.ResetColor();
        Console.WriteLine();
    }

    static void DrawSwapLine()
    {
        // Get swap usage percentage and total memory
        (float percent, float used, float total) = GetSwapUsage();

        Console.ForegroundColor = ConsoleColor.DarkYellow;
        Console.Write("Swp[");
        Console.ResetColor();

        Console.ForegroundColor = GetUsageColor(percent);

        // Draw usage bar and right-aligned percentage
        int barLength = 20;
        string bars = new string('|', (int)(percent / 6.25f)); // Scale to 16 units
        string barLine = bars.PadRight(barLength - 5) + $"{percent,3:0}%";
        Console.Write(barLine);

        Console.ResetColor();
        Console.ForegroundColor = ConsoleColor.DarkYellow;
        Console.Write($"] {used / 1024f:0.0}G/{total / 1024f:0.0}G");

        Console.WriteLine();
    }


    static void DrawSysStats(PerformanceCounter uptimeCounter, string adapterName, float maxMbps)
    {
        // Get current RX/TX in Mbps
        (float rxMbps, float txMbps) = GetNetworkUsage(adapterName);

        // Calculate percentage usage based on max speed
        float rxPercent = (rxMbps / maxMbps) * 100f;
        float txPercent = (txMbps / maxMbps) * 100f;

        // Get color based on usage percentage
        int barLength = 22;
        string rxBar = new string('|', (int)(rxPercent / 5)).PadRight(barLength - 8) + $"{rxPercent,4:0}%";
        string txBar = new string('|', (int)(txPercent / 5)).PadRight(barLength - 8) + $"{txPercent,4:0}%";

        // Get task stats
        (int totalTasks, int totalThreads, int runningTasks) = GetTaskStats();
        TimeSpan up = TimeSpan.FromSeconds(uptimeCounter.NextValue());

        // Format task stats
        string tasksInfo = $"Tasks: {totalTasks} total, {totalThreads} thr; {runningTasks} running";
        string uptimeInfo = $"Uptime: {up:hh\\:mm\\:ss}";

        // RX line
        Console.Write($"Rx [{rxBar}] {rxMbps,6:0.0} Mbps");
        Console.SetCursorPosition(55, Console.CursorTop);
        Console.WriteLine(tasksInfo);

        // TX line
        Console.Write($"Tx [{txBar}] {txMbps,6:0.0} Mbps");
        Console.SetCursorPosition(55, Console.CursorTop);
        Console.WriteLine(uptimeInfo);
    }



    static ConsoleColor GetUsageColor(float percent)
    {
        // Color based on percentage
        if (percent < 60) return ConsoleColor.Green;
        else if (percent < 85) return ConsoleColor.Yellow;
        else return ConsoleColor.Red;
    }

    static float GetRamUsagePercent(out float usedMb, out float totalMb)
    {
        // Get RAM usage percentage and total memory
        usedMb = 0;
        totalMb = 0;
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT TotalVisibleMemorySize, FreePhysicalMemory FROM Win32_OperatingSystem");
            foreach (ManagementObject mo in searcher.Get())
            {
                totalMb = Convert.ToUInt64(mo["TotalVisibleMemorySize"]) / 1024f;
                float freeMb = Convert.ToUInt64(mo["FreePhysicalMemory"]) / 1024f;
                usedMb = totalMb - freeMb;
                return (usedMb / totalMb) * 100f;
            }
        }
        catch { }
        return 0;
    }

    static (float percent, float usedMb, float totalMb) GetSwapUsage()
    {
        // Get swap usage percentage and total memory
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT AllocatedBaseSize, CurrentUsage FROM Win32_PageFileUsage");
            foreach (ManagementObject mo in searcher.Get())
            {
                float total = Convert.ToUInt32(mo["AllocatedBaseSize"]);    // MB
                float used = Convert.ToUInt32(mo["CurrentUsage"]);          // MB
                float percent = (total > 0) ? used / total * 100f : 0f;
                return (percent, used, total); // hepsi MB
            }
        }
        catch { }
        return (0, 0, 1);
    }


    static float GetTotalMemoryInMB()
    {
        // Get total physical memory in MB
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT TotalVisibleMemorySize FROM Win32_OperatingSystem");
            foreach (ManagementObject mo in searcher.Get())
                return Convert.ToUInt64(mo["TotalVisibleMemorySize"]) / 1024f;
        }
        catch { }
        return 0;
    }

    static void DrawProcessHeader()
    {
        Console.WriteLine(); // Add empty line for separation

        // Column names
        Console.BackgroundColor = ConsoleColor.DarkGray;
        Console.ForegroundColor = ConsoleColor.Gray;
        Console.WriteLine("PID   THR PRI  CPU%   MEM     TIME  STIME ARCH  COMMAND".PadRight(Console.WindowWidth));
        Console.ResetColor();
        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.WriteLine(new string('-', Console.WindowWidth));
        Console.ResetColor();
    }

    static void DrawProcessList(Dictionary<int, float> cpuUsages)
    {
        // Get the top 30 processes by memory usage
        int maxRows = Math.Min(30, Console.WindowHeight - 15);

        var processes = Process.GetProcesses()
            .OrderByDescending(p => p.WorkingSet64)
            .Take(maxRows);

        // Draw process list
        foreach (var proc in processes)
        {
            try
            {
                string pid = proc.Id.ToString().PadRight(6);
                string thr = proc.Threads.Count.ToString().PadRight(4);
                string pri = proc.BasePriority.ToString().PadRight(4);
                string time = (DateTime.Now - proc.StartTime).ToString(@"hh\:mm\:ss").PadLeft(8);
                string stime = proc.StartTime.ToString("HH:mm").PadRight(6);

                string name = proc.ProcessName.Length > 50
                    ? proc.ProcessName.Substring(0, 30) + "..."
                    : proc.ProcessName;

                string mem = (proc.WorkingSet64 / 1024 / 1024).ToString().PadLeft(5) + "M";

                float cpuValue = cpuUsages.TryGetValue(proc.Id, out var usage) ? usage : 0f;
                string cpu = $"{cpuValue,4:0}".PadLeft(5);

                // ARCH: try to detect architecture
                string arch = "   ";
                try
                {
                    arch = Environment.Is64BitOperatingSystem
                        ? (proc.MainModule.FileName.Contains(" (x86)") ? "x86" : "x64")
                        : "x86";
                }
                catch { arch = "N/A"; }

                Console.WriteLine($"{pid}{thr}{pri}{cpu}% {mem} {time} {stime} {arch}  {name}".PadRight(Console.WindowWidth));
            }
            catch
            {
                // Skip inaccessible or terminated processes
            }
        }

    }

    // Uncomment this if you want to show the user of the process
    // IMPORTANT: There is a very serious performance problem

    //static string GetProcessUser(int pid)
    //{
    //    try
    //    {
    //        string query = $"SELECT * FROM Win32_Process WHERE ProcessId = {pid}";
    //        using var searcher = new ManagementObjectSearcher(query);
    //        foreach (ManagementObject mo in searcher.Get())
    //        {
    //            string[] owner = new string[2];
    //            int result = Convert.ToInt32(mo.InvokeMethod("GetOwner", owner));
    //            if (result == 0)
    //                return (owner[1] + "\\" + owner[0]).PadRight(18); // DOMAIN\Username
    //        }
    //    }
    //    catch { }
    //    return "SYSTEM".PadRight(18);
    //}

    class CpuUsageTracker
    {
        // Dictionary to store previous CPU times and timestamps for each process
        private Dictionary<int, TimeSpan> prevCpuTimes = new();
        private Dictionary<int, DateTime> prevTimestamps = new();
        public Dictionary<int, float> CurrentCpuUsages = new();

        public void Update(IEnumerable<Process> processes)
        {
            var now = DateTime.Now;

            foreach (var proc in processes)
            {
                try
                {
                    TimeSpan currentCpu = proc.TotalProcessorTime;

                    if (prevCpuTimes.TryGetValue(proc.Id, out var prevCpu) &&
                        prevTimestamps.TryGetValue(proc.Id, out var prevTime))
                    {
                        double cpuDelta = (currentCpu - prevCpu).TotalMilliseconds;
                        double timeDelta = (now - prevTime).TotalMilliseconds;

                        if (timeDelta > 0)
                            CurrentCpuUsages[proc.Id] = (float)(cpuDelta / timeDelta / Environment.ProcessorCount * 100);
                    }

                    prevCpuTimes[proc.Id] = currentCpu;
                    prevTimestamps[proc.Id] = now;
                }
                catch
                {
                    // Skip access denied or exited processes
                }
            }
        }
    }


    static void DrawMemoryAndSystemBlock()
    {
        // Left side: Mem and Swp
        DrawMemLine();
        DrawSwapLine();
        DrawCpuTotalLine();

        // Right side: System Info
        int leftBlockWidth = 50;
        int rightStartX = leftBlockWidth + 5;

        // System Info
        string cpuModel = GetCpuModel();
        string gpuModel = GetGpuModel();
        string ramSize = GetRamSize();

        string[] systemInfo = new[]
        {
        $"CPU : {cpuModel}",
        $"GPU : {gpuModel}",
        $"RAM : {ramSize}"
    };

        // Align to Mem line
        int currentLine = Console.CursorTop;
        Console.SetCursorPosition(rightStartX, currentLine - 3);

        foreach (var line in systemInfo)
        {
            Console.SetCursorPosition(rightStartX, Console.CursorTop);
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine(line.PadRight(Console.WindowWidth - rightStartX));
            Console.ResetColor();
        }
    }


    static string GetCpuModel()
    {
        // Get CPU model using WMI
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT Name FROM Win32_Processor");
            foreach (ManagementObject mo in searcher.Get())
                return mo["Name"]?.ToString()?.Trim() ?? "Unknown CPU";
        }
        catch
        {
            // Handle exceptions gracefully  
        }
        return "Unknown CPU"; // Ensure all code paths return a value  
    }
    static string GetGpuModel()
    {
        // Get GPU model using WMI
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT Name FROM Win32_VideoController");
            foreach (ManagementObject mo in searcher.Get())
                return mo["Name"]?.ToString()?.Trim() ?? "Unknown GPU";
        }
        catch
        {
            // Handle exceptions gracefully  
        }
        return "Unknown CPU"; // Ensure all code paths return a value  
    }

    static string GetRamSize()
    {
        // Get total physical memory using WMI
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT TotalPhysicalMemory FROM Win32_ComputerSystem");
            foreach (ManagementObject mo in searcher.Get())
            {
                ulong bytes = (ulong)mo["TotalPhysicalMemory"];
                double gb = bytes / 1024.0 / 1024.0 / 1024.0;
                return $"{gb:0.0} GB";
            }
        }
        catch
        {
            // Handle exceptions gracefully  
        }
        return "Unknown CPU"; // Ensure all code paths return a value  
    }

    static void DrawCpuTotalLine()
    {
        // Get CPU usage for all cores combined
        using var cpuTotalCounter = new PerformanceCounter("Processor", "% Processor Time", "_Total");
        cpuTotalCounter.NextValue();
        Thread.Sleep(100); // Allow sampling

        float usage = cpuTotalCounter.NextValue();

        Console.ForegroundColor = ConsoleColor.DarkYellow;
        Console.Write("Cpu[");
        Console.ResetColor();

        Console.ForegroundColor = GetUsageColor(usage);

        // Draw usage bar and right-aligned percentage
        int barLength = 20;
        string bars = new string('|', (int)(usage / 6.25f)); // Scale to 16 units
        string barLine = bars.PadRight(barLength - 5) + $"{usage,3:0}%";
        Console.Write(barLine);

        Console.ResetColor();
        Console.ForegroundColor = ConsoleColor.DarkYellow;
        Console.Write($"] [Total]");
        Console.ResetColor();

        Console.WriteLine();
    }

    static (int totalTasks, int totalThreads, int runningTasks) GetTaskStats()
    {
        // Get task stats using WMI
        int totalTasks = 0;
        int totalThreads = 0;
        int runningTasks = 0;

        try
        {
            // Get the total number of processes and threads
            var processes = Process.GetProcesses();
            totalTasks = processes.Length;
            totalThreads = processes.Sum(p => p.Threads.Count);
            runningTasks = processes.Count(p => p.Responding);
        }
        catch
        {
            // Handle exceptions gracefully  
        }

        return (totalTasks, totalThreads, runningTasks);
    }

    static (string adapterName, float maxMbps) GetActiveNetworkAdapter()
    {
        // Get the active network adapter name and its maximum speed
        try
        {
            var searcher = new ManagementObjectSearcher("SELECT Name, NetConnectionStatus, Speed FROM Win32_NetworkAdapter WHERE NetEnabled = true");
            foreach (ManagementObject mo in searcher.Get())
            {
                var name = mo["Name"]?.ToString();
                var speed = mo["Speed"] != null ? Convert.ToUInt64(mo["Speed"]) : 0; // in bits per second
                if (!string.IsNullOrWhiteSpace(name) && speed > 0)
                {
                    return (name, speed / 1_000_000f); // Convert to Mbps
                }
            }
        }
        catch { }
        return ("", 1000f); // fallback
    }
    static (float rxMbps, float txMbps) GetNetworkUsage(string adapterName)
    {
        // Get network usage for the specified adapter
        try
        {
            using var rxCounter = new PerformanceCounter("Network Interface", "Bytes Received/sec", adapterName);
            using var txCounter = new PerformanceCounter("Network Interface", "Bytes Sent/sec", adapterName);

            rxCounter.NextValue(); txCounter.NextValue();
            Thread.Sleep(1000);

            // Convert to Mbps
            float rx = rxCounter.NextValue() * 8 / 1_000_000f;
            float tx = txCounter.NextValue() * 8 / 1_000_000f;

            return (rx, tx);
        }
        catch { return (0f, 0f); }
    }

}
