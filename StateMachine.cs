using Godot;
using System;
using System.Linq;
using System.Collections.Generic;

public class StateMachine<T> where T : Enum
{
   /* --------------------------------------------------------
   *  TS & Events;
   * -------------------------------------------------------- */
   public enum ProcessType { PhysicsProcess, Process }

   /* How to use -> OnStateChanged(int from, int to) {
      (States)from = from;
      (States)to = to;
   }*/
   public event Action<T, T> StateChanged;
   public event Action<T, T> TransitionTriggered;

   /* --------------------------------------------------------
   *  FIELDS
   * -------------------------------------------------------- */
   private Dictionary<T, State> states = new();
   private Dictionary<T, List<Transition>> transitions = new();
   private List<Transition> globalTransitions = new();

   private State currentState;
   private T currentId;
   private T previousId;
   private T initialId;

   private bool hasInitialId;
   private bool paused;
   private float stateTime;

   /* --------------------------------------------------------
   *  STATE MANAGEMENT
   * -------------------------------------------------------- */
   public State AddState(T id, Action<double> update = null, Action enter = null, Action exit = null, float minTime = default, float timeout = -1, ProcessType processType = default)
   {
      if (states.ContainsKey(id))
         return null;

      State state = new State(id, update, enter, exit, minTime, timeout, processType);

      states[id] = state;

      if (!hasInitialId)
      {
         initialId = id;
         hasInitialId = true;
         ChangeStateInternal(id, ignoreExit: true);
      }

      state.SetRestartId(initialId);
      return state;
   }

   public void RemoveState(T id)
   {
      if (!states.ContainsKey(id))
         return;

      states.Remove(id);

      if (initialId.Equals(id))
         hasInitialId = false;

      if (currentId.Equals(id))
            Reset();

      foreach (var key in transitions.Keys.ToList())
         transitions[key] = transitions[key].Where(t => !t.To.Equals(id) && !t.From.Equals(id)).ToList();

      globalTransitions = globalTransitions.Where(t => !t.To.Equals(id)).ToList();
   }

   public void Reset()
   {
      if (states.Count == 0)
         return;

      if (!hasInitialId)
         SetInitialId(states.Values.First().Id);

      ChangeStateInternal(initialId);
      previousId = default;
   }

   public void SetInitialId(T id)
   {
      initialId = id;
      hasInitialId = true;
   }

   public void RestartCurrentState(bool ignoreExit = false, bool ignoreEnter = false)
   {
      ResetStateTime();

      if (currentState == null)
         return;

      if (!ignoreExit && !currentState.IsLocked()) currentState.Exit?.Invoke();
      if (!ignoreEnter) currentState.Enter?.Invoke();
   }

   public State GetState(T id)
   {
      states.TryGetValue(id, out var state);
      return state;
   }

   /* --------------------------------------------------------
   *  STATE CHANGING
   * -------------------------------------------------------- */
   public void TryChangeState(T id, bool condition)
   {
      if (condition && states.ContainsKey(id))
         ChangeStateInternal(id);
   }

   public bool ForceChangeState(T id)
   {
      if (!states.ContainsKey(id) || currentState.IsLocked())
         return false;

      ChangeStateInternal(id);
      return true;
   }

   public void GoBack()
   {
      if (!states.ContainsKey(previousId) || currentState.IsLocked())
      {
         GD.PushError($"There is no previous state to go back to Or current state is locked. Current State Id: {currentId}");
         return;
      }

      ChangeStateInternal(previousId);
   }

   public bool GoBackIfPossible()
   {
      if (!states.ContainsKey(previousId) || currentState.IsLocked())
         return false;

      ChangeStateInternal(previousId);
      return true;
   }

   private void ChangeStateInternal(T id, bool ignoreExit = false)
   {
      if (!states.ContainsKey(id))
         return;

      if (!ignoreExit && !currentState.IsLocked())
         currentState.Exit?.Invoke();

      if (hasInitialId)
         StateChanged?.Invoke(currentId, id);

      stateTime = 0f;
      previousId = currentId;
      currentId = id;
      currentState = states[id];

      currentState.Enter?.Invoke();
   }

