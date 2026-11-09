using System;
using System.IO;
using System.IO.Pipes;
using System.Threading;

namespace VKonvert
{
    /// <summary>
    /// Explorer starts one process per selected file. The first VKonvert window keeps running
    /// and later launches hand their files to it, so "Open with" on 10 images gives one window.
    /// </summary>
    public sealed class SingleInstance : IDisposable
    {
        const string MutexName = @"Local\VKonvert.SingleInstance";
        const string PipeName = "VKonvert.OpenFiles";

        readonly Mutex mutex;
        public bool IsFirst { get; }

        public SingleInstance()
        {
            mutex = new Mutex(true, MutexName, out var createdNew);
            IsFirst = createdNew;
        }

        public bool SendToFirst(string[] files)
        {
            try
            {
                using (var pipe = new NamedPipeClientStream(".", PipeName, PipeDirection.Out))
                {
                    pipe.Connect(5000);
                    using (var w = new StreamWriter(pipe))
                    {
                        foreach (var f in files) w.WriteLine(f);
                        w.WriteLine("");
                    }
                }
                return true;
            }
            catch
            {
                return false;
            }
        }

        public void Listen(Action<string[]> onFiles)
        {
            var t = new Thread(() =>
            {
                while (true)
                {
                    try
                    {
                        using (var pipe = new NamedPipeServerStream(PipeName, PipeDirection.In))
                        {
                            pipe.WaitForConnection();
                            using (var r = new StreamReader(pipe))
                            {
                                var list = new System.Collections.Generic.List<string>();
                                string line;
                                while (!string.IsNullOrEmpty(line = r.ReadLine())) list.Add(line);
                                onFiles(list.ToArray());
                            }
                        }
                    }
                    catch
                    {
                        Thread.Sleep(200);
                    }
                }
            }) { IsBackground = true };
            t.Start();
        }

        public void Dispose()
        {
            if (IsFirst) mutex.ReleaseMutex();
            mutex.Dispose();
        }
    }
}
