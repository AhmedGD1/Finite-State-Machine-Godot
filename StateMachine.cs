using Godot;
using System;
using System.Linq;
using System.Collections.Generic;

/* =======================================================================
 *  GENERIC STATE MACHINE
 * ======================================================================= */
public class StateMachine<T> where T : Enum
{
   /* --------------------------------------------------------
    *  ENUMS & EVENTS
    * -------------------------------------------------------- */
   public enum ProcessType { PhysicsProcess, Process }

   // Events fired on state/transition changes
   public event Action<T, T> StateChanged;
   public event Action<T, T> TransitionTriggered;

   /* --------------------------------------------------------
    *  FIELDS
    * -------------------------------------------------------- */
   private Dictionary<T, State> states = new();
   private Dictionary<T, List<Transition>> transitions = new();
   private List<Transition> globalTransitions = new();
   private Dictionary<string, object> globalData = new();

   private State currentState;
   private T currentId;
   private T previousId;
   private T initialId;

   private bool hasInitialId;
   private bool paused;
   private float stateTime;

   private Node animator;

   /* --------------------------------------------------------
    *  STATE MANAGEMENT
    * -------------------------------------------------------- */
   public State AddState(T id, Action<double> update = null, Action enter = null, Action exit = null, float minTime = default, float timeout = -1, ProcessType processType = ProcessType.PhysicsProcess)
   {
      if (states.ContainsKey(id))
      {
         GD.PushError($"Trying to store an existent state: {id}");
         return null;
      }

      State state = new State(id, update, enter, exit, minTime, timeout, processType);
      states[id] = state;

      // First added state becomes the initial one
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
      {
         GD.PushWarning("Trying to remove a non-existent state");
         return;
      }

      states.Remove(id);

      if (initialId.Equals(id))
         hasInitialId = false;

      if (currentId.Equals(id))
         Reset();

      foreach (var key in transitions.Keys.ToList())
         transitions[key] = transitions[key].Where(t => !t.To.Equals(id) && !t.From.Equals(id)).ToList();

      globalTransitions = globalTransitions.Where(t => !t.To.Equals(id)).ToList();
   }

   public bool Reset()
   {
      if (states.Count == 0)
      {
         GD.PushWarning("Trying to reset an empty state machine");
         return false;
      }

      if (!hasInitialId)
         SetInitialId(states.Values.First().Id);

      ChangeStateInternal(initialId);
      previousId = default;
      return true;
   }

   public void SetInitialId(T id)
   {
      if (!states.ContainsKey(id))
      {
         GD.PushError($"Trying to set non-existent state as initial: {id}");
         return;
      }

      initialId = id;
      hasInitialId = true;
   }

   public void RestartCurrentState(bool ignoreExit = false, bool ignoreEnter = false)
   {
      if (currentState == null)
      {
         GD.PushWarning("Trying to restart a non-existent state");
         return;
      }

      ResetStateTime();

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
      {
         GD.PushWarning("Trying to switch to a non-existent state");
         return;
      }

      bool canExit = !ignoreExit && currentState != null && !currentState.IsLocked();
      if (canExit) currentState.Exit?.Invoke();

      // Switch state
      stateTime = 0f;
      previousId = currentId;
      currentId = id;
      currentState = states[id];

      currentState.Enter?.Invoke();

      // Play animation if defined
      if (currentState.Data.ContainsKey("Animation"))
      {
         var config = currentState.GetData<AnimationConfig>("Animation");
         config.PlayAnimation(animator);
      }

      if (hasInitialId)
         StateChanged?.Invoke(previousId, currentId);
   }

   /* --------------------------------------------------------
    *  TRANSITION MANAGEMENT
    * -------------------------------------------------------- */
   public Transition AddTransition(T fromId, T toId, Predicate<StateMachine<T>> condition, float overrideMinTime = default)
   {
      if (!states.ContainsKey(toId))
      {
         GD.PushError("Trying to add a transition to a non-existent state");
         return null;
      }

      if (!transitions.ContainsKey(fromId))
         transitions[fromId] = new();

      Transition transition = new Transition(fromId, toId, condition, overrideMinTime);
      transitions[fromId].Add(transition);

      return transition;
   }

