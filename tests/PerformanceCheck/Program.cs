using YolanLive.Core.Helper;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Diagnostics;

void Check(bool value, string message) { if (!value) throw new Exception(message); }
using var server = new LocalServer();
var url = server.Url;
var before = server.Connections;
for (int i=0;i<5;i++) Check(await HttpUtil.GetString(url+"echo", new Dictionary<string,string>{{"Cookie", "session=first"}})=="session=first", "Explicit Cookie header was lost");
Check(server.Connections-before == 1, "Sequential requests did not reuse their connection");
await HttpUtil.GetString(url+"cookie");
Check(await HttpUtil.GetString(url+"echo")=="", "Shared client retained an automatic Cookie");
Check(await HttpUtil.GetString(url+"echo", new Dictionary<string,string>{{"Cookie", "session=second"}})=="session=second", "Headers leaked across requests");
Console.WriteLine("PASS connection reuse: 5 requests / 1 connection; no automatic or cross-request cookies");
using var queuedCancel = new CancellationTokenSource();
var batch = Enumerable.Range(0,10).Select(i => HttpUtil.GetString(url+"hold", cancellationToken:i==4?queuedCancel.Token:CancellationToken.None)).ToArray();
await Task.Delay(200);
Check(server.Holding==4 && RequestMetrics.Active==4, "HTTP concurrency exceeded or did not reach four");
queuedCancel.Cancel();
try { await batch[4]; throw new Exception("Queued request was not canceled"); } catch(OperationCanceledException) { }
Check(server.Holding==4, "Canceled queued request reached the server");
server.Release.TrySetResult(true);
await Task.WhenAll(batch.Where((_,i)=>i!=4));
Check(RequestMetrics.Peak==4 && RequestMetrics.Active==0, "Concurrency slot was leaked");
Console.WriteLine("PASS global concurrency 4, queue cancellation and slot release");
using var cancel = new CancellationTokenSource();
var pending = HttpUtil.GetString(url+"cancel", cancellationToken:cancel.Token);
await server.CancelEntered.Task.WaitAsync(TimeSpan.FromSeconds(2));
var watch=Stopwatch.StartNew(); cancel.Cancel();
try { await pending; throw new Exception("In-flight request was not canceled"); } catch(OperationCanceledException) { }
await server.PeerClosed.Task.WaitAsync(TimeSpan.FromSeconds(2));
Check(RequestMetrics.Active==0, "Cancellation retained a slot");
Console.WriteLine($"PASS physical in-flight cancellation; peer disconnected after {watch.ElapsedMilliseconds} ms");
foreach(var site in new YolanLive.Core.Interface.ILiveSite[]{new YolanLive.Core.BiliBili(),new YolanLive.Core.Douyin(),new YolanLive.Core.Huya(),new YolanLive.Core.Douyu()})
{
 using var alreadyCanceled=new CancellationTokenSource(); alreadyCanceled.Cancel();
 try { await site.GetCategores(alreadyCanceled.Token); throw new Exception("Platform ignored cancellation"); } catch(OperationCanceledException) { }
}
Console.WriteLine("PASS four platform cancellable category entrypoints");

sealed class LocalServer : IDisposable
{
 readonly TcpListener listener=new(IPAddress.Loopback,0);
 readonly CancellationTokenSource cancellation=new();
 int connections, holding;
 public int Connections=>Volatile.Read(ref connections);
 public int Holding=>Volatile.Read(ref holding);
 public TaskCompletionSource<bool> Release=new(TaskCreationOptions.RunContinuationsAsynchronously), CancelEntered=new(TaskCreationOptions.RunContinuationsAsynchronously), PeerClosed=new(TaskCreationOptions.RunContinuationsAsynchronously);
 public string Url {get;}
 public LocalServer() {listener.Start();Url=$"http://127.0.0.1:{((IPEndPoint)listener.LocalEndpoint).Port}/";_ = Accept();}
 async Task Accept()
 {
  try {while(!cancellation.IsCancellationRequested){var client=await listener.AcceptTcpClientAsync(cancellation.Token);Interlocked.Increment(ref connections);_ = Handle(client);}}
  catch(Exception ex) when(ex is OperationCanceledException or ObjectDisposedException or SocketException){}
 }
 async Task Handle(TcpClient client)
 {
  using(client)
  try
  {
   var stream=client.GetStream();
   using var reader=new StreamReader(stream,Encoding.ASCII,false,1024,true);
   while(!cancellation.IsCancellationRequested)
   {
    var first=await reader.ReadLineAsync(cancellation.Token);if(first==null)return;
    var route=first.Split(' ')[1];var cookie="";
    string line;
    while(!string.IsNullOrEmpty(line=await reader.ReadLineAsync(cancellation.Token))) if(line.StartsWith("Cookie:",StringComparison.OrdinalIgnoreCase))cookie=line[7..].Trim();
    if(route=="/hold") {Interlocked.Increment(ref holding);try{await Release.Task.WaitAsync(cancellation.Token);}finally{Interlocked.Decrement(ref holding);}}
    if(route=="/cancel")
    {
     CancelEntered.TrySetResult(true);
     try {if (await stream.ReadAsync(new byte[1],cancellation.Token) != 0) throw new IOException("Expected peer close");}catch(IOException){}
     PeerClosed.TrySetResult(true);return;
    }
    var body=route=="/echo"?cookie:"ok";var bytes=Encoding.UTF8.GetBytes(body);
    var header="HTTP/1.1 200 OK\r\nContent-Type: text/plain; charset=utf-8\r\nContent-Length: "+bytes.Length+"\r\n"+(route=="/cookie"?"Set-Cookie: shared=bad; Path=/\r\n":"")+"Connection: keep-alive\r\n\r\n";
    await stream.WriteAsync(Encoding.ASCII.GetBytes(header),cancellation.Token);await stream.WriteAsync(bytes,cancellation.Token);
   }
  }
  catch(Exception ex) when(ex is IOException or OperationCanceledException or SocketException or ObjectDisposedException){}
 }
 public void Dispose(){cancellation.Cancel();listener.Stop();}
}
