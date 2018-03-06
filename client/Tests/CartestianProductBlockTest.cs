using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using MapStitcher;
using System.Threading.Tasks.Dataflow;
using System.Linq;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Tests
{
    [TestClass]
    public class CartestianProductBlockTest
    {
        [TestMethod]
        public async Task TestTransformManyBlockBuffersInput()
        {
            var block = new TransformManyBlock<string, char>(x => { Console.WriteLine("processign"); return x.ToCharArray(); });

            block.Post("a");
            Console.WriteLine("Hello"); // This happens before processing! Post is synchronous only to message accept, not to message process.

            // This shouldn't block
            block.Post("b");
            block.Complete();

            await Drain(block);

            try
            {
                Console.WriteLine(block.Receive());
                Assert.Fail("Should have throw, no input to receive");
            } catch (InvalidOperationException e)
            {
                Assert.IsTrue(true, "Threw expected exception");
            }
        }

        // TODO: This fails all the time. Why?
        [TestMethod, Ignore]
        public async Task TestMultiplePublishersWithPropagateCompletion()
        {
            var block1 = new TransformManyBlock<string, char>(x => x.ToCharArray());
            var block2 = new TransformManyBlock<string, char>(x => x.ToCharArray());

            var target = new BufferBlock<char>();

            var propagate = new DataflowLinkOptions { PropagateCompletion = true };
            block1.LinkTo(target, propagate);
            block2.LinkTo(target, propagate);

            block1.Post("a");
            // This propagates completion through to target
            block1.Complete();
            await AssertCompletes(block1.Completion);

            Assert.IsTrue(await target.OutputAvailableAsync(), "target should have message waiting");
            Assert.IsTrue(!target.Completion.IsCompleted, "target won't be complete until buffer empty");

            // The target won't receive this, because it's already started completing (via propagation)
            block2.Post("b");
            block2.Complete();

            // Pulling this out of the buffer allows the target to complete
            Assert.AreEqual('a', target.Receive());

            // This sholud happen pretty quickly
            await AssertCompletes(target.Completion);
        }

        [TestMethod]
        public async Task TestMultiplePublishers()
        {
            var block1 = new TransformManyBlock<string, char>(x => x.ToCharArray());
            var block2 = new TransformManyBlock<string, char>(x => x.ToCharArray());

            var target = new BufferBlock<char>();

            block1.LinkTo(target);
            block2.LinkTo(target);

            block1.Post("a");
            block1.Complete();
            await AssertCompletes(block1.Completion);

            block2.Post("b");
            block2.Complete();
            await AssertCompletes(block2.Completion);

            Assert.AreEqual('a', target.Receive());
            Assert.AreEqual('b', target.Receive());
        }

        [TestMethod, Timeout(5000)]
        public async Task ProducesCartesianProductOfInputs()
        {
            var block = new CartesianProductBlock<int, string>();
            var target = new BufferBlock<Tuple<int, string>>();

            var left = block.Left;
            var right = block.Right;

            block.LinkTo(target, new DataflowLinkOptions { PropagateCompletion = true });

            Assert.IsTrue(left.Post(1));
            Assert.IsTrue(right.Post("a"));
            Assert.IsTrue(left.Post(2));
            Assert.IsTrue(right.Post("b"));

            left.Complete();
            right.Complete();

            var actual = await Drain(target);

            var expected = new List<Tuple<int, string>>()
            {
                Tuple.Create(1, "a"),
                Tuple.Create(2, "a"),
                Tuple.Create(1, "b"),
                Tuple.Create(2, "b"),
            };

            Console.WriteLine("Made it to end");

            CollectionAssert.AreEquivalent(expected, actual.ToList());
        }

        [TestMethod]
        public async Task DoesNotPropagateCompletenessWhenNotAsked()
        {
            var block = new CartesianProductBlock<int, string>();
            var target = new BufferBlock<Tuple<int, string>>();

            block.LinkTo(target, new DataflowLinkOptions() { PropagateCompletion = false });

            block.Complete();
            await AssertCompletes(block.Completion);

            Assert.IsTrue(!target.Completion.IsCompleted);
        }

        [TestMethod]
        public async Task PropagatesCompletionWhenAsked()
        {
            var block = new CartesianProductBlock<int, string>();
            var target = new BufferBlock<Tuple<int, string>>();

            block.LinkTo(target, new DataflowLinkOptions() { PropagateCompletion = true });

            block.Complete();
            await AssertCompletes(block.Completion);
            await AssertCompletes(target.Completion);
        }

        [TestMethod]
        public async Task UnlinkingNoLongerPropagatesCompleteness()
        {
            var block = new CartesianProductBlock<int, string>();
            var target = new BufferBlock<Tuple<int, string>>();

            var link = block.LinkTo(target, new DataflowLinkOptions() { PropagateCompletion = true });
            link.Dispose();

            block.Complete();
            await AssertCompletes(block.Completion);

            Assert.IsFalse(target.Completion.IsCompleted);
        }

        [TestMethod]
        public async Task UnlinkingNoLongerPropagatesMessages()
        {
            var block = new CartesianProductBlock<int, string>();
            var target = new BufferBlock<Tuple<int, string>>();
            var target2 = new BufferBlock<Tuple<int, string>>();

            var link = block.LinkTo(target, new DataflowLinkOptions { PropagateCompletion = true });
            link.Dispose();
            block.LinkTo(target2);

            block.Left.Post(1);
            block.Right.Post("a");

            block.Complete();
            await AssertCompletes(block.Completion);

            target.Complete();
            await AssertCompletes(target.Completion);
            Assert.AreEqual(0, target.Count);
            Assert.AreEqual(1, target2.Count);
        }

        [TestMethod]
        public async Task CompletesWhenBothInputsComplete()
        {
            var block = new CartesianProductBlock<int, string>();

            block.Left.Complete();
            block.Right.Complete();

            await AssertCompletes(block.Completion);
        }

        private async Task AssertCompletes(Task completion)
        {
            var DefaultTimeout = 2000;

            if (await Task.WhenAny(completion, Task.Delay(DefaultTimeout)) == completion)
            {
                Assert.IsTrue(completion.IsCompleted);
            } else
            {
                Assert.Fail("Timeout waiting for completion");
            }
        }

        private async Task<List<T>> Drain<T>(ISourceBlock<T> source)
        {
            var results = new List<T>();
            while (true)
            {
                var outputAvailable = source.OutputAvailableAsync();
                var result = await Task.WhenAny(outputAvailable, Task.Delay(200));
                if (result == outputAvailable && await outputAvailable)
                {
                    results.Add(source.Receive());
                } else
                {
                    break;
                }
            }
            return results;
        }

    }
}