   public Transition AddGlobalTransition(T toId, Predicate<StateMachine<T>> condition, float overrideMinTime = default)
   {
      if (!states.ContainsKey(toId))
      {
         GD.PushError("Trying to add a global transition to a non-existent state");
         return null;
      }

      Transition transition = new Transition(default, toId, condition, overrideMinTime);
      globalTransitions.Add(transition);

      return transition;
   }

   public bool RemoveTransition(T from, T to)
   {
      if (!transitions.ContainsKey(from))
      {
         GD.PushWarning("Trying to remove a non-existent transition");
         return false;
      }

      int originalCount = transitions[from].Count;
      transitions[from] = transitions[from].Where(t => !t.To.Equals(to)).ToList();

      if (transitions[from].Count == 0)
         transitions.Remove(from);

      bool removed = transitions.ContainsKey(from) ? transitions[from].Count < originalCount : originalCount > 0;
      if (!removed) GD.PushError($"No Transition Was Found Between: {from} -> {to}");

      return removed;
   }

   public bool RemoveGlobalTransition(T to)
   {
      if (!HasAnyGlobalTransition(to))
      {
         GD.PushWarning("Trying to remove a non-existent global transition");
         return false;
      }

      int originalCount = globalTransitions.Count;
      globalTransitions = globalTransitions.Where(t => !t.To.Equals(to)).ToList();

      bool removed = globalTransitions.Count < originalCount;
      if (!removed) GD.PushError($"No Global Transition Was Found Between: {currentId} -> {to}");
      return removed;
   }

   public void CleanTransitionsFromState(T from) => transitions.Remove(from);
   public void CleanTransitions() => transitions.Clear();
   public void CleanGlobalTransitions() => globalTransitions.Clear();

   /* --------------------------------------------------------
    *  PROCESSING (UPDATE LOOP)
    * -------------------------------------------------------- */
   public void Update(ProcessType processType, double delta)
   {
      if (paused || currentState == null) return;

      if (currentState.processType == processType)
      {
         stateTime += (float)delta;
         currentState.Update?.Invoke(delta);
         CheckTransitions();
      }
   }

   private void CheckTransitions()
   {
      // Timeout transition
      bool timeoutTriggered = currentState.Timeout > 0f && stateTime >= currentState.Timeout;
      if (timeoutTriggered)
      {
         var restartId = currentState.RestartId;
         ChangeStateInternal(restartId);
         TransitionTriggered?.Invoke(currentId, restartId);
         return;
      }

      if (currentState.IsLocked()) return;

      var evaluator = TransitionPool.Get();

      try
      {
         if (transitions.TryGetValue(currentId, out var currentTransitions))
            evaluator.CandidateTransitions.AddRange(currentTransitions);

         evaluator.CandidateTransitions.AddRange(globalTransitions);

         // Sort transitions by priority
         if (evaluator.HasCandidates())
         {
            evaluator.CandidateTransitions.Sort(Transition.Compare);
            CheckTransitionsLoop(evaluator.CandidateTransitions);
         }
      }
      finally
      {
         TransitionPool.Return(evaluator);
      }
   }

   private void CheckTransitionsLoop(List<Transition> candidateTransitions)
   {
      foreach (Transition transition in candidateTransitions)
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
      if (resetTime) ResetStateTime();
   }

   public void ResetStateTime() => stateTime = 0f;

   /* --------------------------------------------------------
    *  GETTERS / DEBUG
    * -------------------------------------------------------- */
   public void SetAnimator(Node what) => animator = what;

   public bool SetGlobalData(string key, object value)
   {
      if (string.IsNullOrEmpty(key)) return false;
      globalData[key] = value;
      return true;
   }

   public void RemoveGlobalData(string key) => globalData.Remove(key);

   public TData GetGlobalData<TData>(string key) =>
      globalData.TryGetValue(key, out var value) && value is TData castResult ? castResult : default;

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

   public bool HasAnyGlobalTransition(T to) =>
      globalTransitions.Any(t => t.To.Equals(to));

   public bool HasPreviousState() => !EqualityComparer<T>.Default.Equals(previousId, default);
   public bool HasStateId(T id) => states.ContainsKey(id);

