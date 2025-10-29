using System;
using System.Collections.Generic;
using UnityEngine;
using Rng = UnityEngine.Random;

public class TimersManager : MonoBehaviour
{
    public enum TimerProcessMode { Idle, Physics }

    private readonly Dictionary<string, Timer> timers = new Dictionary<string, Timer>();

    public Timer CreateTimer(string name, float waitTime = 1f, bool oneShot = true, bool autoStart = false, Action onTimeout = null, TimerProcessMode mode = TimerProcessMode.Idle)
    {
        if (timers.ContainsKey(name))
        {
            Debug.LogWarning($"Timer '{name}' already exists. Returning existing instance.");
            return timers[name];
        }

        Timer timer = new Timer(name, waitTime, oneShot, mode);

        if (autoStart) timer.Start();
        if (onTimeout != null) timer.Timeout += onTimeout;

        timers.Add(name, timer);
        return timer;
    }

    public void CreateTimer(float duration, Action onTimeout, TimerProcessMode mode = TimerProcessMode.Idle)
    {
        int randomNumber = Rng.Range(1, 10000);
        string name = randomNumber.ToString();

        void onTimerTimeout()
        {
            onTimeout?.Invoke();
            timers.Remove(name);
        }

        CreateTimer(name, duration, true, true, onTimerTimeout, mode);
    }

    public void CallLater(float delay, Action callback, TimerProcessMode mode = TimerProcessMode.Idle)
    {
        CreateTimer(delay, callback, mode);
    }

    public Timer GetTimer(string timerName)
    {
        return timers.TryGetValue(timerName, out var result) ? result : null;
    }

    public bool RemoveTimer(string timerName)
    {
        if (timers.ContainsKey(timerName))
        {
            timers.Remove(timerName);
            return true;
        }
        return false;
    }

    public void StartAll()
    {
        foreach (Timer timer in timers.Values)
            timer.Start();
    }

    public void StopAll()
    {
        foreach (Timer timer in timers.Values)
            timer.Stop();
    }

    public void SetPaused(bool paused)
    {
        foreach (Timer timer in timers.Values)
            timer.SetPaused(paused);
    }

    public int GetTimersCount()
    {
        return timers.Count;
    }

    private void Update()
    {
        UpdateTimers(TimerProcessMode.Idle, Time.deltaTime);
    }

    private void FixedUpdate()
    {
        UpdateTimers(TimerProcessMode.Physics, Time.fixedDeltaTime);
    }

    private void UpdateTimers(TimerProcessMode mode, float delta)
    {
        foreach (Timer timer in timers.Values)
            if (timer.ProcessMode == mode)
                timer.Update(delta);
    }

    public IEnumerable<Timer> GetAllTimers() => timers.Values;

    public class Timer
    {
        public event Action Timeout;

        public TimerProcessMode ProcessMode { get; set; }
        public string Name { get; set; }

        public float WaitTime { get; set; }
        public bool OneShot { get; set; }
        public bool Paused { get; private set; }

        public float timeLeft;

        private bool active;
        private float currentWaitTime;

        public float Elapsed => currentWaitTime - timeLeft;
        public float Progress => Mathf.Clamp01(Elapsed / currentWaitTime);

        public void Start(float? timeSec = null)
        {
            float waitTime = timeSec ?? WaitTime;
            timeLeft = waitTime;
            currentWaitTime = waitTime;

            active = true;
        }

        public void Stop()
        {
            active = false;
            timeLeft = WaitTime;
            currentWaitTime = WaitTime;
        }

        public void Restart()
        {
            Stop();
            Start();
        }

        public bool IsStopped()
        {
            return !active;
        }

        public void SetWaitTime(float value)
        {
            WaitTime = value;
        }

        public void SetPaused(bool value)
        {
            Paused = value;
        }

        public void Update(float delta)
        {
            if (!active || Paused) return;

            timeLeft -= delta;

            if (timeLeft <= 0f)
            {
                Timeout?.Invoke();

                if (OneShot)
                    active = false;
                else
                    Start();
            }
        }

        public Timer(string name, float waitTime, bool oneShot, TimerProcessMode processMode)
        {
            Name = name;
            WaitTime = waitTime;
            OneShot = oneShot;
            ProcessMode = processMode;
        }
    }
}