   /* --------------------------------------------------------
   *  TRANSITION MANAGEMENT
   * -------------------------------------------------------- */
   public Transition AddTransition(T fromId, T toId, Predicate<StateMachine<T>> condition, float overrideMinTime = default)
   {
      if (!states.ContainsKey(toId))
         return null;

      if (!transitions.ContainsKey(fromId))
         transitions[fromId] = new List<Transition>();

      Transition transition = new Transition(fromId, toId, condition, overrideMinTime);

      transitions[fromId].Add(transition);

      return transition;
   }

   public Transition AddGlobalTransition(T toId, Predicate<StateMachine<T>> condition, float overrideMinTime = default)
   {
      if (!states.ContainsKey(toId))
         return null;

      Transition transition = new Transition(default, toId, condition, overrideMinTime);

      globalTransitions.Add(transition);

      return transition;
   }

   public void RemoveTransition(T from, T to)
   {
      if (!states.ContainsKey(from))
         return;

      int originalCount = transitions[from].Count;
      transitions[from] = transitions[from].Where(t => !t.To.Equals(to)).ToList();

      if (transitions[from].Count == 0)
         transitions.Remove(from);

      if (transitions.ContainsKey(from))
         if (transitions[from].Count == originalCount)
            GD.PushError($"No Transition Was Found Between: {from} -> {to}");
   }

   public void RemoveGlobalTransition(T to)
   {
      int originalCount = globalTransitions.Count;
      globalTransitions = globalTransitions.Where(t => !t.To.Equals(to)).ToList();

      if (globalTransitions.Count == originalCount)
         GD.PushError($"No Global Transition Was Found Between: {currentId} -> {to}");
   }

   public void CleanTransitionsFromState(T from)
   {
      if (transitions.ContainsKey(from))
         transitions.Remove(from);
   }

   public void CleanTransitions() => transitions.Clear();
   public void CleanGlobalTransitions() => globalTransitions.Clear();

   /* --------------------------------------------------------
   *  PROCESSING
   * -------------------------------------------------------- */
   public void Update(ProcessType processType, double delta)
   {
      if (paused || currentState == null)
         return;

      if (currentState.processType == processType)
      {
         stateTime += (float)delta;
         currentState.Update?.Invoke(delta);
         CheckTransitions();
      }
   }

   private void CheckTransitions()
   {
      var evaluator = TransitionPool.Get();

      try
      {
         bool timeoutTriggered = currentState.Timeout > 0f && stateTime >= currentState.Timeout;
         
         if (timeoutTriggered)
         {
            ChangeStateInternal(currentState.RestartId);
            TransitionTriggered?.Invoke(currentId, currentState.RestartId);
            return;
         }
         
         if (transitions.TryGetValue(currentId, out var currentTransitions))
            evaluator.CandidateTransitions.AddRange(currentTransitions);

         evaluator.CandidateTransitions.AddRange(globalTransitions);
         evaluator.CandidateTransitions.Sort(Transition.Compare);

         CheckTransitionsLoop(evaluator.CandidateTransitions);
      }
      finally
      {
         TransitionPool.Return(evaluator);
      }
   }

   private void CheckTransitionsLoop(List<Transition> currentTransitions)
   {
      if (currentState.IsLocked())
         return;
         
      foreach (Transition transition in currentTransitions)
      {
         float requiredTime = transition.OverrideMinTime > 0f ? transition.OverrideMinTime : currentState.MinTime;
         bool timeRequirementMet = transition.ForceInstantTransition || stateTime >= requiredTime;

         if (timeRequirementMet && (transition.Condition?.Invoke(this) ?? true))
         {
            ChangeStateInternal(transition.To);
            TransitionTriggered?.Invoke(transition.From, transition.To);
            return;
         }
      }
   }

   /* --------------------------------------------------------
   *  PAUSE / RESUME
   * -------------------------------------------------------- */
   public void Pause() => paused = true;
   public bool IsPaused() => paused;

   public void Resume(bool resetTime = false)
   {
      paused = false;
      if (resetTime)
         ResetStateTime();
   }

   public void ResetStateTime() => stateTime = 0f;

