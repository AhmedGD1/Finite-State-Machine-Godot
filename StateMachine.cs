using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

[GlobalClass]
public partial class StateMachine : Node
{
   /* --------------------------------------------------------
   *  ENUMS & Events;
   * -------------------------------------------------------- */
   public enum ProcessType { Process, PhysicsProcess }

   [Signal] public delegate void StateChangedEventHandler(int from, int to);

   /* --------------------------------------------------------
   *  FIELDS
   * -------------------------------------------------------- */
   private Dictionary<Enum, State> states = new();
   private Dictionary<Enum, List<Transition>> transitions = new();
   private Dictionary<string, bool> signalConditions = new();
   private List<Transition> globalTransitions = new();
   private List<(Godot.GodotObject owner, string name, Callable callable)> connectedSignals = new();

   private State currentState;
   private Enum currentId;
   private Enum previousId;
   private Enum initialId;

   private bool paused;
   private float stateTime;

   /* --------------------------------------------------------
   *  GODOT LIFECYCLE
   * -------------------------------------------------------- */
   public override void _Process(double delta) =>
      ProcessState(ProcessType.Process, delta);

   public override void _PhysicsProcess(double delta) =>
      ProcessState(ProcessType.PhysicsProcess, delta);

   public override void _ExitTree() =>
      DisconnectAllSignals();

   /* --------------------------------------------------------
   *  STATE MANAGEMENT
   * -------------------------------------------------------- */
   public State AddState(Enum id, Action<double> update = null, Action enter = null, Action exit = null, float minTime = 0f, float timeout = -1f)
   {
      if (states.ContainsKey(id))
         return null;

      State state = new State(id, update, enter, exit, minTime, timeout);

      states[id] = state;

      if (initialId == null)
      {
         initialId = id;
         ChangeStateInternal(id, ignoreExit: true);
      }

      state.SetRestartId(initialId);
      return state;
   }

   public void RemoveState(Enum id)
   {
      if (!states.ContainsKey(id))
         return;

      states.Remove(id);

      if (currentId == id)
         Reset();

      foreach (var key in transitions.Keys.ToList())
         transitions[key] = transitions[key].Where(t => t.To != id && t.From != id).ToList();

      globalTransitions = globalTransitions.Where(t => t.To != id).ToList();
   }

   public void Reset()
   {
      if (states.Count == 0)
         return;

      ChangeStateInternal(initialId);
      previousId = null;
   }

   public void RestartCurrentState(bool ignoreExit = false, bool ignoreEnter = false)
   {
      ResetStateTime();

      if (!ignoreExit) currentState?.Exit?.Invoke();
      if (!ignoreEnter) currentState?.Enter?.Invoke();
   }

   public State GetState(Enum id)
   {
      states.TryGetValue(id, out var state);
      return state;
   }

   /* --------------------------------------------------------
   *  STATE CHANGING
   * -------------------------------------------------------- */
   public void ChangeState(Enum id, bool condition)
   {
      if (condition && states.ContainsKey(id))
         ChangeStateInternal(id);
   }

   public bool ForceChangeState(Enum id)
   {
      if (!states.ContainsKey(id))
         return false;

      ChangeStateInternal(id);
      return true;
   }

   public void GoBack()
   {
      if (previousId == null)
         return;

      ChangeStateInternal(previousId);
   }

   public bool GoBackIfPossible()
   {
      if (previousId == null || !states.ContainsKey(previousId))
         return false;

      ChangeStateInternal(previousId);
      return true;
   }

   private void ChangeStateInternal(Enum id, bool ignoreExit = false)
   {
      if (!states.ContainsKey(id))
         return;

      if (!ignoreExit)
         currentState?.Exit?.Invoke();

      EmitSignal(SignalName.StateChanged, EnumToInt(currentId), EnumToInt(id));

      ResetStateTime();
      previousId = currentId;
      currentId = id;
      currentState = states[id];

      currentState.Enter?.Invoke();
   }

   /* --------------------------------------------------------
   *  TRANSITION MANAGEMENT
   * -------------------------------------------------------- */
   public Transition AddTransition(Enum fromId, Enum toId, Predicate<StateMachine> condition, float overrideMinTime = -1f)
   {
      if (!states.ContainsKey(toId))
         return null;

      if (!transitions.ContainsKey(fromId))
         transitions[fromId] = new List<Transition>();

      Transition transition = new Transition(fromId, toId, condition, overrideMinTime);

      transitions[fromId].Add(transition);
      transitions[fromId].Sort(Transition.Compare);

      return transition;
   }

   public Transition AddGlobalTransition(Enum toId, Predicate<StateMachine> condition, float overrideMinTime = -1f)
   {
      if (!states.ContainsKey(toId))
         return null;

      Transition transition = new Transition(null, toId, condition, overrideMinTime);

      globalTransitions.Add(transition);
      globalTransitions.Sort(Transition.Compare);

      return transition;
   }

   public void RemoveTransition(Enum from, Enum to)
   {
      if (!states.ContainsKey(from))
         return;

      int originalCount = transitions[from].Count;
      transitions[from] = transitions[from].Where(t => t.To != to).ToList();

      if (transitions[from].Count == 0)
         transitions.Remove(from);

      if (transitions.ContainsKey(from))
         if (transitions[from].Count == originalCount)
            GD.PushError($"No Transition Was Found Between: {from} -> {to}");
   }

