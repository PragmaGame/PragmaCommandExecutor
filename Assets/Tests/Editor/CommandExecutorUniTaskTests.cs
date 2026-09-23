#if COMMAND_EXECUTOR_UNITASK_SUPPORT
using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace Pragma.CommandExecutor.Tests
{
    public class CommandExecutorUniTaskTests
    {
        private const float FRAME = 0.25f;

        private CommandExecutor _executor;
        private List<string> _log;

        [SetUp]
        public void SetUp()
        {
            _executor = new CommandExecutor(null, Array.Empty<ICommandRegistrationContext>(), autoTick: false);
            _executor.AddRegistration<ProbeCommand, ProbeProcessor>();
            _log = new List<string>();
        }

        [TearDown]
        public void TearDown()
        {
            _executor.Dispose();
        }

        [Test]
        public void Await_RethrowsFault_InsteadOfLoggingIt()
        {
            var boom = Probe("boom", 1);
            boom.ThrowOnTick = true;

            var task = _executor.Execute(boom).ToUniTask();
            _executor.Tick(FRAME);

            Assert.AreEqual(UniTaskStatus.Faulted, task.Status);
            Assert.Throws<InvalidOperationException>(() => task.GetAwaiter().GetResult());
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void Await_CompletesNormally_WhenRunIsCancelled()
        {
            var handle = _executor.Execute(Probe("p", 5));
            var task = handle.ToUniTask();

            handle.Cancel();

            Assert.AreEqual(UniTaskStatus.Succeeded, task.Status);
            task.GetAwaiter().GetResult();
        }

        [Test]
        public void TokenCancellation_InterruptsRun()
        {
            using var cancellation = new CancellationTokenSource();
            var task = _executor.Execute(Probe("p", 5), cancellation.Token);

            cancellation.Cancel();

            Assert.AreEqual(UniTaskStatus.Succeeded, task.Status);
            Assert.Contains("interrupt:p", _log);
            task.GetAwaiter().GetResult();
        }

        private ProbeCommand Probe(string name, int frames)
        {
            return new ProbeCommand { Name = name, Frames = frames, Log = _log };
        }
    }
}
#endif