   public bool IsCurrentState(T id) => currentState?.Id.Equals(id) ?? false;
   public bool IsPreviousState(T id) => Equals(previousId, id);
   public bool IsInStateWithTag(string tag) => currentState?.Tags.Contains(tag) ?? false;

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
      public T Id { get; private set; }
      public T RestartId { get; private set; }
      public float MinTime { get; private set; }
      public float Timeout { get; private set; }
      public Action<double> Update { get; private set; }
      public Action Enter { get; private set; }
      public Action Exit { get; private set; }
      public ProcessType processType { get; private set; }

      private bool Locked;
      private HashSet<string> tags = new();
      private Dictionary<string, object> data = new();

      public IReadOnlyCollection<string> Tags => tags;
      public IReadOnlyDictionary<string, object> Data => data;

      public State Lock()
      {
         Locked = true;
         return this;
      }

      public State Unlock()
      {
         Locked = false;
         return this;
      }

      public State SetRestartId(T value)
      {
         RestartId = value;
         return this;
      }

      public bool IsLocked() => Locked;
      public bool HasTag(string tag) => Tags.Contains(tag);
      public bool HasData(string key) => Data.ContainsKey(key);
      public IEnumerable<string> GetTags() => Tags;

      public State SetAnimationData(string animationName, float speed = 1f, float blendTime = -1f, bool loop = false)
      {
         AnimationConfig config = new AnimationConfig(animationName, speed, blendTime, loop);

         AddOrUpdateData("Animation", config);
         return this;
      }

      public State AddTags(params string[] what)
      {
         foreach (var tag in what)
            tags.Add(tag);
         return this;
      }

      public State AddOrUpdateData(string key, object value)
      {
         if (!string.IsNullOrEmpty(key))
            data[key] = value;
         return this;
      }

      public State RemoveData(string key)
      {
         data.Remove(key);
         return this;
      }

      public bool TryGetData<TData>(string key, out TData result)
      {
         if (data.TryGetValue(key, out var value) && value is TData castValue)
         {
            result = castValue;
            return true;
         }

         result = default;
         return false;
      }

      public TData GetData<TData>(string key, TData defaultValue = default)
      {
         return TryGetData<TData>(key, out var result) ? (TData)result : defaultValue;
      }

      public State(T id, Action<double> update, Action enter, Action exit, float minTime, float timeout, ProcessType type = default)
      {
         Id = id;
         Update = update;
         Enter = enter;
         Exit = exit;
         MinTime = Mathf.Max(0, minTime);
         Timeout = timeout;
         processType = type;
         RestartId = id;
      }
   }

   public class Transition
   {
      private static int nextIndex = 0;

      public T From { get; private set; }
      public T To { get; private set; }
      public float OverrideMinTime { get; private set; }
      public Predicate<StateMachine<T>> Condition { get; private set; }
      public bool ForceInstantTransition { get; private set; }
      public int Priority { get; private set; }
      public int InsertionIndex { get; private set; }

      public Transition ForceInstant()
      {
         ForceInstantTransition = true;
         return this;
      }

      public Transition SetPriority(int value)
      {
         Priority = Math.Max(0, value);
         return this;
      }

      public Transition SetCondition(Predicate<StateMachine<T>> condition)
      {
         Condition = condition;
         return this;
      }

      public Transition SetMinTime(float time)
      {
         OverrideMinTime = time;
         return this;
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

      public bool HasCandidates() => CandidateTransitions.Count > 0;
   }

   public struct AnimationConfig
   {
      public string Name { get; private set; }
      public float Speed { get; private set; } = 1.0f;
      public float CustomBlend { get; private set; } = -1f;
      public bool Loop { get; private set; }

      public void PlayAnimation(object animator)
      {  
         if (animator is AnimationPlayer animationPlayer)
         {
            animationPlayer.Play(Name, CustomBlend, Speed);
            animationPlayer.GetAnimation(Name).LoopMode = Loop ? Animation.LoopModeEnum.Linear : Animation.LoopModeEnum.None;
         }

         if (animator is AnimatedSprite2D sprite)
         {
            sprite.Play(Name, Speed);
            sprite.SpriteFrames.SetAnimationLoop(Name, Loop);
         }
      }

      public AnimationConfig(string name, float speed, float blend, bool loop)
      {
         Name = name;
         Speed = speed;
         CustomBlend = blend;
         Loop = loop;
      }
   }
}
   