   public void RemoveGlobalTransition(Enum to)
   {
      int originalCount = globalTransitions.Count;
      globalTransitions = globalTransitions.Where(t => t.To != to).ToList();

      if (globalTransitions.Count == originalCount)
         GD.PushError($"No Global Transition Was Found Between: {currentId} -> {to}");
   }

   public void CleanTransitionsFromState(Enum from)
   {
      if (transitions.ContainsKey(from))
         transitions.Remove(from);
   }

   public void CleanTransitions() => transitions.Clear();
   public void CleanGlobalTransitions() => globalTransitions.Clear();

   /* --------------------------------------------------------
   *  SIGNAL MANAGEMENT
   * -------------------------------------------------------- */
   public Transition AddSignalTransition(Enum from, Enum to, Node source, string signalName, float overrideMinTime = -1f)
   {
      Predicate<StateMachine> condition = CreateConditionFromSignal(source, signalName);
      return AddTransition(from, to, condition, overrideMinTime);
   }

   public Transition AddGlobalSignalTransition(Enum to, Node source, string signalName, float overrideMinTime = -1f)
   {
      Predicate<StateMachine> condition = CreateConditionFromSignal(source, signalName);
      return AddGlobalTransition(to, condition, overrideMinTime);
   }

   private Predicate<StateMachine> CreateConditionFromSignal(Node source, string signalName)
   {
      string key = $"{source.GetInstanceId()}_{signalName}";
      signalConditions[key] = false;

      Callable OnSignalInternal = Callable.From(() => { signalConditions[key] = true; });

      source.Connect(signalName, OnSignalInternal);
      connectedSignals.Add((source, signalName, OnSignalInternal));

      return sm =>
      {
         if (signalConditions.TryGetValue(key, out bool result) && result)
         {
            signalConditions[key] = false; // reset so it's only valid for 1 check
            return true;
         }
         return false;
      };
   }

   public void DisconnectAllSignals()
   {
      foreach ((Godot.GodotObject owner, string name, Callable callable) in connectedSignals)
         if (IsInstanceValid(owner) && owner.IsConnected(name, callable))
            owner.Disconnect(name, callable);

      connectedSignals.Clear();
      signalConditions.Clear();
   }

   private static int EnumToInt(Enum value) => Convert.ToInt32(value);

   /* --------------------------------------------------------
   *  PROCESSING
   * -------------------------------------------------------- */
   private void ProcessState(ProcessType processType, double delta)
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
      if (currentState.Timeout > 0f && stateTime >= currentState.Timeout)
      {
         ChangeStateInternal(currentState.RestartId);
         return;
      }

      if (transitions.TryGetValue(currentId, out var currentTransitions))
         CheckTransitionsLoop(currentTransitions);

      CheckTransitionsLoop(globalTransitions);
   }

   private void CheckTransitionsLoop(List<Transition> currentTransitions)
   {
      foreach (Transition transition in currentTransitions)
      {
         if (currentState.IsLocked())
            break;

         float requiredTime = transition.OverrideMinTime > 0f ? transition.OverrideMinTime : currentState.MinTime;
         bool timeRequirementMet = transition.ForceInstantTransition || stateTime >= requiredTime;

         if (timeRequirementMet && (transition.Condition?.Invoke(this) ?? true))
         {
            ChangeStateInternal(transition.To);
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
   public Enum GetCurrentStateId() => currentState.Id;
   public Enum GetInitialStateId() => initialId;
   public float GetStateTime() => stateTime;
   public float GetMinStateTime() => currentState.MinTime;

   public bool HasTransition(Enum from, Enum to) =>
      transitions.ContainsKey(from) && transitions[from].Any(t => t.To == to);

   public bool HasGlobalTransition(Enum to) =>
      globalTransitions.Any(t => t.To == to);

   public bool HasStateId(Enum id) => states.ContainsKey(id);

   public bool IsInState(Enum id) => Equals(currentState.Id, id);
   public bool IsPreviousState(Enum id) => Equals(previousId, id);

   public Enum GetPreviousStateId() => previousId;

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

   /* --------------------------------------------------------
   *  NESTED CLASSES
   * -------------------------------------------------------- */
   public class State
   {
      public Enum Id;
      public Enum RestartId;
      public float MinTime;
      public float Timeout = -1f;
      public Action<double> Update;
      public Action Enter;
      public Action Exit;
      public bool Locked;

      public ProcessType processType = ProcessType.PhysicsProcess;

      public void Lock() => Locked = true;
      public void Unlock() => Locked = false;
      public void SetRestartId(Enum value) => RestartId = value;
      public void SetProcessType(ProcessType type) => processType = type;
      public bool IsLocked() => Locked;

      public State(Enum id, Action<double> update, Action enter, Action exit, float minTime, float timeout)
      {
         Id = id;
         Update = update;
         Enter = enter;
         Exit = exit;
         MinTime = minTime;
         Timeout = timeout;
      }
   }

   public class Transition
   {
      private static int nextIndex = 0;
      public Enum From;
      public Enum To;
      public float OverrideMinTime = -1f;
      public Predicate<StateMachine> Condition;
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

      public Transition(Enum from, Enum to, Predicate<StateMachine> condition, float minTime = -1)
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
}




