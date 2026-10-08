using System;
using OverTheHorizon.Timing;
using OverTheHorizon.Player.Input;
class TimingStandaloneTests {
 static int checks;
 static void Check(bool ok,string label) { checks++; if(!ok)throw new Exception(label); }
 static void Throws<T>(Action a,string label) where T:Exception { bool hit=false;try{a();}catch(T){hit=true;}Check(hit,label); }
 static void Main(){
 foreach(int rate in new[]{50,60,120}){var cfg=new SimulationTimingConfig(rate);var p=new SimulationProgress();p.Reset(1);
 for(int i=0;i<10000;i++){var s=p.Begin(cfg);Check(s.EndTime==(i+1)/(double)rate,"clock");Check(p.CompletedOrdinal==(ulong)i,"reservation");p.Complete(s);}
 Check(p.CompletedOrdinal==10000,"complete");}
 var progress=new SimulationProgress();var config=new SimulationTimingConfig(50);progress.Reset(4);var step=progress.Begin(config);
 Throws<InvalidOperationException>(()=>progress.Begin(config),"double begin");
 Throws<InvalidOperationException>(()=>progress.Complete(new SimulationStepContext(5,1,1,config)),"generation");
 Throws<InvalidOperationException>(()=>progress.Complete(new SimulationStepContext(4,1,1,new SimulationTimingConfig(60))),"rate");
 progress.Complete(step);Throws<InvalidOperationException>(()=>progress.Complete(step),"duplicate complete");
 progress.Begin(config);progress.Abort();Check(progress.CompletedTick==1&&progress.ReservedTick==2,"fault isolation");
 Throws<InvalidOperationException>(()=>progress.Begin(config),"fault begin");progress.Reset(5);Check(progress.CompletedTick==0,"reset");
 Throws<ArgumentException>(()=>progress.Begin(default(SimulationTimingConfig)),"default config");
 Check(TickMath.IsDue(1,uint.MaxValue),"wrap due");Check(!TickMath.IsDue(uint.MaxValue,1),"wrap future");Check(TickMath.Elapsed(1,uint.MaxValue)==2,"wrap elapsed");
 Throws<ArgumentOutOfRangeException>(()=>TickMath.Elapsed(0x80000000u,0),"half range");
 Check(TickMath.DurationToTicks((float).02,50)==1,"float boundary");Check(TickMath.DurationToTicks(.0200001,50)==2,"ceil");
 Check(TickMath.DurationToTicks(0,50)==0,"zero duration");Check(TickMath.PeriodToTicks(0,50)==1,"zero period");
 Check(TickMath.NextFutureDue(1,uint.MaxValue,2)==3,"period wrap");Check(TickMath.NextFutureDue(11,2,3)==14,"period phase");
 Throws<ArgumentOutOfRangeException>(()=>TickMath.DurationToTicks(double.NaN,50),"nan");
 Throws<ArgumentOutOfRangeException>(()=>TickMath.DurationToTicks(double.PositiveInfinity,50),"inf");
 Throws<ArgumentOutOfRangeException>(()=>TickMath.DurationToTicks(-1,50),"negative");
 Throws<ArgumentOutOfRangeException>(()=>TickMath.DurationToTicks(0x80000000u,1),"bound");
 var edges=new InputEdgeBuffer(2);edges.Enqueue(1);ulong ack=edges.HighWatermark;edges.Enqueue(2);
 Check(edges.Read(0).Kind==1&&edges.Count==2,"non destructive");Throws<InvalidOperationException>(()=>edges.Enqueue(3),"overflow");
 edges.AckThrough(ack);Check(edges.Count==1&&edges.Read(0).Kind==2,"partial ack");var old=edges.HighWatermark;edges.Clear();edges.Enqueue(3);Check(edges.HighWatermark>old,"sequence survives clear");
 Console.WriteLine("PASS: "+checks+" assertions; Core clock 30,000 steps; tick boundaries, faults, input edges."); }
}
