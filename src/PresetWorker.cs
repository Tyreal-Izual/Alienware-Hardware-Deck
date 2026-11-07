using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;

namespace HardwareDeck
{
    internal sealed class PresetWorkerRequest
    {
        public PresetRequest Request { get; set; }
        public long CallerWindow { get; set; }
        public int CallerProcess { get; set; }
    }

    internal static class PresetWorker
    {
        internal static string ReadInput(TextReader reader)
        {
            var input = new StringBuilder();
            for (int count = 0; count <= 4096; count++)
            {
                int c = reader.Read();
                if (c < 0 || c == '\n' || c == '\r')
                {
                    if (c == '\r' && reader.Peek() == '\n')
                        reader.Read();
                    return input.Length == 0 ? null : input.ToString();
                }

                if (count == 4096)
                    return null;
                input.Append((char)c);
            }

            return null;
        }

        internal static void WriteResult(TextWriter writer, PresetResult result)
        {
            try
            {
                writer.WriteLine(new JavaScriptSerializer().Serialize(result));
                writer.Flush();
            }
            catch (IOException)
            {
            } // Closing the dashboard closes the result pipe.
            catch (ObjectDisposedException)
            {
            }
        }

        internal static PresetResult RunBackground(Func<PresetResult> operation, int timeout, Action onTimeout)
        {
            PresetResult workerResult = null;
            var thread = new Thread(delegate ()
            {
                try
                {
                    workerResult = operation();
                }
                catch (Exception e)
                {
                    workerResult = new PresetResult
                    {
                        Message = e.Message
                    };
                }
            })
            {
                IsBackground = true
            };
            thread.SetApartmentState(ApartmentState.MTA);
            thread.Start();
            if (thread.Join(timeout))
                return workerResult ?? new PresetResult
                {
                    Message = "No preset result received"
                };
            onTimeout();
            // Never share this timeout object with the still-finishing worker.
            return new PresetResult
            {
                Message = "AWCC background operation timed out; verify current CPU and cooling before retrying"
            };
        }

        internal static int Run()
        {
            Console.SetOut(new StreamWriter(Console.OpenStandardOutput(), new UTF8Encoding(false)) { AutoFlush = true });
            Console.SetIn(new StreamReader(Console.OpenStandardInput(), Encoding.UTF8));
            PresetResult result = null;
            string input;
            try
            {
                input = ReadInput(Console.In);
            }
            catch (IOException)
            {
                return 2;
            }

            if (input == null)
                return 2;
            PresetWorkerRequest request;
            try
            {
                request = new JavaScriptSerializer().Deserialize<PresetWorkerRequest>(input);
                if (request == null || request.Request == null)
                    throw new InvalidOperationException();
            }
            catch
            {
                WriteResult(Console.Out, new PresetResult { Message = "Invalid preset worker request" });
                return 2;
            }

            bool acquired = false;
            using (var gate = new Mutex(false, @"Local\HardwareDeck.AwccPresetSwitch"))
            {
                try
                {
                    try
                    {
                        acquired = gate.WaitOne(0);
                    }
                    catch (AbandonedMutexException)
                    {
                        acquired = true;
                    }

                    if (!acquired)
                        throw new InvalidOperationException("Another preset change is in progress");
                    result = RunBackground(delegate
                    {
                        using (var backend = new LocalPresetBackend(new IntPtr(request.CallerWindow), request.CallerProcess))
                            return PresetTransaction.Execute(request.Request, backend);
                    }, 85000, AwccPresetBridge.StopOwnedFrontend);
                }
                catch (Exception e)
                {
                    result = new PresetResult
                    {
                        Message = e.Message
                    };
                }
                finally
                {
                    if (acquired)
                        gate.ReleaseMutex();
                }
            }

            WriteResult(Console.Out, result);
            return result != null && result.Success ? 0 : 1;
        }

        internal static PresetResult Execute(PresetRequest request, IntPtr caller)
        {
            using (var process = new Process())
            {
                process.StartInfo = new ProcessStartInfo
                {
                    FileName = System.Reflection.Assembly.GetExecutingAssembly().Location,
                    Arguments = "--preset-worker",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardInput = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8
                };
                process.Start();
                var output = process.StandardOutput.ReadToEndAsync();
                var errors = process.StandardError.ReadToEndAsync();
                int callerProcess;
                using (var current = Process.GetCurrentProcess())
                    callerProcess = current.Id;
                process.StandardInput.WriteLine(new JavaScriptSerializer().Serialize(new PresetWorkerRequest { Request = request, CallerWindow = caller.ToInt64(), CallerProcess = callerProcess }));
                process.StandardInput.Close();
                if (!process.WaitForExit(95000))
                {
                    try
                    {
                        process.Kill();
                    }
                    catch
                    {
                    }

                    return new PresetResult
                    {
                        Message = "Preset worker did not complete; current state needs verification"
                    };
                }

                try
                {
                    return new JavaScriptSerializer().Deserialize<PresetResult>(output.Result) ?? new PresetResult
                    {
                        Message = "No preset result received"
                    };
                }
                catch
                {
                    return new PresetResult
                    {
                        Message = "Preset worker could not report a verified result"
                    };
                }
            }
        }
    }
}
