using System.Collections.Concurrent;
using System.Reflection;
using HarmonyLib;

namespace heirowDebug.ProjectZ;

public enum ValueObservationMode { Polling, ChangeNotification, PropertyHook }

// Patch only properties explicitly selected by the user. Shared accessor patches
// are filtered by object identity and removed when their last watch is released.
internal static class PropertyObservation
{
    const string PatchId = "heirowDebug.ProjectZ.property-observation";
    static readonly Harmony harmony = new(PatchId);
    static readonly object gate = new();
    static readonly ConcurrentDictionary<MethodBase, Accessor> accessors = new();
    [ThreadStatic] static bool observing;

    sealed class Accessor(MethodInfo method, bool getter)
    {
        public readonly MethodInfo Method = method;
        public readonly bool Getter = getter;
        public readonly MethodInfo Postfix = typeof(PropertyObservation).GetMethod(
            (method.IsStatic ? "Static" : "Instance") + (getter ? "Getter" : "Setter"), BindingFlags.NonPublic | BindingFlags.Static)!;
        public volatile DebugValue[] Targets = [];
    }

    public static IDisposable Attach(DebugValue value)
    {
        if (value.Member is not PropertyInfo property || property.DeclaringType is null || property.DeclaringType.IsValueType)
            throw new NotSupportedException("Select a property on a reference type to hook its accessors.");
        var methods = new List<(MethodInfo Method, bool Getter)>();
        if (property.GetMethod is { } get) methods.Add((get, true));
        if (property.SetMethod is { } set) methods.Add((set, false));
        foreach (var (method, _) in methods)
            if (method.IsAbstract || method.ContainsGenericParameters || method.GetMethodBody() is null ||
                method.ReturnType.IsByRef || method.ReturnType.IsByRefLike || method.ReturnType.IsPointer ||
                method.DeclaringType?.Assembly == typeof(object).Assembly)
                throw new NotSupportedException("This accessor cannot be hooked; change notifications or polling remain available.");
        var attached = new List<MethodBase>();
        lock (gate)
        {
            try
            {
                foreach (var (method, getter) in methods)
                {
                    if (!accessors.TryGetValue(method, out var accessor))
                    {
                        accessor = new(method, getter);
                        accessors[method] = accessor;
                        try { harmony.Patch(method, postfix: new HarmonyMethod(accessor.Postfix)); }
                        catch { accessors.TryRemove(method, out _); throw; }
                    }
                    accessor.Targets = [.. accessor.Targets, value];
                    attached.Add(method);
                }
            }
            catch { Release(value, attached); throw; }
        }
        return new Lease(value, attached);
    }

    sealed class Lease(DebugValue value, List<MethodBase> methods) : IDisposable
    {
        int disposed;
        public void Dispose() { if (Interlocked.Exchange(ref disposed, 1) == 0) Release(value, methods); }
    }

    static void Release(DebugValue value, IEnumerable<MethodBase> methods)
    {
        lock (gate)
        {
            foreach (var method in methods)
            {
                if (!accessors.TryGetValue(method, out var accessor)) continue;
                accessor.Targets = accessor.Targets.Where(v => !ReferenceEquals(v, value)).ToArray();
                if (accessor.Targets.Length != 0) continue;
                // Remove only our postfix, preserving other instrumentation.
                harmony.Unpatch(method, accessor.Postfix);
                accessors.TryRemove(method, out _);
            }
        }
    }

    static void InstanceGetter(MethodBase __originalMethod, object __instance, object? __result) => Observe(__originalMethod, __instance, __result);
    static void StaticGetter(MethodBase __originalMethod, object? __result) => Observe(__originalMethod, null, __result);
    static void InstanceSetter(MethodBase __originalMethod, object __instance) => Observe(__originalMethod, __instance, null);
    static void StaticSetter(MethodBase __originalMethod) => Observe(__originalMethod, null, null);

    static void Observe(MethodBase method, object? instance, object? result)
    {
        if (observing || !accessors.TryGetValue(method, out var accessor)) return;
        observing = true;
        try
        {
            foreach (var value in accessor.Targets)
            {
                if (!value.MatchesInstance(instance) || !value.IsObservationEnabled) continue;
                // A setter may clamp/normalize its input. Read its final value,
                // suppressing our getter callback instead of recording __0.
                try { value.Record(accessor.Getter ? result : value.Read(), DateTime.UtcNow); }
                catch (Exception ex) { value.RecordObservationError(ex); }
            }
        }
        finally { observing = false; }
    }
}
