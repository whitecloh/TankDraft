using System.Net;
using System.Net.Http;
using System.Text;
using Newtonsoft.Json.Linq;
using TankDraft.Match.ServerClient;
using Xunit;
namespace TankDraft.ServerClient.Tests;
public sealed class RemoteQueueClientTests
{
 const string I="aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", C="bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb", L="ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopq", T="cccccccccccccccccccccccccccccccc";
 [Fact] public async Task Lost_join_reuses_operation_then_cancel_resets_it()
 {
  var ops=new List<string>(); var fail=true; using var h=new H(r=>r.RequestUri.AbsolutePath switch {"/readyz"=>J(Ready()),"/v1/lobby"=>J(Lobby()),"/v1/queue/join"=>Join(r,ops,ref fail),"/v1/queue/cancel"=>J(Idle()),_=>new HttpResponseMessage(HttpStatusCode.NotFound)}); var x=Ctx(); using var q=new RemoteQueueClient(x,h);
  await Assert.ThrowsAsync<HttpRequestException>(()=>q.JoinAsync(CancellationToken.None)); await q.JoinAsync(CancellationToken.None); await q.CancelAsync(T,CancellationToken.None); await q.JoinAsync(CancellationToken.None); Assert.Equal(ops[0],ops[1]); Assert.NotEqual(ops[1],ops[2]);
 }
 [Fact] public async Task Forbidden_relogs_with_new_operation_and_idle_leave_clears_binding()
 {
  var ops=new List<string>();var status=0;using var h=new H(r=>r.RequestUri.AbsolutePath switch {"/readyz"=>J(Ready()),"/v1/lobby"=>Login(r,ops),"/v1/queue/status"=>++status==1?new HttpResponseMessage(HttpStatusCode.Forbidden):J(Idle()),"/v1/queue/leave"=>J(Idle()),_=>new HttpResponseMessage(HttpStatusCode.NotFound)});var x=Ctx();x.PendingLeaveMatchId=I+"-"+new string('d',32);x.MatchId=x.PendingLeaveMatchId;using var q=new RemoteQueueClient(x,h);await q.StatusAsync(CancellationToken.None);await q.LeaveAsync(x.PendingLeaveMatchId,CancellationToken.None);Assert.Equal(2,ops.Count);Assert.NotEqual(ops[0],ops[1]);Assert.Null(x.PendingLeaveMatchId);Assert.Null(x.MatchId);
 }
 [Theory][InlineData(true)][InlineData(false)] public async Task Bad_type_or_instance_is_rejected(bool foreign)
 {
  var body=foreign?Queue("Idle","",0,"null","null","null","dddddddddddddddddddddddddddddddd"):Queue("Idle","not-empty",0,"null","null","null",I);using var h=new H(r=>r.RequestUri.AbsolutePath switch {"/readyz"=>J(Ready()),"/v1/lobby"=>J(Lobby()),"/v1/queue/status"=>J(body),_=>new HttpResponseMessage(HttpStatusCode.NotFound)});var x=Ctx();using var q=new RemoteQueueClient(x,h);if(foreign){await Assert.ThrowsAsync<RemoteQueueException>(()=>q.StatusAsync(CancellationToken.None));Assert.Null(x.InstanceId);}else await Assert.ThrowsAsync<InvalidDataException>(()=>q.StatusAsync(CancellationToken.None));
 }
 [Fact] public async Task Deadline_keeps_late_wire_from_mutating_context_and_blocks_second_wire()
 {
  var started=new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);var release=new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);using var h=new SlowH(started,release);var x=Ctx();x.InstanceId=I;x.LobbyToken=L;x.LobbyValidUntil=long.MaxValue;using var q=new RemoteQueueClient(x,h,TimeSpan.FromMilliseconds(40));
  await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>q.StatusAsync(CancellationToken.None));await started.Task.WaitAsync(TimeSpan.FromSeconds(1));Assert.Equal(I,x.InstanceId);Assert.Equal(L,x.LobbyToken);var retry=q.StatusAsync(CancellationToken.None);await Task.Delay(30);Assert.Equal(1,h.Calls);q.Dispose();release.TrySetResult(J(Idle()));await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>retry);
 }
 static HttpResponseMessage Join(HttpRequestMessage r,List<string> o,ref bool fail){o.Add(ServerWire.Parse(r.Content.ReadAsStringAsync().GetAwaiter().GetResult()).Value<string>("OperationId"));if(fail){fail=false;throw new HttpRequestException();}return J(Search());}static HttpResponseMessage Login(HttpRequestMessage r,List<string>o){o.Add(ServerWire.Parse(r.Content.ReadAsStringAsync().GetAwaiter().GetResult()).Value<string>("OperationId"));return J(Lobby());}
 static RemoteSessionContext Ctx()=>new(new Tickets(),new Uri("https://remote.example/"),C,Path.GetTempPath());static string Ready()=>"{\"InstanceId\":\""+I+"\",\"ContentVersion\":\""+C+"\",\"IsDraining\":false,\"EconomyWritesEnabled\":false}";static string Lobby()=>"{\"LobbyToken\":\""+L+"\",\"ExpiresInSeconds\":60}";static string Idle()=>Queue("Idle","",0,"null","null","null",I);static string Search()=>Queue("Searching",T,10,"null","null","null",I);static string Queue(string s,string t,object n,string m,string side,string o,string instance)=>"{\"State\":\""+s+"\",\"TicketId\":\""+t+"\",\"RemainingSeconds\":"+n+",\"MatchId\":"+m+",\"Side\":"+side+",\"OpponentKind\":"+o+",\"InstanceId\":\""+instance+"\"}";static HttpResponseMessage J(string x)=>new(HttpStatusCode.OK){Content=new StringContent(x,Encoding.UTF8,"application/json")};sealed class Tickets:IPlayFabSessionSource{public Task<string> AcquireSessionTicketAsync(CancellationToken t)=>Task.FromResult("ticket-a");}sealed class H(Func<HttpRequestMessage,HttpResponseMessage> f):HttpMessageHandler{protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r,CancellationToken t)=>Task.FromResult(f(r));}sealed class SlowH(TaskCompletionSource<bool>s,TaskCompletionSource<HttpResponseMessage>r):HttpMessageHandler{public int Calls;protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage q,CancellationToken t){Interlocked.Increment(ref Calls);s.TrySetResult(true);return r.Task;}}
}
