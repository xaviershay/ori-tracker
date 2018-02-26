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
    class DataSender : ITargetBlock<TraceEvent>
    {
        private ConcurrentQueue<PendingMessage> pending;

        public DataSender()
        {
            pending = new ConcurrentQueue<PendingMessage>();
        }

        public async Task SendLoopAsync()
        {

            HttpClient client = new HttpClient();
            client.BaseAddress = new Uri("https://us-central1-ori-tracker.cloudfunctions.net/");

            var serializerSettings = new JsonSerializerSettings();
            serializerSettings.ContractResolver = new CamelCasePropertyNamesContractResolver();

            while (true)
            {
                // Build up a list of pending events to send
                // Ordering doesn't matter, since events contain their timestamp
                var toSend = new LinkedList<PendingMessage>();

                // Use current size of queue as upper bound. More elements could be added subsequently;
                // we'll pick them up on the next go around.
                for (var i = 0; i < pending.Count; i++)
                {
                    PendingMessage m;

                    if (pending.TryDequeue(out m))
                    {
                        if (m.Source.ReserveMessage(m.MessageHeader, this))
                        {
                            toSend.AddFirst(m);
                        } else
                        {
                            Debug.Fail("With a single consumer this should never happen");
                        }
                    } else
                    {
                        Debug.Fail("This code is the only one dequeing objects, how could there be fewer items in it?");
                    }
                }

                var path = "/track";
                var json = JsonConvert.SerializeObject(toSend.Select(x => x.Value), serializerSettings);

                var content = new StringContent(json, Encoding.UTF8, "application/json");
                var result = await client.PostAsync(path, content);

                if (result.IsSuccessStatusCode)
                {
                    foreach (var message in toSend)
                    {
                        message.Source.ConsumeMessage(message.MessageHeader, this, out _);
                    }
                }
                else
                {
                    // Put the pending messages back in the queue for retry. The released reservation will be picked up again next time we try to send it.
                    foreach (var message in toSend)
                    {
                        pending.Enqueue(message);
                        message.Source.ReleaseReservation(message.MessageHeader, this);
                    }
                    Console.WriteLine("POST failed: {0} {1}", result.StatusCode, result.ReasonPhrase);
                }
            }
        }

        public Task Completion => throw new NotImplementedException();

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
            pending.Enqueue(new PendingMessage(messageHeader, source, messageValue));
            return DataflowMessageStatus.Postponed;
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
