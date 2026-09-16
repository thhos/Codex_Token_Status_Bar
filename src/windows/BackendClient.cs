using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace CodexPetCredits {
    // Own the child process and ordered pipe writes outside the WPF dispatcher.
    internal sealed class BackendClient : IDisposable {
        private readonly BlockingCollection<string> outbound=new BlockingCollection<string>();
        private readonly JavaScriptSerializer reader=new JavaScriptSerializer{MaxJsonLength=16*1024*1024};
        private Process process;
        private Task writer;
        private volatile bool disposed;
        public event Action<object> ViewReceived,PetReceived,SettingsReceived;
        public event Action<string> Failed;
        public void Start(string root){
            string node=Environment.GetEnvironmentVariable("CODEX_STATUS_NODE")??"node.exe";
            var start=new ProcessStartInfo(node,"\""+Path.Combine(root,"src","backend","main.mjs")+"\" --port 9337"){
                WorkingDirectory=root,UseShellExecute=false,CreateNoWindow=true,
                RedirectStandardInput=true,RedirectStandardOutput=true,RedirectStandardError=true,
                StandardOutputEncoding=System.Text.Encoding.UTF8,StandardErrorEncoding=System.Text.Encoding.UTF8
            };
            process=new Process{StartInfo=start,EnableRaisingEvents=true};
            process.OutputDataReceived+=Receive;
            process.ErrorDataReceived+=delegate{};
            process.Exited+=delegate{ReportFailure("数据进程已退出");};
            try{
                process.Start();process.BeginOutputReadLine();process.BeginErrorReadLine();
                writer=Task.Run((Action)WriteMessages);
            }catch{ReportFailure("数据进程启动失败");}
        }
        private void Receive(object sender,DataReceivedEventArgs args){
            if(disposed || String.IsNullOrEmpty(args.Data))return;
            try{
                var message=reader.DeserializeObject(args.Data);string type=Json.Text(message,"type");
                var handler=type=="view"?ViewReceived:type=="pet"?PetReceived:type=="settings"?SettingsReceived:null;
                if(handler!=null)handler(message);
            }catch{ /* Incomplete or unrelated stdout lines must not stop subsequent samples. */ }
        }
        public void Send(object message){
            if(disposed)return;
            // Called on the UI thread; serialization is short, pipe backpressure is handled by the writer.
            try{outbound.Add(Json.Serializer.Serialize(message));}catch(InvalidOperationException){}
        }
        private void WriteMessages(){
            try{foreach(string line in outbound.GetConsumingEnumerable()){process.StandardInput.WriteLine(line);process.StandardInput.Flush();}}
            catch{ReportFailure("数据连接已中断");}
            finally{try{process.StandardInput.Close();}catch{}}
        }
        private void ReportFailure(string message){var handler=Failed;if(!disposed && handler!=null)handler(message);}
        public void Dispose(){
            if(disposed)return;
            // Preserve pending settings and shutdown ordering before closing stdin.
            Send(new System.Collections.Generic.Dictionary<string,object>{{"type","shutdown"}});
            disposed=true;outbound.CompleteAdding();
            // Closing the application terminates pool threads. Give the final settings a bounded drain.
            if(writer!=null)try{writer.Wait(300);}catch(AggregateException){}
            var child=process;
            if(child!=null)Task.Run(async delegate{
                await Task.Delay(3000);
                try{if(!child.HasExited)child.Kill();}catch{}
                child.Dispose();
            });
        }
    }
}
