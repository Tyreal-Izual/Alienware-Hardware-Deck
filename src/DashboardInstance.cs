using System;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Threading;

namespace HardwareDeck
{
    // The mutex is session-local and shared by normal and elevated copies for this user.
    // Probe, preview, and preset-worker processes intentionally do not acquire it.
    internal sealed class DashboardInstance : IDisposable
    {
        readonly Mutex mutex;
        readonly bool owns;
        internal bool Acquired { get { return owns; } }

        internal static string Name
        {
            get
            {
                using (var identity = WindowsIdentity.GetCurrent())
                    return @"Local\HardwareDeck.Dashboard." + identity.User.Value;
            }
        }

        internal DashboardInstance(string name, int waitMilliseconds = 0)
        {
            var security = new MutexSecurity();
            using (var identity = WindowsIdentity.GetCurrent())
                security.AddAccessRule(new MutexAccessRule(identity.User, MutexRights.FullControl, AccessControlType.Allow));
            try
            {
                bool created;
                mutex = new Mutex(true, name, out created, security);
                if (created) owns = true;
                else
                {
                    try { owns = mutex.WaitOne(waitMilliseconds); }
                    catch (AbandonedMutexException) { owns = true; }
                }
            }
            catch (UnauthorizedAccessException)
            {
                // A protected instance can still own the cross-integrity name.
                // Never open a second dashboard merely because it cannot be inspected.
                owns = false;
            }
        }

        public void Dispose()
        {
            if (mutex == null) return;
            if (owns) mutex.ReleaseMutex();
            mutex.Dispose();
        }
    }
}
