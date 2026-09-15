using System.Collections.Immutable;
using Microsoft.Data.Sqlite;
using TankDraft.Server.Progression;
using Xunit;

namespace TankDraft.Server.Progression.Tests;
public sealed class ProgressionTests : IDisposable
{
    readonly string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".db");
    [Fact] public void UnitProgressRejectsNegative() => Assert.Throws<ArgumentOutOfRangeException>(() => new UnitProgress(0, 0, 0));
    [Fact] public void StateRejectsWrongSchema() => Assert.Throws<ArgumentException>(() => new ProgressionState(2, 0, "x", ImmutableDictionary<string, UnitProgress>.Empty, 0, ImmutableHashSet<string>.Empty));
    [Fact] public void RulesRejectEmptyUnits() => Assert.Throws<ArgumentException>(() => new ProgressionRules("v", [], [], [], 0, 0, 0, 0, [], [], []));
    [Fact] public async Task StoreUsesCas() { using var s = new SqliteProgressionStore(path); await s.WriteAsync("acct1", ProgressionState.Empty, 0, default); await Assert.ThrowsAsync<ProgressionConflictException>(() => s.WriteAsync("acct1", ProgressionState.Empty, 0, default)); }
    [Fact] public async Task StaleCasCannotOverwriteAdvancedSequence()
    {
        using var store = new SqliteProgressionStore(path);
        var first = ProgressionState.Empty with { AppliedSequence = 1, AppliedFingerprint = "first" };
        var advanced = first with { AppliedSequence = 2, AppliedFingerprint = "second" };
        await store.WriteAsync("acct1", first, 0, default);
        await store.WriteAsync("acct1", advanced, 1, default);
        await Assert.ThrowsAsync<ProgressionConflictException>(() => store.WriteAsync("acct1", first, 1, default));
        Assert.Equal(2, (await store.ReadAsync("acct1", default)).State!.AppliedSequence);
    }
    [Fact] public async Task UpgradeDebitsAndReplays() { using var s = new SqliteProgressionStore(path); var w = new Wallet(); var svc = new ProgressionService(s, w, Rules()); var owned = ImmutableHashSet.Create("unit1"); await s.WriteAsync("acct1", ProgressionState.Empty with { Units = ImmutableDictionary<string,UnitProgress>.Empty.Add("unit1", new(1, 3, 0)) }, 0, default); var id = Guid.NewGuid(); var a = await svc.ExecuteAsync("acct1", id, new(ProgressionRequestKind.UpgradeUnit,"unit1"), 0, owned, 1, default); var b = await svc.ExecuteAsync("acct1", id, new(ProgressionRequestKind.UpgradeUnit,"unit1"), 0, owned, 1, default); Assert.Equal(2,a.State.Units["unit1"].Level); Assert.Equal(a.Sequence,b.Sequence); Assert.Equal(1,w.Debits); }
    [Fact] public async Task ChangedReplayConflicts() { using var s = new SqliteProgressionStore(path); var svc = new ProgressionService(s,new Wallet(),Rules()); var owned=ImmutableHashSet.Create("unit1"); await s.WriteAsync("acct1",ProgressionState.Empty with { Units=ImmutableDictionary<string,UnitProgress>.Empty.Add("unit1",new(1,3,0))},0,default); var id=Guid.NewGuid(); await svc.ExecuteAsync("acct1",id,new(ProgressionRequestKind.UpgradeUnit,"unit1"),0,owned,1,default); await Assert.ThrowsAsync<InvalidOperationException>(()=>svc.ExecuteAsync("acct1",id,new(ProgressionRequestKind.BoostMastery,"unit1"),0,owned,1,default)); }
    [Fact] public async Task BattleOnlyChangesParticipants() { using var s=new SqliteProgressionStore(path); var svc=new ProgressionService(s,new Wallet(),Rules()); var r=await svc.ApplyBattleAsync("acct1",new string('a',64),ImmutableArray.Create("unit1","unit2","unit3","unit4"),true,default); Assert.Equal(5,r.State.Units["unit1"].MasteryXp); Assert.False(r.State.Units.ContainsKey("unit5")); }
    [Fact] public async Task PackHasDistinctRecipients() { using var s=new SqliteProgressionStore(path); var svc=new ProgressionService(s,new Wallet(),Rules()); var r=await svc.ExecuteAsync("acct1",Guid.NewGuid(),new(ProgressionRequestKind.BuyOffer,"pack"),0,ImmutableHashSet.Create("unit1","unit2"),1,default); Assert.Equal(2,r.ResolvedUnitIds.Distinct().Count()); }
    [Fact] public async Task LostProfileWriteResponseDoesNotDuplicateBits() { using var journal=new SqliteProgressionStore(path); var profile=new LostWriteStore(journal); var svc=new ProgressionService(journal,new Wallet(),Rules(),profile); await journal.WriteAsync("acct1",ProgressionState.Empty,0,default); var id=Guid.NewGuid(); var receipt=await svc.ExecuteAsync("acct1",id,new(ProgressionRequestKind.BuyOffer,"pack"),0,ImmutableHashSet.Create("unit1","unit2"),1,default); var replay=await svc.ExecuteAsync("acct1",id,new(ProgressionRequestKind.BuyOffer,"pack"),0,ImmutableHashSet.Create("unit1","unit2"),1,default); Assert.Equal(ProgressionOperationStatus.Completed,receipt.Status); Assert.Equal(receipt.Sequence,replay.Sequence); Assert.Equal(1,profile.Writes); }
    [Fact] public async Task DebitLostResponsePreservesEntitlementAndQuarantines() { using var s=new SqliteProgressionStore(path); var w=new LostDebitWallet(); var svc=new ProgressionService(s,w,Rules()); var r=await svc.ExecuteAsync("acct1",Guid.NewGuid(),new(ProgressionRequestKind.BuyOffer,"pack"),0,ImmutableHashSet.Create("unit1","unit2"),1,default); Assert.Equal(ProgressionOperationStatus.NeedsReview,r.Status); Assert.Equal(1,w.Debits); Assert.Equal(1,(await svc.GetAsync("acct1",default)).AppliedSequence); await svc.RecoverAsync(default); Assert.Equal(1,w.Debits); }
    [Fact] public async Task InsufficientCoinsDoesNotPrepareOrMutate() { using var s=new SqliteProgressionStore(path); var svc=new ProgressionService(s,new EmptyWallet(),Rules()); await Assert.ThrowsAsync<InvalidOperationException>(()=>svc.ExecuteAsync("acct1",Guid.NewGuid(),new(ProgressionRequestKind.BuyOffer,"pack"),0,ImmutableHashSet.Create("unit1","unit2"),1,default)); Assert.Equal(0,(await svc.GetAsync("acct1",default)).AppliedSequence); }
    [Fact] public async Task PendingOperationBlocksNewPaidOperation() { using var s=new SqliteProgressionStore(path); var svc=new ProgressionService(s,new LostDebitWallet(),Rules()); await svc.ExecuteAsync("acct1",Guid.NewGuid(),new(ProgressionRequestKind.BuyOffer,"pack"),0,ImmutableHashSet.Create("unit1","unit2"),1,default); await Assert.ThrowsAsync<InvalidOperationException>(()=>svc.ExecuteAsync("acct1",Guid.NewGuid(),new(ProgressionRequestKind.BuyOffer,"pack"),1,ImmutableHashSet.Create("unit1","unit2"),1,default)); }
    [Fact] public async Task RecoveryAfterUnappliedCasWritesOnceAndDebitsOnce() { using var journal=new SqliteProgressionStore(path); var profile=new BeforeWriteStore(journal); var wallet=new Wallet(); var svc=new ProgressionService(journal,wallet,Rules(),profile); await Assert.ThrowsAsync<IOException>(()=>svc.ExecuteAsync("acct1",Guid.NewGuid(),new(ProgressionRequestKind.BuyOffer,"pack"),0,ImmutableHashSet.Create("unit1","unit2"),1,default)); profile.Enabled=false; await svc.RecoverAsync(default); Assert.Equal(1,(await profile.ReadAsync("acct1",default)).State!.AppliedSequence); Assert.Equal(1,wallet.Debits); }
    [Fact] public async Task ReopenQuarantinesPersistedWalletSendingWithoutRetry()
    {
        var operation = Guid.NewGuid();
        using (var store = new SqliteProgressionStore(path))
        {
            var wallet = new Wallet();
            var service = new ProgressionService(store, wallet, Rules());
            await service.ExecuteAsync("acct1", operation, new(ProgressionRequestKind.BuyOffer, "pack"), 0, ImmutableHashSet.Create("unit1", "unit2"), 1, default);
            Assert.Equal(1, wallet.Debits);
        }
        using (var connection = new SqliteConnection("Data Source=" + path + ";Pooling=False"))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "UPDATE Operations SET Status=2 WHERE Account='acct1' AND OperationId=$id";
            command.Parameters.AddWithValue("$id", operation.ToString("N"));
            Assert.Equal(1, command.ExecuteNonQuery());
        }
        using var reopened = new SqliteProgressionStore(path);
        var retryWallet = new Wallet();
        var recovered = new ProgressionService(reopened, retryWallet, Rules());
        await recovered.RecoverAsync(default);
        var replay = await recovered.ExecuteAsync("acct1", operation, new(ProgressionRequestKind.BuyOffer, "pack"), 0, ImmutableHashSet.Create("unit1", "unit2"), 1, default);
        Assert.Equal(ProgressionOperationStatus.NeedsReview, replay.Status);
        Assert.Equal(0, retryWallet.Debits);
    }
    [Fact] public async Task ForgivingUncertainDebitCompletesReviewAndUnblocksAccount()
    {
        using var store = new SqliteProgressionStore(path); var wallet = new LostDebitWallet(); var service = new ProgressionService(store, wallet, Rules()); var operation = Guid.NewGuid();
        await service.ExecuteAsync("acct1", operation, new(ProgressionRequestKind.BuyOffer, "pack"), 0, ImmutableHashSet.Create("unit1", "unit2"), 1, default);
        var review = Assert.Single(service.ReadReviews()); Assert.True(review.WalletDelta > 0); Assert.False(review.CompensationAttempted);
        var resolved = await service.ResolveReviewAsync("acct1", operation, ProgressionReviewAction.ForgiveDebit, "legacy-call-unproven", default);
        Assert.Equal(ProgressionOperationStatus.Completed, resolved.Status); Assert.Empty(service.ReadReviews());
        wallet.Fail = false;
        Assert.Equal(ProgressionOperationStatus.Completed, (await service.ExecuteAsync("acct1", Guid.NewGuid(), new(ProgressionRequestKind.BuyOffer, "pack"), 1, ImmutableHashSet.Create("unit1", "unit2"), 1, default)).Status);
    }
    [Fact] public async Task LostCreditCompensationIsAuditedOnceAndSurvivesReopen()
    {
        var operation = Guid.NewGuid();
        using (var store = new SqliteProgressionStore(path))
        {
            var wallet = new LostCreditWallet(); var service = new ProgressionService(store, wallet, CoinRules());
            await store.WriteAsync("acct1", ProgressionState.Empty with { Units = ImmutableDictionary<string, UnitProgress>.Empty.Add("unit1", new UnitProgress(1, 0, 1)) }, 0, default);
            var claim = await service.ExecuteAsync("acct1", operation, new(ProgressionRequestKind.ClaimMilestone, "unit1:1"), 0, ImmutableHashSet.Create("unit1"), 1, default);
            Assert.Equal(ProgressionOperationStatus.NeedsReview, claim.Status); Assert.Equal(1, wallet.Credits);
            var compensation = await service.ResolveReviewAsync("acct1", operation, ProgressionReviewAction.CompensateCreditOnce, "legacy-credit-unproven", default);
            Assert.Equal(ProgressionOperationStatus.NeedsReview, compensation.Status); Assert.Equal(2, wallet.Credits);
            Assert.True(Assert.Single(service.ReadReviews()).CompensationAttempted);
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.ResolveReviewAsync("acct1", operation, ProgressionReviewAction.CompensateCreditOnce, "second-attempt", default));
        }
        using var reopened = new SqliteProgressionStore(path);
        var recovered = new ProgressionService(reopened, new Wallet(), CoinRules());
        Assert.True(Assert.Single(recovered.ReadReviews()).CompensationAttempted);
        await Assert.ThrowsAsync<InvalidOperationException>(() => recovered.ResolveReviewAsync("acct1", operation, ProgressionReviewAction.CompensateCreditOnce, "after-reopen", default));
    }
    [Fact] public async Task RequestCancellationAfterDurableWalletStageDoesNotCancelWalletCall()
    {
        using var store = new SqliteProgressionStore(path); using var caller = new CancellationTokenSource(); var wallet = new CancellingWallet(caller); var service = new ProgressionService(store, wallet, Rules());
        var receipt = await service.ExecuteAsync("acct1", Guid.NewGuid(), new(ProgressionRequestKind.BuyOffer, "pack"), 0, ImmutableHashSet.Create("unit1", "unit2"), 1, caller.Token);
        Assert.True(caller.IsCancellationRequested); Assert.Equal(ProgressionOperationStatus.Completed, receipt.Status); Assert.Equal(1, wallet.Debits);
    }
    static ProgressionRules Rules()=>new("v",[new UnitDefinition("unit1","r",1),new UnitDefinition("unit2","r",1),new UnitDefinition("unit3","r",1),new UnitDefinition("unit4","r",1)],[new LevelStep(1,3,2,1,1)],[],5,1,1,2,[],[new OfferDefinition("pack","GM",1,"unit1",1,"p")],[new PackDefinition("p",2,[new PackRarity("r",1,1,1)])]);
    static ProgressionRules CoinRules()=>new("coins",[new UnitDefinition("unit1","r",1),new UnitDefinition("unit2","r",1),new UnitDefinition("unit3","r",1),new UnitDefinition("unit4","r",1)],[new LevelStep(1,3,2,1,1)],[new MasteryStep(1,1,MasteryRewardKind.Coins,7)],5,1,1,2,[],[new OfferDefinition("pack","GM",1,"unit1",1,"p")],[new PackDefinition("p",2,[new PackRarity("r",1,1,1)])]);
    public void Dispose(){foreach(var x in new[]{path,path+".writer.lock",path+"-wal",path+"-shm"})if(File.Exists(x))File.Delete(x);}
    class Wallet:IProgressionWallet { public int Debits; public int Credits; public Task<int> ReadAsync(string a,string c,CancellationToken t)=>Task.FromResult(100); public virtual Task DebitAsync(string a,string c,int n,CancellationToken t){Debits++;return Task.CompletedTask;} public virtual Task CreditAsync(string a,string c,int n,CancellationToken t){Credits++;return Task.CompletedTask;} }
    sealed class EmptyWallet:IProgressionWallet { public Task<int> ReadAsync(string a,string c,CancellationToken t)=>Task.FromResult(0); public Task DebitAsync(string a,string c,int n,CancellationToken t)=>Task.CompletedTask; public Task CreditAsync(string a,string c,int n,CancellationToken t)=>Task.CompletedTask; }
    sealed class LostDebitWallet:Wallet { public bool Fail = true; public override Task DebitAsync(string a,string c,int n,CancellationToken t){Debits++; return Fail ? Task.FromException(new IOException("lost")) : Task.CompletedTask;} }
    sealed class LostCreditWallet:Wallet { public override Task CreditAsync(string a,string c,int n,CancellationToken t){Credits++; return Task.FromException(new IOException("lost"));} }
    sealed class CancellingWallet(CancellationTokenSource caller):Wallet { public override Task DebitAsync(string a,string c,int n,CancellationToken t){Debits++; caller.Cancel(); Assert.False(t.IsCancellationRequested); return Task.CompletedTask;} }
    sealed class LostWriteStore:IProgressionStore { readonly IProgressionStore inner; public int Writes; public LostWriteStore(IProgressionStore inner){this.inner=inner;} public Task<StoredProgression> ReadAsync(string a,CancellationToken t)=>inner.ReadAsync(a,t); public async Task WriteAsync(string a,ProgressionState s,int v,CancellationToken t){Writes++;await inner.WriteAsync(a,s,v,t);throw new IOException("lost");} }
    sealed class BeforeWriteStore:IProgressionStore { readonly IProgressionStore inner; public bool Enabled=true; public BeforeWriteStore(IProgressionStore inner){this.inner=inner;} public Task<StoredProgression> ReadAsync(string a,CancellationToken t)=>inner.ReadAsync(a,t); public Task WriteAsync(string a,ProgressionState s,int v,CancellationToken t)=>Enabled?Task.FromException(new IOException("before")):inner.WriteAsync(a,s,v,t); }
}
