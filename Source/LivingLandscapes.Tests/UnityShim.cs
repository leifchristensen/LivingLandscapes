using System;

namespace UnityEngine;

/// <summary>
/// The subset of UnityEngine.Mathf that the linked, game-free source files use, so they compile and run outside the game.
/// Add members only as a linked file needs them, with the same semantics as Unity's.
/// </summary>
public static class Mathf
{
    public const float PI = (float)Math.PI;

    public static float Abs(float v) => Math.Abs(v);
    public static float Min(float a, float b) => Math.Min(a, b);
    public static float Max(float a, float b) => Math.Max(a, b);
    public static int Min(int a, int b) => Math.Min(a, b);
    public static int Max(int a, int b) => Math.Max(a, b);
    public static float Clamp01(float v) => v < 0f ? 0f : (v > 1f ? 1f : v);
    public static float Lerp(float a, float b, float t) => a + (b - a) * Clamp01(t);
    public static float Clamp(float v, float min, float max) => v < min ? min : (v > max ? max : v);
    public static int Clamp(int v, int min, int max) => v < min ? min : (v > max ? max : v);
    public static float Sqrt(float v) => (float)Math.Sqrt(v);
    public static float Pow(float v, float p) => (float)Math.Pow(v, p);
    public static float Exp(float v) => (float)Math.Exp(v);
    public static float Cos(float v) => (float)Math.Cos(v);
    public static float Atan2(float y, float x) => (float)Math.Atan2(y, x);
    public static int CeilToInt(float v) => (int)Math.Ceiling(v);
}

/// <summary>The subset of UnityEngine.Vector2 the linked source files use (same semantics as Unity's).</summary>
public struct Vector2 : IEquatable<Vector2>
{
    public float x, y;

    public Vector2(float x, float y)
    {
        this.x = x;
        this.y = y;
    }

    public static float Dot(Vector2 a, Vector2 b) => a.x * b.x + a.y * b.y;
    public static Vector2 zero => new Vector2(0f, 0f);
    public float magnitude => (float)Math.Sqrt(x * x + y * y);
    public Vector2 normalized
    {
        get
        {
            float m = magnitude;
            return m > 1E-05f ? new Vector2(x / m, y / m) : zero;
        }
    }

    public static Vector2 operator +(Vector2 a, Vector2 b) => new Vector2(a.x + b.x, a.y + b.y);
    public static Vector2 operator -(Vector2 a, Vector2 b) => new Vector2(a.x - b.x, a.y - b.y);
    public static Vector2 operator *(Vector2 a, float d) => new Vector2(a.x * d, a.y * d);
    public static bool operator ==(Vector2 a, Vector2 b) => a.x == b.x && a.y == b.y;
    public static bool operator !=(Vector2 a, Vector2 b) => !(a == b);
    public bool Equals(Vector2 other) => this == other;
    public override bool Equals(object obj) => obj is Vector2 v && this == v;
    public override int GetHashCode() => HashCode.Combine(x, y);
}
