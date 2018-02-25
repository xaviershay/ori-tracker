using Google.Cloud.Firestore;
using LiveSplit.OriDE.Memory;
using Nemiro.OAuth.LoginForms;
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
using System.Windows.Forms;

namespace OriTracker
{
    [FirestoreData]
    public class TraceEvent
    {
        [FirestoreProperty]
        public float X { get; set; }

        [FirestoreProperty]
        public float Y { get; set; }
    }

    public partial class Form1 : Form
    {
		private OriMemory Memory { get; set; }
        private FirestoreDb db { get; set; }
        private PointF? lastPos;

        static HttpClient client = new HttpClient();

        public Form1()
        {
            InitializeComponent();

            // TODO: Do this off main thread, at very least does FS access. Maybe does network too?
            //db = FirestoreDb.Create("ori-tracker");

            client.BaseAddress = new Uri("https://us-central1-ori-tracker.cloudfunctions.net/");

            Memory = new OriMemory();
			Thread t = new Thread(UpdateLoop);
			t.IsBackground = true;
			t.Start();
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
        public void UpdateValues()
        {
            TimeSpan t = DateTime.UtcNow - new DateTime(1970, 1, 1);
            var now = t.TotalMilliseconds;
            PointF pos = Memory.GetCameraTargetPosition();
            if (!lastPos.HasValue || lastPos.Value != pos)
            {
                var trace = new TraceEvent
                {
                    X = pos.X,
                    Y = pos.Y
                };
                var traceID = now.ToString();
                //var docRef = db.Document("boards/abc123/players/xavier/traces/" + traceID);

                // TODO: Presumably firestore keeps retrying on network failure? Check this.
                //docRef.SetAsync(trace);
                var path = $"/track?board_id=abc123&player_id=xavier&x={pos.X}&y={pos.Y}&timestamp={string.Format("{0:0.}", now)}";
                Console.WriteLine(path);
                client.GetAsync(path);
            }
            lastPos = pos;
        }
    }
}
