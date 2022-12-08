﻿using System.Diagnostics;

using Akka.Actor;


namespace ChartApp.Actors
{
  /// <summary>
  /// Actor responsible for monitoring a specific <see cref="PerformanceCounter"/>
  /// </summary>
  public class PerformanceCounterActor : UntypedActor
  {
    private readonly string _seriesName;
    private readonly Func<PerformanceCounter> _performanceCounterGenerator;
    private PerformanceCounter _counter;

    private readonly HashSet<IActorRef> _subscriptions;
    private readonly ICancelable _cancelPublishing;

    public PerformanceCounterActor(string seriesName, Func<PerformanceCounter> performanceCounterGenerator)
    {
      _seriesName = seriesName;
      _performanceCounterGenerator = performanceCounterGenerator;
      _subscriptions = new HashSet<IActorRef>();
      _cancelPublishing = new Cancelable(Context.System.Scheduler);
    }

    #region actor lifecycle methods

    protected override void PreStart()
    {
      _counter = _performanceCounterGenerator();
      Context.System.Scheduler.ScheduleTellRepeatedly(
        TimeSpan.FromMilliseconds(250),
        TimeSpan.FromMilliseconds(250),
        Self,
        new GatherMetrics(),
        Self,
        _cancelPublishing);
    }

    protected override void PostStop()
    {
      try
      {
        _cancelPublishing.Cancel(false);
        _counter.Dispose();
      }
      catch
      {
        // don't care about additional "ObjectDisposed" exception
      }
      finally
      {
        base.PostStop();
      }
    }

    #endregion actor lifecycle methods

    protected override void OnReceive(object message)
    {
      if (message is GatherMetrics)
      {
        var metric = new Metric(_seriesName, _counter.NextValue());
        foreach (var sub in _subscriptions)
        {
          sub.Tell(metric);
        }
      }
      else if (message is SubscribeCounter)
      {
        // add a subscription for this counter
        // (it's parent's job to filter by counter types)
        var sc = message as SubscribeCounter;
        _subscriptions.Add(sc.Subscriber);
      }
      else if (message is UnsubscriberCounter)
      {
        var uc = message as UnsubscriberCounter;
        _subscriptions.Remove(uc.Subscriber);
      }
    }
  }
}