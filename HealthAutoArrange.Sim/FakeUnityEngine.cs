// Minimal, behavior-faithful UnityEngine stand-in for the HealthAutoArrange behavior
// simulator. Only the API surface touched by UnityUiAdapter/FakeGame is implemented, with
// the exact Unity semantics that matter for the sort-fight investigation:
//   * Object.Destroy defers actual hierarchy removal to the end of the frame.
//   * Object == null ("fake null") is switchable between Unity's two interpretations:
//     immediate (reports null as soon as Destroy is called) and deferred (stays non-null
//     until the end-of-frame destroy pass, making destroy-pending ghosts indistinguishable
//     from live moodles during the rebuild frame).
//   * Transform children are an ordered list; SetParent appends as the LAST sibling,
//     exactly like the game's AddMoodle.
using System;
using System.Collections.Generic;

namespace UnityEngine
{
    internal static class SimEngine
    {
        public static readonly List<Object> PendingDestroys = new List<Object>();

        /// <summary>
        /// Controls == null semantics for destroy-pending objects. true = Unity reports
        /// null immediately after Destroy(); false = null only after the end-of-frame
        /// destroy pass (the pessimistic "ghost" model).
        /// </summary>
        public static bool ImmediateFakeNull;

        public static void EndFrame()
        {
            // The end-of-frame destroy pass: objects flagged by Destroy() are actually
            // removed from the hierarchy and fully destroyed now.
            foreach (var obj in PendingDestroys)
            {
                obj.Destroyed = true;
                if (obj is GameObject go)
                {
                    go.RectTransform.RemoveFromParent();
                    go.RectTransform.Destroyed = true;
                    foreach (var c in go.Components) c.Destroyed = true;
                }
            }
            PendingDestroys.Clear();
        }
    }

    public class Object
    {
        private static int _nextInstanceId = 1;

        internal bool DestroyPending;
        internal bool Destroyed;
        internal readonly int InstanceId = _nextInstanceId++;

        public string name { get; set; }

        public int GetInstanceID() => InstanceId;

        internal bool IsFakeNull => SimEngine.ImmediateFakeNull ? (DestroyPending || Destroyed) : Destroyed;

        public static bool operator ==(Object a, Object b)
        {
            if (ReferenceEquals(a, b)) return true; // both null, or same reference
            if (ReferenceEquals(a, null)) return b != null && b.IsFakeNull;
            if (ReferenceEquals(b, null)) return a.IsFakeNull;
            return false;
        }

        public static bool operator !=(Object a, Object b) => !(a == b);

        public override bool Equals(object other)
        {
            if (other is null) return IsFakeNull;
            return this == other as Object;
        }

        public override int GetHashCode() => InstanceId;

        public static void Destroy(Object obj)
        {
            if (obj is null || obj.Destroyed || obj.DestroyPending) return;
            obj.DestroyPending = true;
            if (obj is GameObject goObj)
            {
                // Destroying a GameObject flags its transform and components as well.
                goObj.RectTransform.DestroyPending = true;
                foreach (var c in goObj.Components) c.DestroyPending = true;
            }
            else if (obj is Component comp)
            {
                // Destroying a component also dooms the whole GameObject, Unity-style.
                comp.gameObject.DestroyPending = true;
                comp.gameObject.RectTransform.DestroyPending = true;
                foreach (var c in comp.gameObject.Components) c.DestroyPending = true;
            }
            SimEngine.PendingDestroys.Add(obj);
        }
    }

    public class Component : Object
    {
        internal GameObject Owner;

        protected Component()
        {
        }

        public GameObject gameObject => Owner;

        public Transform transform => Owner?.RectTransform;

        public T GetComponent<T>() where T : class
        {
            if (Owner == null || IsFakeNull) return null;
            foreach (var c in Owner.Components)
            {
                if (c != null && c is T typed && !c.IsFakeNull) return typed;
            }
            return null;
        }
    }

    public class Transform : Component
    {
        internal readonly List<Transform> Children = new List<Transform>();

        internal Transform()
        {
        }

        public Transform parent => ParentField;
        internal Transform ParentField;

        public int childCount => Children.Count;

        public Transform GetChild(int index)
        {
            if (index < 0 || index >= Children.Count) throw new ArgumentOutOfRangeException(nameof(index));
            return Children[index];
        }

        public int GetSiblingIndex()
        {
            if (ParentField == null) return 0;
            var idx = ParentField.Children.IndexOf(this);
            return idx < 0 ? 0 : idx;
        }

        public void SetSiblingIndex(int index)
        {
            if (ParentField == null) return;
            var list = ParentField.Children;
            list.Remove(this);
            if (index < 0) index = 0;
            if (index > list.Count) index = list.Count;
            list.Insert(index, this);
        }

        public void SetParent(Transform parent)
        {
            ParentField?.Children.Remove(this);
            ParentField = parent;
            if (parent != null) parent.Children.Add(this); // appends as LAST sibling, like the game
        }

