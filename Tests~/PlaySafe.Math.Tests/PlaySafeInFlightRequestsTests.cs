using System;
using System.Collections.Generic;
using NUnit.Framework;
using _DL.PlaySafe;

namespace _DL.PlaySafe.Tests
{
    public class PlaySafeInFlightRequestsTests
    {
        [Test]
        public void AbandonAll_RunsEachActionOnce_AndForgetsThem()
        {
            var requests = new PlaySafeInFlightRequests();
            int first = 0, second = 0;
            requests.Add(() => first++);
            requests.Add(() => second++);

            requests.AbandonAll();
            requests.AbandonAll();

            Assert.AreEqual(1, first);
            Assert.AreEqual(1, second);
            Assert.AreEqual(0, requests.Count);
        }

        [Test]
        public void ARemovedRequest_IsNotAbandoned()
        {
            var requests = new PlaySafeInFlightRequests();
            int finished = 0, pending = 0;
            Action done = () => finished++;
            requests.Add(done);
            requests.Add(() => pending++);

            requests.Remove(done);
            requests.AbandonAll();

            Assert.AreEqual(0, finished);
            Assert.AreEqual(1, pending);
        }

        [Test]
        public void OneActionThrowing_DoesNotKeepTheOthersPending()
        {
            var requests = new PlaySafeInFlightRequests();
            int completed = 0;
            requests.Add(() => throw new InvalidOperationException("already disposed"));
            requests.Add(() => completed++);

            Assert.DoesNotThrow(() => requests.AbandonAll());
            Assert.AreEqual(1, completed);
        }

        [Test]
        public void AnActionThatStartsANewRequest_LeavesThatRequestRegistered()
        {
            var requests = new PlaySafeInFlightRequests();
            var order = new List<string>();
            Action retry = () => order.Add("retry");
            requests.Add(() =>
            {
                order.Add("first");
                requests.Add(retry);
            });

            requests.AbandonAll();

            Assert.AreEqual(new[] { "first" }, order);
            Assert.AreEqual(1, requests.Count);
        }

        [Test]
        public void RemovingARequestThatWasNeverAdded_IsHarmless()
        {
            var requests = new PlaySafeInFlightRequests();
            Assert.DoesNotThrow(() => requests.Remove(() => { }));
            Assert.DoesNotThrow(() => requests.AbandonAll());
        }
    }
}
