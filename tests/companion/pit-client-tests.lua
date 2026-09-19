local create = assert(loadfile(arg[1]))()
local count = 0
local function check(value, reason) assert(value, reason);count=count+1 end
local function fixture()
  local f={requests={},pending={},executions=0,checks=0,ids=0,ready=true}
  f.plan={protocolVersion=1,planId=string.rep('a',32),carId='car',label='Tune',baselineValues={CAMBER_LF=-30},
    changes={{section='CAMBER_LF',before=-30,after=-25}}}
  f.pit={prepare=function(_,plan,operation)
    f.checks=f.checks+1
    if not f.ready then return nil,'Not in pits' end
    if operation=='restore' and not f.previous then return nil,'No backup' end
    return {operation=operation,planId=operation=='restore' and f.previous or plan.planId}
  end,
  newCommandId=function() f.ids=f.ids+1;return 'command-'..f.ids end,
  execute=function(_,plan,command,ticket)
    f.executions=f.executions+1;f.executedPlan=plan;f.previous=ticket.planId
    return {success=true,state=ticket.operation=='restore' and 'restored' or 'applied',message='Verified'}
  end}
  f.client=create(function(method,url,headers,body,callback)
    local request={method=method,url=url,headers=headers,body=body,callback=callback}
    f.requests[#f.requests+1]=request;f.pending[#f.pending+1]=request
  end,function(x)return x end,function(x)return x end,nil,f.pit)
  f.client.token='paired-token'
  function f:state()
    return {protocolVersion=1,recorder={state='ready',canStart=true,windowId='w',sessionId='s',controlVersion='v'},
      pitSetup={protocolVersion=1,plan=self.plan,controlVersion=string.rep('b',64),canApply=true,busy=false,message='Ready'}}
  end
  function f:answer(body,status)
    local r=table.remove(self.pending,1);assert(r);r.callback(nil,{status=status or 200,body=body});return r
  end
  function f:status(state)
    self.client.nextPoll=0;self.client:update(0.1);self:answer(state or self:state())
  end
  f:status()
  return f,f.client
end
local function authorize(f,override)
  local data={ok=true,commandId=f.pending[1].body.commandId,leaseId=string.rep('c',32),plan=f.plan}
  for k,v in pairs(override or {}) do data[k]=v end
  return f:answer(data)
end
local f,c=fixture()
check(c:canPit('apply') and not c:canPit('restore') and f.executions==0,'status reading mutated or wrong availability')
check(c:pitAction('apply'),'explicit apply failed')
local prepare=f.pending[1]
check(prepare.url=='http://127.0.0.1:5190/api/companion/pit-setup' and prepare.headers['X-ADT-Token']=='paired-token'
  and prepare.body.operation=='apply' and prepare.body.action=='prepare' and prepare.body.controlVersion==string.rep('b',64)
  and prepare.body.planId==f.plan.planId and prepare.body.protocolVersion==1,'prepare did not carry bound authenticated local contract')
check(not c:fresh() and not c:pitAction('apply') and not c:command('start') and f.executions==0,'mutation before lease or double click')
local immutable={protocolVersion=1,planId=f.plan.planId}
authorize(f,{plan=immutable})
check(f.executions==1 and f.executedPlan==immutable and c.pendingPitResult,'cached plan used or result not retained')
local complete=f.pending[1]
check(complete.body.action=='complete' and complete.body.leaseId==string.rep('c',32) and complete.body.commandId==prepare.body.commandId
  and complete.body.state=='applied' and complete.body.success==true,'completion lost immutable lease identity')
prepare.callback(nil,{status=200,body={ok=true,commandId=prepare.body.commandId,leaseId=string.rep('c',32),plan=f.plan}})
check(f.executions==1,'duplicate prepare response repeated mutation')
f:answer({ok=true});check(not c.pendingPitResult and c.pitMessage=='Verified','acknowledged result not cleared')
f:status();check(c:canPit('restore'),'explicit restore unavailable after apply')
check(c:pitAction('restore') and f.pending[1].body.planId==f.plan.planId,'restore lost original plan identity')
authorize(f,{plan=false});check(f.executions==2 and f.pending[1].body.state=='restored','restore not executed and completed')
f:answer({ok=true});f:status()

f,c=fixture();local state=f:state();state.recorder.state='recording';f:status(state)
check(not c:pitAction('apply') and f.executions==0,'recording allowed setup mutation')
state=f:state();state.pitSetup.busy=true;f:status(state);check(not c:pitAction('apply'),'desktop lease busy ignored')
state=f:state();state.pitSetup.canApply=false;f:status(state);check(not c:pitAction('apply'),'server disabled apply ignored')
state=f:state();state.pitSetup.controlVersion='bad';f:status(state);check(not c:pitAction('apply'),'invalid control version accepted')
state=f:state();state.pitSetup=nil;f:status(state);check(not c:pitAction('apply') and c:command('start'),'old desktop fallback broke recording')
f,c=fixture();f.ready=false;check(not c:pitAction('apply') and f.executions==0,'local pit guard ignored')
f,c=fixture();check(c:canPit('apply'),'precondition');f.ready=false
check(not c:pitAction('apply') and f.executions==0 and c.pitMessage=='Not in pits','cached display bypassed fresh click check')

for _,override in ipairs({{ok=false},{commandId='different'},{leaseId='../unsafe'},{leaseId=false}}) do
  f,c=fixture();c:pitAction('apply');authorize(f,override)
  check(f.executions==0 and #f.pending==0,'invalid lease mutated setup')
end
f,c=fixture();c:pitAction('apply');local late=table.remove(f.pending,1)
c:update(8.1)
check(f.executions==0 and not c:fresh() and c.pitMessage:find('timed out'),'prepare timeout mutated or was not explained')
late.callback(nil,{status=200,body={ok=true,commandId=late.body.commandId,leaseId=string.rep('c',32),plan=f.plan}})
check(f.executions==0 and not c.pendingPitResult,'late timed-out lease mutated')
c:update(2.1);check(f.pending[1].method=='GET' and f.executions==0,'prepare timeout automatically retried')

f,c=fixture();c:pitAction('apply');authorize(f);local lost=table.remove(f.pending,1)
c:update(8.1);check(f.executions==1 and c.pendingPitResult and c.pitMessage:find('Sync result'),'lost completion discarded result')
check(c:syncPitResult() and f.pending[1].body==lost.body and f.executions==1,'sync repeated mutation or changed completion identity')
lost.callback(nil,{status=200,body={ok=true}})
check(c.pendingPitResult~=nil,'late completion cleared pending newer request')
f:answer({ok=true});check(c.pendingPitResult==nil and f.executions==1,'completion retry not acknowledged')

f,c=fixture();c:pitAction('apply');authorize(f);f:answer({error='Unavailable'},503)
check(c.pendingPitResult and f.executions==1,'HTTP completion error lost retry receipt')
c:forget();check(c.pendingPitResult and not c:syncPitResult(),'disconnect discarded receipt or sent without pairing')
print('PASS '..count..' pit transport assertions: lease binding, current guards, immutable completion, timeouts and no mutation retries.')
