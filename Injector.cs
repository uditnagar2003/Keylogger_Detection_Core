﻿using Keylogger_Core_1._0;
using System.Diagnostics;
using VisualKeyloggerDetector.Core.Monitoring;
using VisualKeyloggerDetector.Core.Translation;

namespace VisualKeyloggerDetector.Core.Injection
{
    public class InjectorResult : Dictionary<uint, List<ulong>> { }
  
    public class Injector
    {
        private readonly Random _random = new Random();
        // Characters to inject. Can be customized.
        private readonly string _charsToInject = "abcdefghijklmnopqrstuvwxyz";

        private ExperimentConfiguration _config;
        MonitoringResult monitoringResult;
        
        public event EventHandler<string> StatusUpdate;

        
        public event EventHandler<int> ProgressUpdate;

        public event EventHandler<ProcessWriteInfoData> ProcessInfoUpdate;

         protected  virtual void OnProcessInfoUpdate(ProcessWriteInfoData processWriteInfoData) => ProcessInfoUpdate?.Invoke(this, processWriteInfoData);


        protected virtual void OnStatusUpdate(string message) => StatusUpdate?.Invoke(this, message);


        protected virtual void OnProgressUpdate(int progress1) => ProgressUpdate?.Invoke(this, progress1);

       
        public async Task<InjectorResult> InjectStreamAsync(KeystrokeStreamSchedule schedule, ExperimentConfiguration _config1, CancellationToken cancellationToken = default)
        {
            _config = _config1 ?? throw new ArgumentNullException(nameof(_config1));
            if (schedule == null) throw new ArgumentNullException(nameof(schedule));

            OnStatusUpdate("Starting keystroke injection...");
            Debug.WriteLine("Starting keystroke injection... " + DateTime.Now.ToString("HH:mm:ss.fff"));
            int totalIntervals = schedule.KeysPerInterval.Count;
            var stopwatch = new Stopwatch();

            var results = new InjectorResult();
            var processIdList = _config.ProcessIdsToMonitor?.ToList() ?? new List<uint>();
            var processSet = new HashSet<uint>(processIdList);
            // Initialize results structure for expected processes
            foreach (uint pid in processSet)
            {
                results[pid] = new List<ulong>(_config.PatternLengthN);
            }

            var objectsToMonitor = new Monitors(_config);
           
            for (int i = 0; i < totalIntervals; i++)
            {
                // Check for cancellation at the start of each interval
                cancellationToken.ThrowIfCancellationRequested();

                int keysInThisInterval = schedule.KeysPerInterval[i];
                int intervalDuration = schedule.IntervalDurationMs;
                OnStatusUpdate($"Interval {i + 1}/{totalIntervals}: Injecting {keysInThisInterval} keys over {intervalDuration}ms.");
                Debug.WriteLine($"Interval {i + 1}/{totalIntervals}: Injecting {keysInThisInterval} keys over {intervalDuration}ms. " + DateTime.Now.ToString("HH:mm:ss.fff"));
                stopwatch.Restart();

                if (keysInThisInterval > 0 && intervalDuration > 0) // Ensure duration is positive for delay calculation
                {
                    // Distribute keys somewhat evenly within the interval
                    // Calculate average delay, handling potential division by zero if intervalDuration is 0
                    double delayBetweenKeys = (double)intervalDuration / keysInThisInterval;
                    double accumulatedDelayError = 0; // Accumulates fractional parts of delays
                    var initialProcessInfo = await ProcessMonitor.GetAllProcessesInfoAsync();
                    foreach (var pInfo in initialProcessInfo)
                    {
                        if (processSet.Contains(pInfo.Id))
                        {
                            objectsToMonitor.lastWriteCounts[pInfo.Id] = pInfo.WriteCount;
                            if (!results.ContainsKey(pInfo.Id))
                                results[pInfo.Id] = new List<ulong>(_config.PatternLengthN);
                        }
                        else
                        {
                          /*  _config.processInfoDatas.Add(new ProcessInfoData
                            {
                                Id = pInfo.Id,
                                Name = pInfo.Name,
                                ExecutablePath = pInfo.ExecutablePath,
                                WriteCount = pInfo.WriteCount
                            });*/
                            _config.ProcessInfoDataMap[pInfo.Id] = pInfo; // Update the map with the new process info
                            // Fix for CS8602: Dereference of a possibly null reference.
                            if (_config.ProcessIdsToMonitor != null)
                            {
                                _config.ProcessIdsToMonitor.Add(pInfo.Id);
                            }

                            processSet.Add(pInfo.Id);
                            processIdList.Add(pInfo.Id);
                            results[pInfo.Id] = new List<ulong>(_config.PatternLengthN);
                            objectsToMonitor.lastWriteCounts[pInfo.Id] = pInfo.WriteCount; // Initialize to 0 for non-target processes
                        }
                    }
                    for (int k = 0; k < keysInThisInterval; k++)
                    {
                        // Check for cancellation before each key injection
                        cancellationToken.ThrowIfCancellationRequested();

                        // Inject a random character
                        try
                        {
                            char charToSend = _charsToInject[_random.Next(_charsToInject.Length)];
                            // Uses the static helper class KeyInputInjector (defined elsewhere)
                            KeyInputInjector.SendCharacter(charToSend);
                            // Console.WriteLine($"Injected Charater {DateTime.Now.ToString("HH:mm:ss.fff")} " + charToSend);
                        }
                        catch (Exception ex)
                        {
                            // Log error but continue injection if possible
                            OnStatusUpdate($"Error sending key: {ex.Message}. Skipping key.");
                        }

                        // Calculate delay until the *next* key injection
                        // Only delay if there are more keys to send in this interval
                        if (k < keysInThisInterval - 1)
                        {
                            double currentDelay = delayBetweenKeys + accumulatedDelayError;
                            int waitTimeMs = (int)Math.Floor(currentDelay);
                            accumulatedDelayError = currentDelay - waitTimeMs; // Carry over the fractional part

                            if (waitTimeMs > 0)
                            {
                                await Task.Delay(waitTimeMs, cancellationToken);
                            }
                        }
                       // CHange focus of the application when injection of half number of key of the particular interval ahs been done
                        if (k == keysInThisInterval / 2)
                        {
                            FocusHandler.ForceFocusChange();
                        }
                    } // End key loop (k)
                } // End if keysInThisInterval > 0

                stopwatch.Stop();

                Task<MonitoringResult> result;
                Debug.WriteLine($"Monitoring processes for interval {i + 1} at {DateTime.Now.ToString("HH:mm:ss.fff")}");
                result = objectsToMonitor.MonitorProcessesAsync(processIdList, cancellationToken);

                monitoringResult = await result;
                foreach (uint pid in processSet)
                {
                    if (!results.ContainsKey(pid))
                        results[pid] = new List<ulong>(_config.PatternLengthN); // Should not happen if initialized correctly, but safety check

                    // Only add if the list isn't already full (e.g., due to errors)
                    try
                    {
                        if (results[pid].Count < _config.PatternLengthN && monitoringResult.ContainsKey(pid))
                        {
                            //ProcessInfoData info = (ProcessInfoData)_config.processInfoDatas.Where(p => p.Id == pid);
                            ProcessInfoData pro = _config.ProcessInfoDataMap[pid]; // Get process info from the map
                            ProcessWriteInfoData infoData = new ProcessWriteInfoData
                            {
                                Id =pid,
                                Name =pro.Name ,
                                ExecutablePath = pro.ExecutablePath,
                                WriteCount = monitoringResult[pid]
                            };
                           
                            OnProcessInfoUpdate(infoData); // Notify about process info update
                            results[pid].Add(monitoringResult[pid]);
                            // Console.WriteLine($"PID {pid}: Interval {i + 1} - Bytes Written: {monitoringResult[pid]} {DateTime.Now.ToString("HH:mm:ss.fff")}");
                        }
                    }
                    catch
                    {
                        Debug.WriteLine("error not finding the process id in monitoring result");
                    }
                }

                // Ensure the full interval duration is respected by waiting for any remaining time.
                int elapsedTime = (int)stopwatch.ElapsedMilliseconds;
                int remainingTime = intervalDuration - elapsedTime + _config.T;
                if (remainingTime > 0)
                {
                    await Task.Delay(remainingTime, cancellationToken);
                }
                await Task.Delay(1000, cancellationToken); // Optional delay before finishing

                //Console.WriteLine($"interval ended {DateTime.Now.ToString("HH:mm:ss.fff")} " + i);
                OnProgressUpdate(i+4); // Report progress after completing interval i

            } // End interval loop (i)
            foreach (var pid in processSet)
            {
                if (results.TryGetValue(pid, out var list))
                {
                    while (list.Count < _config.PatternLengthN)
                    {
                        list.Add(0); // Pad missing intervals with 0
                    }
                }
            }
            InjectorResult filteredResult = new InjectorResult();

            foreach (var kvp in results)
            {
                int zeroCount = kvp.Value.Count(b => b == 0);
                int halfLength = kvp.Value.Count / 2;

                if (zeroCount < halfLength)
                {
                    filteredResult[kvp.Key] = kvp.Value;
                }
            }
            //OnProgressUpdate(totalIntervals - 1); // Indicate completion of the last interval
            OnStatusUpdate("Injection finished.");
            return filteredResult;

        }
    }
}