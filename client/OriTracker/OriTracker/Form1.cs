using Google.Cloud.Firestore;
using LiveSplit.OriDE.Memory;
using Nemiro.OAuth.LoginForms;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;
using System.Windows.Forms;

namespace OriTracker
{
    public partial class Form1 : Form
    {
		private OriMemory Memory { get; set; }
        private FirestoreDb db { get; set; }
        private PointF? lastPos;

        private BufferBlock<TraceEvent> Buffer;
        private Metrics metrics = new Metrics();

        class Metrics : INotifyPropertyChanged
        {
            private int bufferSize;
            public int BufferSize
            {
                get { return bufferSize; }
                set
                {
                    bufferSize = value;
                    InvokePropertyChanged(new PropertyChangedEventArgs("BufferSize"));
                }
            }

            public event PropertyChangedEventHandler PropertyChanged;

            public void InvokePropertyChanged(PropertyChangedEventArgs e)
            {
                PropertyChangedEventHandler handler = PropertyChanged;
                if (handler != null) handler(this, e);
            }
        }

        public Form1()
        {
            InitializeComponent();

            // TODO: Do this off main thread, at very least does FS access. Maybe does network too?
            //db = FirestoreDb.Create("ori-tracker");


            metrics.BufferSize = 3;

            var options = new DataflowBlockOptions();
            options.BoundedCapacity = 200; // TODO: Drop old events rather than block production of new events. Might need custom block type?
            Buffer = new BufferBlock<TraceEvent>(options);

            var consumer = SendToServer(Buffer);
            var publisher = GenerateFakeTrace(Buffer);

            lblBufferSize.DataBindings.Add("Text", metrics, "BufferSize");
            /*
            Memory = new OriMemory();
			Thread t = new Thread(UpdateLoop);
			t.IsBackground = true;
			t.Start();
            */
        }

        async Task GenerateFakeTrace(ITargetBlock<TraceEvent> target)
        {
            var x = 200.0;
            var y = -100.0;
            var xvel = 0.1;
            var yvel = 0.1;

            while (true)
            {
                await Task.Delay(200);
                x = x + xvel;
                y = y + yvel;

                if (x > 300)
                {
                    xvel = -0.1;
                }
                if (x < 100)
                {
                    xvel = 0.1;
                }
                if (y > 0)
                {
                    yvel = -0.1;
                }
                if (y < -200)
                {
                    yvel = 0.1;
                }
                var fakeTrace = new TraceEvent{
                    X = x,
                    Y = y,
                    Timestamp = Now()
                };
                target.Post(fakeTrace);
                metrics.BufferSize = Buffer.Count;
            }
        }

        async Task SendToServer(ISourceBlock<TraceEvent> source)
        {
            HttpClient client = new HttpClient();
            client.BaseAddress = new Uri("https://us-central1-ori-tracker.cloudfunctions.net/");
            //source.ReserveMessage
            while (true)
            {
                await source.OutputAvailableAsync();
                IList<TraceEvent> traces = new List<TraceEvent>();
                Buffer.TryReceiveAll(out traces);

                /*
                var trace = await source.ReceiveAsync();
                var traceID = string.Format("{0:0.}", trace.Timestamp);
                var path = $"/track?board_id=abc123&player_id=xavier&x={trace.X}&y={trace.Y}&timestamp={traceID}";
                Console.WriteLine(path);
                await Task.Delay(500);
                */
                var serializerSettings = new JsonSerializerSettings();
                serializerSettings.ContractResolver = new CamelCasePropertyNamesContractResolver();
                var path = "/track";
                var json = JsonConvert.SerializeObject(traces, serializerSettings);
                Console.WriteLine(json);
                var content = new StringContent(json, Encoding.UTF8, "application/json");
                var result = await client.PostAsync(path, content);
                if (result.IsSuccessStatusCode)
                {

                }
                else
                {
                    Console.WriteLine("POST failed: {0} {1}", result.StatusCode, result.ReasonPhrase);
                    // Put the messages back in the buffer
                    foreach (var trace in traces)
                    {
                        Buffer.Post(trace);
                    }
                }
                // TODO: Check result was success
            }

        }
        private void UpdateLoop() {
            bool lastHooked = false;
            while (true) {
                bool hooked = false;
                try {
                    hooked = Memory.HookProcess();
                } catch { }

                if (hooked) {
                    UpdateValues();
                }
                if (lastHooked != hooked) {
                    lastHooked = hooked;
                }
                Thread.Sleep(200);
            }
        }

        public double Now()
        {
            TimeSpan t = DateTime.UtcNow - new DateTime(1970, 1, 1);
            return t.TotalMilliseconds;
        }
        public void UpdateValues()
        {
            PointF pos = Memory.GetCameraTargetPosition();
            if (!lastPos.HasValue || lastPos.Value != pos)
            {
                var trace = new TraceEvent
                {
                    X = pos.X,
                    Y = pos.Y,
                    Timestamp = Now()
                };

                Console.WriteLine(Buffer.Post(trace));
            }
            lastPos = pos;
        }
    }
}