        internal void RemoveFromParent()
        {
            ParentField?.Children.Remove(this);
            ParentField = null;
        }

        /// <summary>Unity Transform enumerates its children; used by foreach in game code.</summary>
        public System.Collections.IEnumerator GetEnumerator()
        {
            // Snapshot: ClearMoodles destroys children while iterating over them.
            return Children.ToArray().GetEnumerator();
        }

        public Vector3 localPosition { get; set; }
        public Vector3 localScale { get; set; } = Vector3.one;
        public Quaternion localRotation { get; set; } = Quaternion.identity;
    }

    public class RectTransform : Transform
    {
        public Vector2 anchoredPosition { get; set; }
        public Vector2 anchorMin { get; set; }
        public Vector2 anchorMax { get; set; }
    }

    public sealed class GameObject : Object
    {
        internal readonly RectTransform RectTransform = new RectTransform();
        internal readonly List<Component> Components = new List<Component>();

        public GameObject(string name)
        {
            this.name = name;
            RectTransform.Owner = this;
            Components.Add(RectTransform);
        }

        public Transform transform => RectTransform;

        public bool activeInHierarchy => !IsFakeNull;

        public bool activeSelf => true;

        public T AddComponent<T>() where T : Component, new()
        {
            var instance = new T();
            instance.Owner = this;
            Components.Add(instance);
            return instance;
        }

        public T GetComponent<T>() where T : class
        {
            if (IsFakeNull) return null;
            foreach (var c in Components)
            {
                if (c != null && c is T typed && !c.IsFakeNull) return typed;
            }
            return null;
        }

        public void SetActive(bool value)
        {
            // Not modeled beyond existence; the sort pipeline only reads activeInHierarchy.
        }
    }

    public class MonoBehaviour : Component
    {
        protected MonoBehaviour()
        {
        }
    }

    public static class Time
    {
        public static int frameCount;
        public static float realtimeSinceStartup;
        public static float unscaledTime;
        public static float unscaledDeltaTime;
        public static float deltaTime;
        public static float timeScale = 1f;
    }

    public static class Mathf
    {
        public static int RoundToInt(float f) => (int)MathF.Round(f, MidpointRounding.AwayFromZero);
        public static float Abs(float f) => MathF.Abs(f);
        public static float Lerp(float a, float b, float t) => a + (b - a) * Math.Clamp(t, 0f, 1f);
        public static float Sin(float f) => MathF.Sin(f);

        public static float PingPong(float t, float length)
        {
            if (length <= 0f) return 0f;
            var rem = MathF.Abs(t) % (2f * length);
            return rem <= length ? rem : 2f * length - rem;
        }
    }

    public struct Vector2
    {
        public float x;
        public float y;

        public Vector2(float x, float y) { this.x = x; this.y = y; }

        public static Vector2 zero => new Vector2(0f, 0f);
        public static Vector2 one => new Vector2(1f, 1f);
        public static Vector2 up => new Vector2(0f, 1f);

        public static Vector2 operator +(Vector2 a, Vector2 b) => new Vector2(a.x + b.x, a.y + b.y);
        public static Vector2 operator -(Vector2 a, Vector2 b) => new Vector2(a.x - b.x, a.y - b.y);
        public static Vector2 operator *(Vector2 a, float d) => new Vector2(a.x * d, a.y * d);
        public static Vector2 operator *(float d, Vector2 a) => new Vector2(a.x * d, a.y * d);

        public static implicit operator Vector3(Vector2 v) => new Vector3(v.x, v.y, 0f);

        public override string ToString() => $"({x:0.##}, {y:0.##})";
    }

    public struct Vector3
    {
        public float x;
        public float y;
        public float z;

        public Vector3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }

        public static Vector3 zero => new Vector3(0f, 0f, 0f);
        public static Vector3 one => new Vector3(1f, 1f, 1f);

        public static Vector3 Lerp(Vector3 a, Vector3 b, float t)
        {
            t = Math.Clamp(t, 0f, 1f);
            return new Vector3(a.x + (b.x - a.x) * t, a.y + (b.y - a.y) * t, a.z + (b.z - a.z) * t);
        }

        public static Vector3 operator *(Vector3 a, float d) => new Vector3(a.x * d, a.y * d, a.z * d);
        public static Vector3 operator +(Vector3 a, Vector3 b) => new Vector3(a.x + b.x, a.y + b.y, a.z + b.z);

        public override string ToString() => $"({x:0.##}, {y:0.##}, {z:0.##})";
    }

    public struct Quaternion
    {
        public static Quaternion identity => default;
    }

    public struct Color
    {
        public float r, g, b, a;
        public Color(float r, float g, float b, float a) { this.r = r; this.g = g; this.b = b; this.a = a; }
    }
}

namespace UnityEngine.UI
{
    using UnityEngine;

    public class HorizontalLayoutGroup : MonoBehaviour { }
    public class VerticalLayoutGroup : MonoBehaviour { }
    public class GridLayoutGroup : MonoBehaviour { }
}
