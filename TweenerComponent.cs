using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

[GlobalClass]
public partial class TweenerComponent : Node
{
    public event Action<string> TweenFinished;

    [Export] private Tween.TransitionType defaultTransition;
    [Export] private Tween.EaseType defaultEase;

    [Export] private Godot.Collections.Dictionary<string, Curve> curves = new();

    private readonly Dictionary<string, Tween> activeTweens = new();
    private readonly Dictionary<string, Curve> candidateCurves = new();

    public override void _Ready()
    {
        foreach (string tag in curves.Keys)
            candidateCurves.Add(tag, curves[tag]);
    }

    public void Animate(GodotObject target, TweenerData data)
    {
        if (!CheckErrors(target, data.Property)) return;

        if (activeTweens.TryGetValue(data.Tag, out Tween existing))
            existing.Kill();

        int count = Mathf.Min(data.Values.Length, data.Durations.Length);

        if (data.Values.Length != data.Durations.Length)
            GD.PushWarning("Tweener Data values & durations are not equal, some animations will not be shown");
        
        Tween tween = CreateTween();
        tween.SetTrans(data.TransitionType).SetEase(data.EaseType);
        tween.SetParallel(data.Parallel);
        tween.SetLoops(data.Loops);
        
        if (data.Delay > 0f)
            tween.TweenInterval(data.Delay);

        for (int i = 0; i < count; i++)
            tween.TweenProperty(target, data.Property, data.Values[i], data.Durations[i]);

        activeTweens.Add(data.Tag, tween);
        
        tween.Finished += () =>
        {
            data.OnFinished?.Invoke();
            TweenFinished?.Invoke(data.Tag);
            activeTweens.Remove(data.Tag);
        };
    }

    public void Animate(GodotObject target, string tag, string property, Variant[] values, float[] durations, Tween.TransitionType? trans = null, Tween.EaseType? ease = null, Action onFinished = null)
    {
        TweenerData tweenerData = new TweenerData
        {
            Tag = tag,
            Property = property,
            Values = values,
            Durations = durations,
            TransitionType = trans ?? defaultTransition,
            EaseType = ease ?? defaultEase,
            OnFinished = onFinished
        };

        Animate(target, tweenerData);
    }

    public void AnimatePath(GodotObject target, string property, string curveName, Variant from, Variant to, float duration = 1f, Action onFinished = null, string tag = null)
    {
        if (!CheckErrors(target, property)) return;

        if (!candidateCurves.TryGetValue(curveName, out Curve curve))
        {
            GD.PushError($"Curve '{curveName}' not found");
            return;
        }

        string tweenTag = tag ?? curveName;

        if (activeTweens.TryGetValue(tweenTag, out Tween existing))
            existing.Kill();

        Tween tween = CreateTween();
        Callable callable = Callable.From<float>(t => Interpolate(t, target, property, curve, from, to));

        tween.TweenMethod(callable, 0f, 1f, duration);
        activeTweens.Add(tweenTag, tween);

        tween.Finished += () =>
        {
            onFinished?.Invoke();
            TweenFinished?.Invoke(tweenTag);
            activeTweens.Remove(tweenTag);
        };
    }

    public void StopTween(string tag, bool emitFinished = false)
    {
        if (activeTweens.TryGetValue(tag, out Tween tween))
        {
            tween.Kill();
            activeTweens.Remove(tag);
            if (emitFinished)
                TweenFinished?.Invoke(tag);   
        }
    }

    public void StopAll(bool emitFinished = false) 
    {
        foreach (var tag in activeTweens.Keys.ToList())
            StopTween(tag, emitFinished);
    }

    public Tween GetActiveTween(string tag)
    {
        CleanupExpiredTweens();
        return activeTweens.TryGetValue(tag, out Tween result) ? result : null;
    }

    public bool HasActiveTween(string tag)
    {
        CleanupExpiredTweens();
        return activeTweens.ContainsKey(tag);
    }

    public int GetActiveTweenCount()
    {
        CleanupExpiredTweens();
        return activeTweens.Count;
    }

    private void Interpolate(float t, GodotObject target, string property, Curve curve, Variant a, Variant b)
    {
        float sample = curve.Sample(t);
        
        Variant result = a.VariantType switch
        {
            Variant.Type.Float => Mathf.Lerp((float)a, (float)b, sample),
            Variant.Type.Vector2 => ((Vector2)a).Lerp((Vector2)b, sample),
            Variant.Type.Vector3 => ((Vector3)a).Lerp((Vector3)b, sample),
            Variant.Type.Color => ((Color)a).Lerp((Color)b, sample),
            Variant.Type.Quaternion => ((Quaternion)a).Slerp((Quaternion)b, sample),
            _ => throw new ArgumentException($"Unsupported type {a.VariantType} for interpolation.")
        };

        target.Set(property, result);
    }

    private bool CheckErrors(GodotObject target, string property)
    {
        if (target == null)
        {
            GD.PushError("Trying to reach a null value, (Tween Target)");
            return false;
        }
        
        try
        {
            target.Get(property);
            return true;
        }
        catch
        {
            GD.PushError($"Property '{property}' does not exist on {target.GetClass()}");
            return false;
        }
    }

    private void CleanupExpiredTweens()
    {
        if (activeTweens.Count == 0) return;
        
        var expiredKeys = activeTweens
            .Where(kvp => !IsInstanceValid(kvp.Value))
            .Select(kvp => kvp.Key)
            .ToList();
        
        foreach (var key in expiredKeys)
            activeTweens.Remove(key);
    }
}

public class TweenerData
{
    public string Tag { get; set; }
    public string Property { get; set; }
    public Variant[] Values { get; set; }
    public float[] Durations { get; set; }
    public Tween.TransitionType TransitionType { get; set; }
    public Tween.EaseType EaseType { get; set; }
    public Action OnFinished { get; set; }

    public float Delay { get; set; } = 0f;
    public int Loops { get; set; } = 1;
    public bool Parallel { get; set; } = false;
}

