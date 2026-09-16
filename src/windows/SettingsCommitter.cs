using System;
using System.Collections.Generic;
using System.Windows.Threading;

namespace CodexPetCredits {
    // Preview slider values locally; persist only the newest value after a short quiet period.
    public sealed class SettingsCommitter : IDisposable {
        private readonly Dictionary<string,object> pending=new Dictionary<string,object>();
        private readonly DispatcherTimer timer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(200)};
        private readonly Action<Dictionary<string,object>> commit;
        public SettingsCommitter(Action<Dictionary<string,object>> commit){this.commit=commit;timer.Tick+=OnTick;}
        public void Set(string key,object value){pending[key]=value;timer.Stop();timer.Start();}
        private void OnTick(object sender,EventArgs args){Flush();}
        public void Flush(){
            timer.Stop();if(pending.Count==0)return;
            var message=new Dictionary<string,object>(pending){{"type","settings"}};pending.Clear();commit(message);
        }
        public void Dispose(){Flush();timer.Tick-=OnTick;}
    }
}
