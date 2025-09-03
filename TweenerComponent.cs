using Godot;
using System;
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;

public class TweenerComponent
{
   public TweenerComponent(Node owner, Tween.TransitionType trans = Tween.TransitionType.Cubic, Tween.EaseType ease = Tween.EaseType.In)
   {
      Owner = owner;
      defaultTransition = trans;
      defaultEase = ease;
   }

   public enum LoopMode
   {
      None,
      Linear,
      PingPong
   }

   public Node Owner { get; private set; }

   private Tween.TransitionType defaultTransition;
   private Tween.EaseType defaultEase;

   public Dictionary<string, Curve> Curves { get; private set; } = new();
   public Dictionary<string, List<Tween>> tweenGroups = new();
   public List<Tween> ActiveTweens { get; private set; } = new();

   public void AddCurveData(string key, Curve curve)
   {
      Curves[key] = curve;
   }

   public void StopAllTweens()
   {
      // Converted to array to avoid InvalidOperationException if tweens remove themselves in Finished;
      foreach (Tween tween in ActiveTweens.ToArray())
         tween.Kill();
      ActiveTweens.Clear();
   }
   public void StopTweensByTag(string tag)
   {
      if (!tweenGroups.TryGetValue(tag, out var group))
         return;

      foreach (var tween in group.ToArray())
         tween.Kill();
      tweenGroups[tag].Clear();
   }

   public void ClearCurves() => Curves.Clear();

   public Tween Animate(GodotObject target, string property, Variant[] values, float[] durations, Tween.TransitionType? trans = null, Tween.EaseType? ease = null, Action onFinished = null, LoopMode loopMode = LoopMode.None, string tag = null)
   {
      if (target == null) throw new ArgumentNullException(nameof(target));
      if (values == null) throw new ArgumentNullException(nameof(values));
      if (values.Length == 0) throw new ArgumentException("Values array cannot be empty");

      Tween tween = Owner.CreateTween();
      tween.SetTrans(trans ?? defaultTransition);
      tween.SetEase(ease ?? defaultEase);

      int count = Math.Min(values.Length, durations.Length);

      for (int i = 0; i < count; i++)
         tween.TweenProperty(target, property, values[i], durations[i]);

      ActiveTweens.Add(tween);
      
      if (!string.IsNullOrEmpty(tag))
      {
         if (!tweenGroups.ContainsKey(tag)) tweenGroups[tag] = new List<Tween>();
         tweenGroups[tag].Add(tween);
      }

      tween.Finished += () =>
      {
         ActiveTweens.Remove(tween);

         if (!string.IsNullOrEmpty(tag)) 
            tweenGroups[tag].Remove(tween);

         onFinished?.Invoke();
      };

      return tween;
   }

   public async Task AnimateAsync(GodotObject target, string property, Variant[] values, float[] durations, Tween.TransitionType? trans = null, Tween.EaseType? ease = null)
   {
      Tween tween = Animate(target, property, values, durations, trans, ease);
      await Owner.ToSignal(tween, Tween.SignalName.Finished);
   }

   public async Task AnimatePathAsync(Node target, string property, string curveName, Variant initialValue, Variant finalValue, float duration = 1f)
   {
      Tween tween = AnimatePath(target, property, curveName, initialValue, finalValue, duration);
      await Owner.ToSignal(tween, Tween.SignalName.Finished);
   }

   public async Task Sequence(params Func<Task>[] steps)
   {
      foreach (var step in steps)
         await step();
   }

   public void AnimateData(TweenerData data)
   {
      Animate(data.Target, data.Property, data.Values, data.Durations, data.TransitionType, data.EaseType, data.OnFinished);
   }

   public async Task AnimateDataAsync(TweenerData data)
   {
      Tween tween = Animate(data.Target, data.Property, data.Values, data.Durations, data.TransitionType, data.EaseType, data.OnFinished);
      await Owner.ToSignal(tween, Tween.SignalName.Finished);
   }