   /* --------------------------------------------------------
   *  GETTERS / DEBUG
   * -------------------------------------------------------- */
   public T GetCurrentStateId() => currentState != null ? currentState.Id : default;
   public T GetInitialStateId() => initialId;
   public float GetStateTime() => stateTime;
   public float GetMinStateTime() => currentState?.MinTime ?? 0f;

   public float GetRemainingTime() =>
      currentState?.Timeout > 0 ? Mathf.Max(0, currentState.Timeout - stateTime) : -1f;

   public bool HasTransition(T from, T to) =>
      transitions.ContainsKey(from) && transitions[from].Any(t => t.To.Equals(to));

   public bool HasAnyTransitionFrom(T id) =>
      transitions.ContainsKey(id) && transitions[id].Count > 0;

   public bool HasGlobalTransition(T to) =>
      globalTransitions.Any(t => t.To.Equals(to));

   public bool HasPreviousState() => !EqualityComparer<T>.Default.Equals(previousId, default);
   public bool HasStateId(T id) => states.ContainsKey(id);

   public bool IsCurrentState(T id) => currentState?.Id.Equals(id) ?? false;
   public bool IsPreviousState(T id) => Equals(previousId, id);

   public T GetPreviousStateId() => previousId;

   public string DebugCurrentTransition() => $"{previousId} -> {currentId}";

   public string DebugAllTransitions()
   {
      var result = new List<string>();
      foreach (var kvp in transitions)
         foreach (var t in kvp.Value)
            result.Add($"{t.From} -> {t.To} (Priority: {t.Priority})");

      foreach (var t in globalTransitions)
         result.Add($"GLOBAL -> {t.To} (Priority: {t.Priority})");

      return string.Join("\n", result);
   }

   public string DebugAllStates()
   {
      var result = new List<string>();

      foreach (State state in states.Values)
         result.Add(state.Id.ToString());

      return string.Join("\n", result);
   }

   /* --------------------------------------------------------
   *  NESTED CLASSES
   * -------------------------------------------------------- */
   public class State
   {
      public T Id;
      public T RestartId;
      public float MinTime = 0f;
      public float Timeout = -1f;
      public Action<double> Update;
      public Action Enter;
      public Action Exit;
      private bool Locked;

      public ProcessType processType = ProcessType.PhysicsProcess;

      public void Lock() => Locked = true;
      public void Unlock() => Locked = false;
      public void SetRestartId(T value) => RestartId = value;
      public bool IsLocked() => Locked;

      public State(T id, Action<double> update, Action enter, Action exit, float minTime, float timeout, ProcessType type = default)
      {
         Id = id;
         Update = update;
         Enter = enter;
         Exit = exit;
         MinTime = minTime;
         Timeout = timeout;
         processType = type;
      }
   }

   public class Transition
   {
      private static int nextIndex = 0;

      public T From;
      public T To;
      public float OverrideMinTime = -1f;
      public Predicate<StateMachine<T>> Condition;
      public bool ForceInstantTransition;
      public int Priority;
      public int InsertionIndex { get; private set; }

      public void ForceInstant() => ForceInstantTransition = true;

      public void SetPriority(int value)
      {
         if (value < 0)
         {
            GD.PushError("Priority Should be greater than zero");
            return;
         }
         Priority = value;
      }

      public Transition(T from, T to, Predicate<StateMachine<T>> condition, float minTime = -1)
      {
         From = from;
         To = to;
         Condition = condition;
         OverrideMinTime = minTime;

         InsertionIndex = nextIndex++;
      }

      internal static int Compare(Transition a, Transition b)
      {
         int priorityCompare = b.Priority.CompareTo(a.Priority);
         return priorityCompare != 0 ? priorityCompare : a.InsertionIndex.CompareTo(b.InsertionIndex);
      }
   }

   public struct TransitionPool
   {
      private static Queue<TransitionEvaluator> pool = new();
      
      public static TransitionEvaluator Get()
      {
         return pool.Count > 0 ? pool.Dequeue() : new TransitionEvaluator();
      }
      
      public static void Return(TransitionEvaluator evaluator)
      {
         evaluator.Reset();
         pool.Enqueue(evaluator);
      }
   }

   public class TransitionEvaluator
   {
      public List<Transition> CandidateTransitions { get; private set; } = new();
      
      public void Reset() => CandidateTransitions.Clear();
   }
}




