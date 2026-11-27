using System;
using System.Collections.Generic;

namespace _DL.PlaySafe
{
    /// <summary>
    /// What to do for each request whose coroutine may never finish. Unity stops a coroutine without running its
    /// finally block when the GameObject is deactivated or destroyed, which would leave the awaited task pending forever.
    /// Kept free of UnityEngine so it can be unit-tested. Main thread only.
    /// </summary>
    internal sealed class PlaySafeInFlightRequests
    {
        private readonly List<Action> _abandon = new List<Action>();

        internal int Count => _abandon.Count;

        internal void Add(Action abandon) => _abandon.Add(abandon);

        internal void Remove(Action abandon) => _abandon.Remove(abandon);

        /// <summary>Runs every registered action once and forgets them. One that throws does not stop the rest.</summary>
        internal void AbandonAll()
        {
            if (_abandon.Count == 0)
                return;

            Action[] pending = _abandon.ToArray();
            _abandon.Clear();
            foreach (Action abandon in pending)
            {
                try
                {
                    abandon();
                }
                catch (Exception)
                {
                    // completing one caller's task must not keep the others pending
                }
            }
        }
    }
}