   public Tween AnimatePath(Node target, string property, string curveName, Variant initial, Variant final, float duration = 1f, Action onFinished = null, string tag = null)
   {
      Tween tween = Owner.CreateTween();

      if (!Curves.TryGetValue(curveName, out Curve curve))
      {
         GD.PushError($"Curve '{curveName}' not found in TweenerComponent.");
         return null;
      }

      Callable method = Callable.From<float>(t => Interpolate(t, target, property, curve, initial, final));
      tween.TweenMethod(method, 0.0f, 1.0f, duration);

      ActiveTweens.Add(tween);

      if (!string.IsNullOrEmpty(tag))
      {
         if (!tweenGroups.ContainsKey(tag)) tweenGroups[tag] = new List<Tween>();
         tweenGroups[tag].Add(tween);
      }

      tween.Finished += () =>
      {
         ActiveTweens.Remove(tween);

         if (!string.IsNullOrEmpty(tag))
            tweenGroups[tag].Remove(tween);

         onFinished?.Invoke();
      };

      return tween;
   }

   public async Task AnimateLoop(GodotObject target, string property, Variant[] values, float[] durations, Tween.TransitionType? trans = null, Tween.EaseType? ease = null, Action onFinished = null, LoopMode loopMode = LoopMode.Linear, int loopCount = -1)
   {
      int loopDone = 0;
      bool pingPong = loopMode == LoopMode.PingPong;

      Variant[] currentValues = values;
      float[] currentDurations = durations;

      do
      {
         await AnimateAsync(target, property, currentValues, currentDurations, trans, ease);
         onFinished?.Invoke();

         if (pingPong)
         {
            currentValues = currentValues.Reverse().ToArray();
            currentDurations = currentDurations.Reverse().ToArray();
         }

         loopDone++;
      }
      while (loopMode != LoopMode.None && (loopCount < 0 || loopDone < loopCount));
   }

   public async Task AnimatePathLoop(Node target, string property, string curveName, Variant initialValue, Variant finalValue, float duration = 1f, Action onFinished = null, LoopMode loopMode = LoopMode.None, int loopCount = -1) 
   { 
      int loopDone = 0; 
      bool pingPong = loopMode == LoopMode.PingPong; 

      Variant currentInitialValue = initialValue; 
      Variant currentFinalValue = finalValue; 
      
      do 
      { 
         await AnimatePathAsync(target, property, curveName, currentInitialValue, currentFinalValue, duration); 
         onFinished?.Invoke(); 
         
         if (pingPong) 
         {
            currentFinalValue = initialValue; 
            currentInitialValue = finalValue; 
         } 

         loopDone++; 
      } 
      while (loopMode != LoopMode.None && (loopCount < 0 || loopDone < loopCount)); 
   }

   private void Interpolate(float t, Node target, string property, Curve curve, Variant a, Variant b)
   {
      if (target == null || !GodotObject.IsInstanceValid(target))
      {
         GD.PushWarning($"Target node for property '{property}' has been freed during animation");
         return;
      }

      if (curve == null)
      {
         GD.PushError("Curve is null during interpolation");
         return;
      }

      float sample = curve.Sample(t);
      Variant result;

      switch (a.VariantType)
      {
         case Variant.Type.Float:
            result = (float)a + (((float)b - (float)a) * sample);
            break;

         case Variant.Type.Vector2:
            result = (Vector2)a + (((Vector2)b - (Vector2)a) * sample);
            break;

         case Variant.Type.Vector3:
            result = (Vector3)a + (((Vector3)b - (Vector3)a) * sample);
            break;

         case Variant.Type.Color:
            result = (Color)a + (((Color)b - (Color)a) * sample);
            break;

         case Variant.Type.Quaternion:
            result = (Quaternion)a + (((Quaternion)b - (Quaternion)a) * sample);
            break;

         default:
            GD.PushError($"Unsupported type {a.VariantType} for interpolation.");
            return;
      }

      target.Set(property, result);
   }

   public class TweenerData
   {
      public Tween.TransitionType TransitionType { get; private set; }
      public Tween.EaseType EaseType { get; private set; }

      public GodotObject Target { get; private set; }
      public string Property { get; private set; }

      public Variant[] Values { get; private set; }
      public float[] Durations { get; private set; }

      public Action OnFinished { get; private set; }

      public TweenerData SetTrans(Tween.TransitionType type)
      {
         TransitionType = type;
         return this;
      }

      public TweenerData SetEase(Tween.EaseType type)
      {
         EaseType = type;
         return this;
      }

      public TweenerData(Node target, string property, Variant[] values, float[] durations, Action onFinished = null)
      {
         Target = target;
         Property = property;
         Values = values;
         Durations = durations;
         OnFinished = onFinished;
      }
   }
}
