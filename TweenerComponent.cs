using Godot;
using System;
using System.Collections.Generic;

public class TweenerComponent
{
    private const string DefaultGroup = "Generic";

    public Node Owner { get; private set; }

    private readonly Dictionary<string, Curve> curves = new();
    private readonly Dictionary<string, List<Tween>> tweenGroups = new();

    private Tween.TransitionType defaultTransition;
    private Tween.EaseType defaultEase;

    public TweenerComponent(Node owner, Tween.TransitionType? transitionType = null, Tween.EaseType? easeType = null)
    {
        Owner = owner;
        defaultTransition = transitionType ?? Tween.TransitionType.Cubic;
        defaultEase = easeType ?? Tween.EaseType.In;
    }

    public void AddCurve(string key, Curve curve)
    {
        curves[key] = curve;
    }

    public void Animate(GodotObject target, TweenerData data)
    {
        int count = Mathf.Min(data.Values.Length, data.Durations.Length);

        Tween tween = Owner.CreateTween();
        tween.SetTrans(data.TransitionType).SetEase(data.EaseType);
        tween.SetLoops(data.Loops);
        tween.SetParallel(data.Parallel);

        AddTweenToGroup(data.Group, tween);

        for (int i = 0; i < count; i++)
            tween.TweenProperty(target, data.Property, data.Values[i], data.Durations[i]);
        
        tween.Finished += () =>
        {
            data.OnFinished?.Invoke();
            tweenGroups[data.Group].Remove(tween);
        };
    }

    public void Animate(GodotObject target, string property, Variant[] values, float[] durations, Tween.TransitionType? transition = null, Tween.EaseType? ease = null, Action onFinished = null, string group = DefaultGroup)
    {
        TweenerData data = new TweenerData
        {
            Group = group,
            Property = property,
            Values = values,
            Durations = durations,
            TransitionType = transition ?? defaultTransition,
            EaseType = ease ?? defaultEase,
            OnFinished = onFinished
        };

        Animate(target, data);
    }

    public void AnimatePath(GodotObject target, string property, string curveKey, Variant from, Variant to, float duration = 1f, Action onFinished = null, string group = DefaultGroup)
    {
        Curve curve = curves[curveKey];
        Tween tween = Owner.CreateTween();
        
        AddTweenToGroup(group, tween);

        Callable callable = Callable.From<float>(t => Interpolate(t, target, property, curve, from, to));
        tween.TweenMethod(callable, 0f, 1f, duration);

        tween.Finished += () =>
        {
            onFinished?.Invoke();
            tweenGroups[group].Remove(tween);
        };
    }

    public void StopTweens(string group)
    {
        List<Tween> tweens = tweenGroups[group];

        foreach (Tween tween in tweens)
            tween.Kill();
    }

    public void StopAll()
    {
        foreach (List<Tween> list in tweenGroups.Values)
            foreach (Tween tween in list)
                tween.Kill();
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

    private void AddTweenToGroup(string group, Tween tween)
    {
        if (tweenGroups.TryGetValue(group, out List<Tween> list))
            list.Add(tween);
        else
            tweenGroups.Add(group, [tween]);
    }
}

public class TweenerData
{
    public string Group { get; set; }
    public string Property { get; set; }
    public Variant[] Values { get; set; }
    public float[] Durations { get; set; }
    public Tween.TransitionType TransitionType { get; set; }
    public Tween.EaseType EaseType { get; set; }

    public float Delay { get; set; } = 0f;
    public int Loops { get; set; } = 1;
    public bool Parallel { get; set; } = false;

    public Action OnFinished { get; set; }
}
