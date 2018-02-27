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
        private OriMemory Memory { get; set; } = new OriMemory();
        private FirestoreDb db { get; set; }
        private PointF? lastPos;

        private Metrics metrics = new Metrics();
        private DataSender httpSender = new DataSender();

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

            //var publisher = GenerateFakeTrace(consumer);

            lblBufferSize.DataBindings.Add("Text", metrics, "BufferSize");

            var oriTask = MonitorOriAsync();
            /*
			Thread t = new Thread(UpdateLoop);
			t.IsBackground = true;
			t.Start();
            */
        }

        async Task GenerateFakeTrace(ITargetBlock<TraceEvent> target)
        {
            var x = 200.0;
            var y = -100.0;
            var xvel = 1.0;
            var yvel = 1.0;

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
            }
        }

        async Task MonitorOriAsync() {
            bool lastHooked = false;
            while (true) {
                await Task.Run(() =>
                {
                    bool hooked = false;
                    try
                    {
                        hooked = Memory.HookProcess();
                    }
                    catch { }

                    if (hooked)
                    {
                        UpdateValues();
                    }
                    if (lastHooked != hooked)
                    {
                        lastHooked = hooked;
                    }
                });
                await Task.Delay(200);
            }
        }

        public double Now()
        {
            TimeSpan t = DateTime.UtcNow - new DateTime(1970, 1, 1);
            return t.TotalMilliseconds;
        }

        public bool CheckInGame(GameState state)
        {
            return state != GameState.Logos && state != GameState.StartScreen && state != GameState.TitleScreen;
        }
        public bool CheckInGameWorld(GameState state)
        {
            return CheckInGame(state) && state != GameState.Prologue && !Memory.IsEnteringGame();
        }

        public void UpdateValues()
        {
            var state = Memory.GetGameState();
            if (CheckInGameWorld(state))
            {
                PointF pos = Memory.GetCameraTargetPosition();

                // Only send events if position has changed since last time
                if (!lastPos.HasValue || lastPos.Value != pos)
                {
                    var trace = new TraceEvent
                    {
                        X = pos.X,
                        Y = pos.Y,
                        Start = !lastPos.HasValue,
                        Timestamp = Now()
                    };

                    httpSender.Post(trace);
                }
                lastPos = pos;
            } else
            {
                // Reset last position when exited to menu
                lastPos = null;
            }
        }
    }
}
