using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;
using System.Windows.Forms;

namespace OriTracker
{
    public class DataSenderOptions
    {
        // Number of milliseconds to delay before beginning to send a batch. This is in addition to
        // the round-trip time of the HTTP request.
        public int BatchDelay { get; internal set; }

        // Approximate maximum number of events that will be accepted for processing at a time
        public int BoundedCapacity { get; internal set; }

        // URI to send requests to
        public Uri BaseAddress { get; internal set; }
    }

    class DataSender : ITargetBlock<TraceEvent>
    {
        public static DataSenderOptions DefaultOptions = new DataSenderOptions {
            BaseAddress = new Uri("https://us-central1-ori-tracker.cloudfunctions.net/"),
            BatchDelay = 1000,
            BoundedCapacity = 200
        };

        public Task Completion => throw new NotImplementedException();
        public DataSenderOptions Options { get; } = DefaultOptions;

        private ConcurrentQueue<TraceEvent> pending = new ConcurrentQueue<TraceEvent>();
        private HttpClient client = new HttpClient();
        private Task httpTask;

        public DataSender() : this(DefaultOptions)
        {
        }

        public DataSender(DataSenderOptions options)
        {
            Options = options;
            client.BaseAddress = Options.BaseAddress;
            httpTask = SendLoopAsync();
        }

        private async Task SendLoopAsync()
        {

            var serializerSettings = new JsonSerializerSettings();
            serializerSettings.ContractResolver = new CamelCasePropertyNamesContractResolver();

            while (true)
            {
                await Task.Delay(Options.BatchDelay);
                var pendingCount = pending.Count;
                if (pendingCount == 0)
                {
                    continue;
                }

                // Build up a list of pending events to send
                // Ordering doesn't matter, since events contain their timestamp
                var toSend = new LinkedList<TraceEvent>();

                // Use current size of queue as upper bound. More elements could be added subsequently;
                // we'll pick them up on the next go around.
                for (var i = 0; i < pendingCount; i++)
                {
                    TraceEvent m;

                    if (pending.TryDequeue(out m))
                    {
                        toSend.AddFirst(m);
                    } else
                    {
                        Debug.Fail("This code is the only one dequeing objects, how could there be fewer items in it?");
                    }
                }

                var path = "/track?board_id=abc123&player_id=xavier";
                var json = JsonConvert.SerializeObject(toSend, serializerSettings);

                var content = new StringContent(json, Encoding.UTF8, "application/json");
                var result = await client.PostAsync(path, content);

                if (result.IsSuccessStatusCode)
                {
                    Console.WriteLine("Successfully sent {0} events", toSend.Count);
                }
                else
                {
                    Console.WriteLine("POST failed for {2} events: {0} {1}", result.StatusCode, result.ReasonPhrase, pending.Count);
                    // Put the pending messages back in the queue for retry. The released reservation will be picked up again next time we try to send it.
                    foreach (var message in toSend)
                    {
                        pending.Enqueue(message);
                    }
                }
            }
        }

        public void Complete()
        {
            throw new NotImplementedException();
        }

        public void Fault(Exception exception)
        {
            throw new NotImplementedException();
        }

        public DataflowMessageStatus OfferMessage(DataflowMessageHeader messageHeader, TraceEvent messageValue, ISourceBlock<TraceEvent> source, bool consumeToAccept)
        {
            // This check isn't thread safe but we don't care because the bound is more of a suggestion
            if (pending.Count > Options.BoundedCapacity)
            {
                return DataflowMessageStatus.Declined;
            }
            else
            {
                pending.Enqueue(messageValue);
                return DataflowMessageStatus.Accepted;
            }
        }

        private class PendingMessage
        {
            public DataflowMessageHeader MessageHeader;
            public ISourceBlock<TraceEvent> Source;
            public TraceEvent Value;

            public PendingMessage(DataflowMessageHeader messageHeader, ISourceBlock<TraceEvent> source, TraceEvent value)
            {
                this.MessageHeader = messageHeader;
                this.Source = source;
                this.Value = value;
            }
        }
    }
}
